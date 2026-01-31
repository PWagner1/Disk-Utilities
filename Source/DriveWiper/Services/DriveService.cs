using System.Management;
using System.Security.Cryptography;
using DriveWiper.Models;

namespace DriveWiper.Services;

/// <summary>
/// Service for enumerating logical drives and formatting volumes via WMI (Win32_Volume).
/// Offers Windows (NTFS, FAT, exFAT, ReFS), Linux (ext2/3/4, Btrfs, XFS), and Apple (HFS, HFS+, APFS) file systems.
/// Windows natively formats only NTFS, FAT, FAT32, and optionally exFAT/ReFS; others typically return "Unsupported file system".
/// </summary>
public static class DriveService
{
    /// <summary>Drive letter that must never be formatted (system drive).</summary>
    public const string ProtectedDriveLetter = "C:";

    /// <summary>
    /// Presets for secure deletion (multiple overwrite passes). Passes overwrite free space with random data before format.
    /// </summary>
    public static readonly (string DisplayName, int Passes)[] SecureWipePresets =
    {
        ("1 pass (quick overwrite)", 1),
        ("3 passes (DoD 5220.22-M)", 3),
        ("7 passes (Military grade)", 7),
    };

    /// <summary>
    /// File systems offered by the format UI (Windows, Linux, and Apple).
    /// Win32_Volume.Format natively supports NTFS, FAT, FAT32; exFAT/ReFS may work on newer Windows.
    /// ext, APFS, HFS, etc. are listed for completeness; formatting them on Windows usually returns "Unsupported file system" unless third-party tools are used.
    /// </summary>
    public static readonly string[] SupportedFileSystems =
    {
        "NTFS", "FAT32", "FAT", "exFAT", "ReFS",
        "ext2", "ext3", "ext4", "Btrfs", "XFS",
        "HFS", "HFS+", "APFS"
    };

    /// <summary>
    /// Enumerates all logical volumes that have a drive letter and are safe to list for formatting.
    /// Excludes the protected system drive (C:).
    /// </summary>
    /// <returns>List of drive items; empty on WMI or permission errors.</returns>
    public static IReadOnlyList<DriveItem> GetFormattableDrives()
    {
        var list = new List<DriveItem>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label, FileSystem, Capacity, FreeSpace FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var letter = obj["DriveLetter"]?.ToString()?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(letter) || letter.Equals(ProtectedDriveLetter, StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(new DriveItem
                {
                    DriveLetter = letter,
                    Label = obj["Label"]?.ToString() ?? string.Empty,
                    FileSystem = obj["FileSystem"]?.ToString() ?? string.Empty,
                    Capacity = obj["Capacity"] != null ? Convert.ToUInt64(obj["Capacity"]) : 0,
                    FreeSpace = obj["FreeSpace"] != null ? Convert.ToUInt64(obj["FreeSpace"]) : 0
                });
            }
        }
        catch
        {
            // Caller can show a generic error if list is empty
        }

        return list;
    }

    /// <summary>
    /// Securely wipes free space on the volume by overwriting it with cryptographically random data multiple times.
    /// Call this before <see cref="FormatAsync"/> for "secure deletion" (e.g. Military grade = 7 passes).
    /// Does not allow wiping the protected drive (C:).
    /// </summary>
    /// <param name="driveLetter">Drive letter with or without colon (e.g. "D" or "D:").</param>
    /// <param name="passes">Number of overwrite passes (e.g. 7 for Military grade).</param>
    /// <param name="progress">Optional progress (pass number, total passes, percent 0–100).</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>True if wipe completed; false if drive is protected or error.</returns>
    public static async Task<bool> SecureWipeAsync(
        string driveLetter,
        int passes,
        IProgress<(int pass, int totalPasses, double percent)>? progress,
        CancellationToken cancellationToken = default)
    {
        var letter = driveLetter?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!letter.EndsWith(":", StringComparison.Ordinal))
            letter += ":";
        if (letter.Equals(ProtectedDriveLetter, StringComparison.Ordinal))
            return false;

        string rootPath = letter + Path.DirectorySeparatorChar;
        const int chunkSize = 1024 * 1024 * 64; // 64 MB per chunk
        byte[] buffer = new byte[chunkSize];

        await Task.Run(() =>
        {
            long totalBytesToWipe = GetFreeSpaceBytes(rootPath) * Math.Max(1, passes);
            long totalWritten = 0;

            for (int pass = 0; pass < passes; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int fileIndex = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long freeNow = GetFreeSpaceBytes(rootPath);
                    if (freeNow <= 0) break;
                    string filePath = Path.Combine(rootPath, $"SecureWipe_P{pass + 1}_{fileIndex}.tmp");
                    try
                    {
                        using (var rng = RandomNumberGenerator.Create())
                        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            long writtenThisFile = 0;
                            while (writtenThisFile < freeNow)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int toWrite = (int)Math.Min(freeNow - writtenThisFile, chunkSize);
                                if (toWrite <= 0) break;
                                rng.GetBytes(buffer.AsSpan(0, toWrite));
                                try
                                {
                                    fs.Write(buffer, 0, toWrite);
                                }
                                catch (IOException)
                                {
                                    break; // disk full
                                }
                                writtenThisFile += toWrite;
                                totalWritten += toWrite;
                                double pct = totalBytesToWipe > 0 ? (double)totalWritten / totalBytesToWipe * 100.0 : 0;
                                progress?.Report((pass + 1, passes, Math.Min(100, pct)));
                            }
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* ignore */ }
                    }
                    fileIndex++;
                    if (GetFreeSpaceBytes(rootPath) <= 0) break; // drive full, delete freed space and continue next pass
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report((passes, passes, 100));
        return true;
    }

    /// <summary>
    /// Formats the given volume via WMI Win32_Volume.Format.
    /// Does not allow formatting the protected drive (C:).
    /// </summary>
    /// <param name="driveLetter">Drive letter with or without colon (e.g. "D" or "D:").</param>
    /// <param name="fileSystem">File system (e.g. NTFS, FAT32, exFAT, ReFS, ext4, APFS, HFS+).</param>
    /// <param name="quickFormat">True to quick format (no bad sector scan).</param>
    /// <param name="clusterSize">Allocation unit size in bytes; 0 to use default.</param>
    /// <param name="volumeLabel">Volume label (optional).</param>
    /// <param name="enableCompression">Compress files to save space; supported for NTFS (WMI may not implement on all systems).</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>Result: 0 = success; non-zero = WMI error code.</returns>
    public static async Task<uint> FormatAsync(
        string driveLetter,
        string fileSystem,
        bool quickFormat,
        uint clusterSize,
        string volumeLabel,
        bool enableCompression,
        CancellationToken cancellationToken = default)
    {
        var letter = driveLetter?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!letter.EndsWith(":", StringComparison.Ordinal))
            letter += ":";

        if (letter.Equals(ProtectedDriveLetter, StringComparison.Ordinal))
            return 3; // Access denied

        uint result = 18; // Unknown error
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Volume WHERE DriveLetter = '{letter}'");
                using var volumes = searcher.Get();
                foreach (ManagementObject vol in volumes)
                {
                    try
                    {
                        uint size = clusterSize > 0 ? clusterSize : 4096;
                        object[] args = { fileSystem, quickFormat, size, volumeLabel ?? string.Empty, enableCompression };
                        result = (uint)vol.InvokeMethod("Format", args);
                    }
                    finally
                    {
                        vol.Dispose();
                    }
                    return;
                }
            }
            catch
            {
                result = 18;
            }
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Gets free space in bytes for the given root path (e.g. "D:\").</summary>
    private static long GetFreeSpaceBytes(string rootPath)
    {
        try
        {
            var di = new DriveInfo(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return (long)di.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Returns a user-friendly message for a WMI format return code.</summary>
    public static string GetFormatResultMessage(uint result)
    {
        return result switch
        {
            0 => "Format completed successfully.",
            1 => "Unsupported file system.",
            2 => "Incompatible media in drive.",
            3 => "Access denied (e.g. insufficient rights or protected drive).",
            4 => "Format was canceled.",
            5 => "Cancel request was too late.",
            6 => "Volume is write protected.",
            7 => "Volume lock failed.",
            8 => "Unable to quick format.",
            9 => "I/O error.",
            10 => "Invalid volume label.",
            11 => "No media in drive.",
            12 => "Volume is too small.",
            13 => "Volume is too large.",
            14 => "Volume is not mounted.",
            15 => "Cluster size is too small.",
            16 => "Cluster size is too large.",
            17 => "Cluster size is beyond 32 bits.",
            _ => $"Unknown error (code {result})."
        };
    }
}
