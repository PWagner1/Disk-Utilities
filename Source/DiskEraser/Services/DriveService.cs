using System.ComponentModel;

namespace DiskEraser.Services;

/// <summary>
/// Service for enumerating logical drives and formatting volumes via WMI (Win32_Volume).
/// Native format targets are NTFS, FAT, FAT32, exFAT, and ReFS. Linux/Apple options are listed but disabled in the UI
/// because stock Windows typically returns "Unsupported file system".
/// </summary>
public static class DriveService
{
    /// <summary>Drive letter that must never be formatted (Windows system volume).</summary>
    public const string ProtectedDriveLetter = "C:";

    /// <summary>
    /// Presets for free-space overwrite before format. Not ATA Secure Erase / NVMe Sanitize and not a standards certification.
    /// </summary>
    public static readonly (string DisplayName, int Passes)[] SecureWipePresets =
        new (string DisplayName, int Passes)[]
        {
            ("1 pass — free-space overwrite", 1),
            ("3 passes — free-space overwrite (higher effort)", 3),
            ("7 passes — free-space overwrite (highest effort)", 7)
        };

    /// <summary>File system option for the format UI.</summary>
    /// <param name="Name">File system name passed to WMI Format.</param>
    /// <param name="IsNativelySupported">True if stock Windows can typically format this via WMI.</param>
    public readonly record struct FileSystemOption(string Name, bool IsNativelySupported);

    /// <summary>
    /// File systems shown in the format UI. Only <see cref="FileSystemOption.IsNativelySupported"/> options are selectable.
    /// </summary>
    public static readonly FileSystemOption[] FormatFileSystems =
        new FileSystemOption[]
        {
            new("NTFS", true),
            new("FAT32", true),
            new("FAT", true),
            new("exFAT", true),
            new("ReFS", true),
            new("ext2", false),
            new("ext3", false),
            new("ext4", false),
            new("Btrfs", false),
            new("XFS", false),
            new("HFS", false),
            new("HFS+", false),
            new("APFS", false)
        };

    /// <summary>Legacy list of all format UI names (native + non-native).</summary>
    public static readonly string[] SupportedFileSystems =
        FormatFileSystems.Select(f => f.Name).ToArray();

    private static readonly string[] ProtectedLabelHints =
        new[]
        {
            "SYSTEM RESERVED", "SYSTEM", "EFI", "ESP", "RECOVERY", "WINDOWS RE", "WINRE", "BOOT", "MSR"
        };

    /// <summary>
    /// Returns true if the volume must not be formatted or wiped (system/boot/EFI/recovery/BitLocker system volumes).
    /// </summary>
    public static bool IsProtectedVolume(
        string? driveLetter,
        string? label = null,
        bool? bootVolume = null,
        bool? systemVolume = null,
        string? partitionType = null)
    {
        var letter = NormalizeLetter(driveLetter);
        if (!string.IsNullOrEmpty(letter) &&
            letter.Equals(ProtectedDriveLetter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (bootVolume == true || systemVolume == true)
        {
            return true;
        }

        if (LabelLooksProtected(label))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(partitionType))
        {
            string type = partitionType;
            if (type.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("EFI", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Reserved", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Looks up volume flags/label via WMI and returns whether the drive letter is protected.</summary>
    public static bool IsProtectedDriveLetter(string? driveLetter)
    {
        var letter = NormalizeLetter(driveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return false;
        }

        if (letter.Equals(ProtectedDriveLetter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label, BootVolume, SystemVolume FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var volLetter = NormalizeLetter(obj["DriveLetter"]?.ToString());
                if (!letter.Equals(volLetter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return IsProtectedVolume(
                    volLetter,
                    obj["Label"]?.ToString(),
                    obj["BootVolume"] as bool?,
                    obj["SystemVolume"] as bool?);
            }
        }
        catch
        {
            // Fall through to C:-only protection if WMI fails
        }

        return false;
    }

    private static bool LabelLooksProtected(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        string normalized = label.Trim().ToUpperInvariant();
        foreach (string hint in ProtectedLabelHints)
        {
            if (normalized == hint || normalized.Contains(hint, StringComparison.Ordinal))
            {
                // Avoid treating a user volume named e.g. "My Boot Backup" too aggressively:
                // require exact match for short ambiguous tokens SYSTEM/BOOT/MSR.
                if (hint is "SYSTEM" or "BOOT" or "MSR")
                {
                    if (normalized == hint || normalized.StartsWith(hint + " ", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static string NormalizeLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return string.Empty;
        }

        var letter = driveLetter.Trim().TrimEnd('\\', '/').ToUpperInvariant();
        if (letter.Length == 1 && char.IsLetter(letter[0]))
        {
            return letter + ":";
        }

        if (letter.Length >= 2 && char.IsLetter(letter[0]) && letter[1] == ':')
        {
            return letter[..2];
        }

        if (!letter.EndsWith(':'))
        {
            letter += ":";
        }

        return letter;
    }

    /// <summary>
    /// Enumerates logical volumes that have a drive letter and are safe to list for formatting.
    /// Excludes protected system/boot/EFI/recovery volumes (including C:).
    /// </summary>
    /// <returns>List of drive items; empty on WMI or permission errors.</returns>
    public static IReadOnlyList<DriveItem> GetFormattableDrives()
    {
        var list = new List<DriveItem>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label, FileSystem, Capacity, FreeSpace, BootVolume, SystemVolume FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var letter = NormalizeLetter(obj["DriveLetter"]?.ToString());
                string label = obj["Label"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(letter) ||
                    IsProtectedVolume(letter, label, obj["BootVolume"] as bool?, obj["SystemVolume"] as bool?))
                {
                    continue;
                }

                list.Add(new DriveItem
                {
                    DriveLetter = letter,
                    Label = label,
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

    /// <summary>Runs <see cref="GetFormattableDrives"/> on a thread-pool thread for non-blocking UI.</summary>
    public static Task<IReadOnlyList<DriveItem>> GetFormattableDrivesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetFormattableDrives, cancellationToken);

    /// <summary>
    /// Gets the physical disk and partition layout for the diagram.
    /// Uses Win32_DiskDrive, Win32_DiskPartition, Win32_LogicalDiskToPartition, and Win32_Volume.
    /// </summary>
    /// <returns>List of disks with their partitions; empty on WMI or permission errors.</returns>
    public static IReadOnlyList<DiskInfo> GetDiskPartitionLayout()
    {
        var disks = new List<DiskInfo>();
        try
        {
            var volumeInfoByLetter = GetVolumeInfoByDriveLetter();

            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Size, Model, Status FROM Win32_DiskDrive");
            foreach (ManagementBaseObject diskObj in diskSearcher.Get())
            {
                using var disk = (ManagementObject)diskObj;
                int diskIndex = Convert.ToInt32(disk["Index"]);
                ulong diskSize = disk["Size"] != null ? Convert.ToUInt64(disk["Size"]) : 0;
                string model = disk["Model"]?.ToString()?.Trim() ?? $"Disk {diskIndex}";
                string status = disk["Status"]?.ToString()?.Trim() ?? string.Empty;

                var diskInfo = new DiskInfo
                {
                    DiskIndex = diskIndex,
                    Model = model,
                    Size = diskSize,
                    Status = status,
                    Partitions = []
                };

                string diskPath = disk["DeviceID"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(diskPath))
                {
                    disks.Add(diskInfo);
                    continue;
                }

                using var partitionSearcher = new ManagementObjectSearcher(
                    $"SELECT DeviceID, Index, Size, StartingOffset, Type FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");
                foreach (ManagementBaseObject partObj in partitionSearcher.Get())
                {
                    using var partition = (ManagementObject)partObj;
                    int partIndex = Convert.ToInt32(partition["Index"]);
                    ulong partSize = partition["Size"] != null ? Convert.ToUInt64(partition["Size"]) : 0;
                    ulong partOffset = partition["StartingOffset"] != null ? Convert.ToUInt64(partition["StartingOffset"]) : 0;
                    string partitionType = partition["Type"]?.ToString() ?? string.Empty;

                    string driveLetter = string.Empty;
                    string label = string.Empty;
                    string fileSystem = string.Empty;
                    ulong capacity = partSize;
                    ulong freeSpace = 0;
                    bool? bootVolume = null;
                    bool? systemVolume = null;

                    try
                    {
                        using var logicalDisks = partition.GetRelated("Win32_LogicalDisk");
                        foreach (ManagementBaseObject ldObj in logicalDisks)
                        {
                            using var ld = (ManagementObject)ldObj;
                            driveLetter = NormalizeLetter(ld["DeviceID"]?.ToString());
                            if (!string.IsNullOrEmpty(driveLetter) && volumeInfoByLetter.TryGetValue(driveLetter, out var vol))
                            {
                                label = vol.Label;
                                fileSystem = vol.FileSystem;
                                capacity = vol.Capacity;
                                freeSpace = vol.FreeSpace;
                                bootVolume = vol.BootVolume;
                                systemVolume = vol.SystemVolume;
                            }
                            break;
                        }
                    }
                    catch
                    {
                        // Partition may have no drive letter (e.g. recovery, EFI)
                    }

                    bool protectedPartition = IsProtectedVolume(driveLetter, label, bootVolume, systemVolume, partitionType);
                    // Letterless FAT/FAT32 under ~1 GB is typically the EFI system partition.
                    if (!protectedPartition &&
                        string.IsNullOrEmpty(driveLetter) &&
                        partSize > 0 && partSize <= 1024UL * 1024 * 1024 &&
                        (fileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ||
                         fileSystem.Equals("FAT", StringComparison.OrdinalIgnoreCase) ||
                         string.IsNullOrEmpty(fileSystem)))
                    {
                        protectedPartition = true;
                    }

                    diskInfo.Partitions.Add(new PartitionInfo
                    {
                        PartitionIndex = partIndex,
                        Size = partSize,
                        Offset = partOffset,
                        DriveLetter = driveLetter,
                        Label = label,
                        FileSystem = fileSystem,
                        Capacity = capacity,
                        FreeSpace = freeSpace,
                        IsProtected = protectedPartition
                    });
                }

                disks.Add(diskInfo);
            }

            disks.Sort((a, b) => a.DiskIndex.CompareTo(b.DiskIndex));
        }
        catch
        {
            // Return empty list on permission or WMI errors
        }

        return disks;
    }

    /// <summary>Runs <see cref="GetDiskPartitionLayout"/> on a thread-pool thread for non-blocking UI.</summary>
    public static Task<IReadOnlyList<DiskInfo>> GetDiskPartitionLayoutAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetDiskPartitionLayout, cancellationToken);

    /// <summary>Builds a map of drive letter to volume metadata from Win32_Volume.</summary>
    private static Dictionary<string, (string Label, string FileSystem, ulong Capacity, ulong FreeSpace, bool? BootVolume, bool? SystemVolume)> GetVolumeInfoByDriveLetter()
    {
        var map = new Dictionary<string, (string, string, ulong, ulong, bool?, bool?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label, FileSystem, Capacity, FreeSpace, BootVolume, SystemVolume FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var letter = NormalizeLetter(obj["DriveLetter"]?.ToString());
                if (string.IsNullOrEmpty(letter))
                {
                    continue;
                }

                map[letter] = (
                    obj["Label"]?.ToString() ?? string.Empty,
                    obj["FileSystem"]?.ToString() ?? string.Empty,
                    obj["Capacity"] != null ? Convert.ToUInt64(obj["Capacity"]) : 0,
                    obj["FreeSpace"] != null ? Convert.ToUInt64(obj["FreeSpace"]) : 0,
                    obj["BootVolume"] as bool?,
                    obj["SystemVolume"] as bool?);
            }
        }
        catch { /* ignore */ }
        return map;
    }

    /// <summary>
    /// Overwrites free space on the volume with cryptographically random data (multiple passes), then the caller typically formats.
    /// This is not full-disk Secure Erase or NVMe Sanitize. Does not allow wiping protected volumes.
    /// </summary>
    /// <param name="driveLetter">Drive letter with or without colon (e.g. "D" or "D:").</param>
    /// <param name="passes">Number of free-space overwrite passes.</param>
    /// <param name="progress">Optional wipe progress including ETA.</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>True if wipe completed; false if drive is protected or error.</returns>
    public static async Task<bool> SecureWipeAsync(
        string driveLetter,
        int passes,
        IProgress<WipeProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var letter = NormalizeLetter(driveLetter);
        if (string.IsNullOrEmpty(letter) || IsProtectedDriveLetter(letter))
        {
            return false;
        }

        string rootPath = letter + Path.DirectorySeparatorChar;
        const int chunkSize = 1024 * 1024 * 64; // 64 MB per chunk
        byte[] buffer = new byte[chunkSize];

        await Task.Run(() =>
        {
            long totalBytesToWipe = GetFreeSpaceBytes(rootPath) * Math.Max(1, passes);
            long totalWritten = 0;
            var sw = Stopwatch.StartNew();
            long lastReportAt = 0;

            for (int pass = 0; pass < passes; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int fileIndex = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long freeNow = GetFreeSpaceBytes(rootPath);
                    if (freeNow <= 0)
                    {
                        break;
                    }

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
                                if (toWrite <= 0)
                                {
                                    break;
                                }

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

                                // Throttle UI reports (~4 Hz)
                                if (sw.ElapsedMilliseconds - lastReportAt >= 250 || totalWritten >= totalBytesToWipe)
                                {
                                    lastReportAt = sw.ElapsedMilliseconds;
                                    double pct = totalBytesToWipe > 0 ? (double)totalWritten / totalBytesToWipe * 100.0 : 0;
                                    var eta = WipeProgress.EstimateRemaining(sw.Elapsed, totalWritten, totalBytesToWipe);
                                    progress?.Report(new WipeProgress(
                                        pass + 1, passes, Math.Min(100, pct), totalWritten, totalBytesToWipe, eta));
                                }
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        }
                        catch
                        {
                            // ignore cleanup failures
                        }
                    }
                    fileIndex++;
                    if (GetFreeSpaceBytes(rootPath) <= 0)
                    {
                        break;
                    }
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(new WipeProgress(passes, passes, 100, 0, 0, EstimatedRemaining: null));
        return true;
    }

    /// <summary>
    /// Formats the given volume via WMI Win32_Volume.Format.
    /// Does not allow formatting protected system/boot/EFI/recovery volumes.
    /// Rejects file systems that are not natively supported on stock Windows.
    /// </summary>
    /// <param name="driveLetter">Drive letter with or without colon (e.g. "D" or "D:").</param>
    /// <param name="fileSystem">File system (e.g. NTFS, FAT32, exFAT, ReFS).</param>
    /// <param name="quickFormat">True to quick format (no bad sector scan).</param>
    /// <param name="clusterSize">Allocation unit size in bytes; 0 to use default.</param>
    /// <param name="volumeLabel">Volume label (optional).</param>
    /// <param name="enableCompression">Compress files to save space; supported for NTFS (WMI may not implement on all systems).</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <param name="phaseProgress">Optional format-phase status messages for the UI.</param>
    /// <returns>Result: 0 = success; non-zero = WMI error code.</returns>
    public static async Task<uint> FormatAsync(
        string driveLetter,
        string fileSystem,
        bool quickFormat,
        uint clusterSize,
        string? volumeLabel,
        bool enableCompression,
        CancellationToken cancellationToken = default,
        IProgress<string>? phaseProgress = null)
    {
        var letter = NormalizeLetter(driveLetter);
        if (string.IsNullOrEmpty(letter) || IsProtectedDriveLetter(letter))
        {
            return 3; // Access denied
        }

        if (!FormatFileSystems.Any(f => f.IsNativelySupported &&
            f.Name.Equals(fileSystem, StringComparison.OrdinalIgnoreCase)))
        {
            return 1; // Unsupported file system
        }

        phaseProgress?.Report(quickFormat
            ? $"Formatting {letter} as {fileSystem} (quick)…"
            : $"Formatting {letter} as {fileSystem} (full)…");

        uint result = 18; // Unknown error
        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Volume WHERE DriveLetter = '{letter}'");
                using var volumes = searcher.Get();
                foreach (var o in volumes)
                {
                    var vol = (ManagementObject)o;
                    try
                    {
                        uint size = clusterSize > 0 ? clusterSize : 4096;
                        object[] args = [fileSystem, quickFormat, size, volumeLabel ?? string.Empty, enableCompression];
                        result = (uint)vol.InvokeMethod("Format", args);
                    }
                    finally
                    {
                        vol.Dispose();
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"WMI Format failed for {letter}.", ex, nameof(DriveService));
                result = 18;
            }
        }, cancellationToken).ConfigureAwait(false);

        phaseProgress?.Report(result == 0 ? "Format completed." : "Format finished with errors.");
        return result;
    }

    /// <summary>Gets free space in bytes for the given root path (e.g. "D:\").</summary>
    private static long GetFreeSpaceBytes(string rootPath)
    {
        try
        {
            var di = new DriveInfo(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return di.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Opens Windows Disk Management (diskmgmt.msc) for partition and drive letter management.</summary>
    public static void OpenDiskManagement()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "diskmgmt.msc",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if shell execute fails
        }
    }

    /// <summary>
    /// Tries to assign a new drive letter to the volume that currently has the given letter.
    /// Uses diskpart; may require administrator rights. Use <see cref="OpenDiskManagement"/> if this fails.
    /// </summary>
    /// <param name="currentLetter">Current drive letter (e.g. "D" or "D:").</param>
    /// <param name="newLetter">New drive letter (e.g. "Z" or "Z:").</param>
    /// <returns>True if diskpart reported success; false otherwise.</returns>
    public static bool TryAssignDriveLetter(string? currentLetter, string? newLetter)
    {
        var cur = (currentLetter ?? string.Empty).Trim().ToUpperInvariant().TrimEnd(':');
        var @new = (newLetter ?? string.Empty).Trim().ToUpperInvariant().TrimEnd(':');
        if (cur.Length != 1 || @new.Length != 1 || cur == @new)
        {
            return false;
        }

        if (cur[0] < 'A' || cur[0] > 'Z' || @new[0] < 'A' || @new[0] > 'Z')
        {
            return false;
        }

        try
        {
            string diskpartPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");
            if (!File.Exists(diskpartPath))
            {
                return false;
            }

            int? volumeNumber = GetVolumeNumberFromDiskPart(diskpartPath, cur);
            if (volumeNumber == null)
            {
                return false;
            }

            var script = $"select volume {volumeNumber}\r\nassign letter={@new}\r\n";
            var scriptPath = Path.Combine(Path.GetTempPath(), $"dw_assign_{Guid.NewGuid():N}.txt");
            File.WriteAllText(scriptPath, script);
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = diskpartPath,
                    Arguments = $"/s \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                p.Start();
                p.WaitForExit(15000);
                return p.ExitCode == 0;
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { /* ignore */ }
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Most recent diskpart stdout/stderr (for UI status). Cleared on each run.</summary>
    public static string LastDiskPartOutput { get; private set; } = string.Empty;

    /// <summary>Runs a diskpart script (one command per line). Returns true if exit code 0.</summary>
    public static bool RunDiskPartScript(IEnumerable<string> commands, out string output)
    {
        var result = RunDiskPartScriptDetailed(commands);
        output = result.Output;
        return result.Success;
    }

    /// <summary>Runs diskpart and returns structured success/exit code/output; logs failures.</summary>
    public static DiskPartResult RunDiskPartScriptDetailed(IEnumerable<string> commands)
    {
        LastDiskPartOutput = string.Empty;
        try
        {
            string diskpartPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");
            if (!File.Exists(diskpartPath))
            {
                LastDiskPartOutput = "diskpart.exe not found.";
                AppLog.Error(LastDiskPartOutput, source: nameof(DriveService));
                return new DiskPartResult(false, -1, LastDiskPartOutput);
            }

            string script = string.Join("\r\n", commands) + "\r\n";
            var scriptPath = Path.Combine(Path.GetTempPath(), $"dw_script_{Guid.NewGuid():N}.txt");
            File.WriteAllText(scriptPath, script);
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = diskpartPath,
                    Arguments = $"/s \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120_000);
                string output = (stdout + stderr).Trim();
                LastDiskPartOutput = output;
                bool ok = p.ExitCode == 0;
                if (!ok)
                {
                    AppLog.Warning(
                        $"diskpart exited with code {p.ExitCode}. Output: {TruncateForLog(output)}",
                        source: nameof(DriveService));
                }

                return new DiskPartResult(ok, p.ExitCode, output);
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { /* ignore */ }
            }
        }
        catch (Win32Exception)
        {
            LastDiskPartOutput = "Administrator rights required.";
            AppLog.Warning(LastDiskPartOutput, source: nameof(DriveService));
            return new DiskPartResult(false, -1, LastDiskPartOutput);
        }
        catch (Exception ex)
        {
            LastDiskPartOutput = ex.Message;
            AppLog.Error("diskpart failed.", ex, nameof(DriveService));
            return new DiskPartResult(false, -1, LastDiskPartOutput);
        }
    }

    private static string TruncateForLog(string text, int max = 500)
        => text.Length <= max ? text : text[..max] + "…";

    /// <summary>Result of a diskpart script run.</summary>
    public readonly record struct DiskPartResult(bool Success, int ExitCode, string Output);

    /// <summary>Builds diskpart commands to create a primary partition (size 0 = use all unallocated).</summary>
    public static IReadOnlyList<string> BuildCreatePartitionCommands(int diskIndex, uint sizeMb)
    {
        var commands = new List<string> { $"select disk {diskIndex}" };
        commands.Add(sizeMb > 0 ? $"create partition primary size={sizeMb}" : "create partition primary");
        return commands;
    }

    /// <summary>Builds diskpart commands to delete a partition by diskpart partition number (1-based).</summary>
    public static IReadOnlyList<string> BuildDeletePartitionCommands(int diskIndex, int diskpartPartitionNumber)
        => new List<string> { $"select disk {diskIndex}", $"select partition {diskpartPartitionNumber}", "delete partition" };

    /// <summary>Builds diskpart commands to extend a volume.</summary>
    public static IReadOnlyList<string> BuildExtendVolumeCommands(int volumeNumber, uint? sizeMb)
    {
        var commands = new List<string> { $"select volume {volumeNumber}" };
        commands.Add(sizeMb is > 0 ? $"extend size={sizeMb}" : "extend");
        return commands;
    }

    /// <summary>Builds diskpart commands to shrink a volume by the desired MB.</summary>
    public static IReadOnlyList<string> BuildShrinkVolumeCommands(int volumeNumber, uint desiredMb)
        => new List<string> { $"select volume {volumeNumber}", $"shrink desired={desiredMb}" };

    /// <summary>Creates a primary partition on the given disk. Size in MB; 0 = use all unallocated. May require administrator.</summary>
    public static bool CreatePartition(int diskIndex, uint sizeMb)
        => RunDiskPartScript(BuildCreatePartitionCommands(diskIndex, sizeMb), out _);

    /// <summary>Deletes the given partition on the disk. May require administrator. Cannot delete system/boot partitions from user mode.</summary>
    public static bool DeletePartition(int diskIndex, int partitionIndex)
    {
        int? partNum = GetPartitionNumberFromDiskPart(diskIndex, partitionIndex);
        return partNum != null && RunDiskPartScript(BuildDeletePartitionCommands(diskIndex, partNum.Value), out _);
    }

    /// <summary>Gets the diskpart partition number (1-based) for the given disk and 0-based partition index. Runs list partition and parses.</summary>
    private static int? GetPartitionNumberFromDiskPart(int diskIndex, int partitionIndex)
    {
        if (!RunDiskPartScript([$"select disk {diskIndex}", "list partition"], out string output))
        {
            return null;
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(output, @"Partition\s+(\d+)");
        if (partitionIndex >= 0 && partitionIndex < matches.Count)
        {
            return int.TryParse(matches[partitionIndex].Groups[1].Value, out int num) ? num : null;
        }

        return null;
    }

    /// <summary>Extends the volume by the given size in MB. If sizeMB is 0 or null, extends to use all contiguous unallocated space. May require administrator.</summary>
    public static bool ExtendVolume(int volumeNumber, uint? sizeMb)
        => RunDiskPartScript(BuildExtendVolumeCommands(volumeNumber, sizeMb), out _);

    /// <summary>Shrinks the volume by the desired amount in MB. May require administrator.</summary>
    public static bool ShrinkVolume(int volumeNumber, uint desiredMb)
        => desiredMb != 0 && RunDiskPartScript(BuildShrinkVolumeCommands(volumeNumber, desiredMb), out _);

    /// <summary>Assigns a drive letter to a partition (by disk and partition index). May require administrator.</summary>
    public static bool AssignLetterToPartition(int diskIndex, int partitionIndex, string? letter)
    {
        var l = (letter ?? string.Empty).Trim().ToUpperInvariant().TrimEnd(':');
        if (l.Length != 1 || l[0] < 'A' || l[0] > 'Z')
        {
            return false;
        }

        int? partNum = GetPartitionNumberFromDiskPart(diskIndex, partitionIndex);
        return partNum != null && RunDiskPartScript([$"select disk {diskIndex}", $"select partition {partNum}", "assign letter=" + l], out _);
    }

    /// <summary>Gets the volume number (for diskpart) for the given drive letter (e.g. "D" or "D:"). Returns null if not found.</summary>
    public static int? GetVolumeNumberForDriveLetter(string? driveLetter)
    {
        var letter = (driveLetter ?? string.Empty).Trim().ToUpperInvariant().TrimEnd(':');
        return letter.Length != 1 ? null : GetVolumeNumberFromDiskPart(GetDiskPartPath(), letter);
    }

    /// <summary>Removes the drive letter from the volume. May require administrator.</summary>
    public static bool RemoveDriveLetter(string? driveLetter)
    {
        var letter = (driveLetter ?? string.Empty).Trim().ToUpperInvariant().TrimEnd(':');
        if (letter.Length != 1 || letter == "C")
        {
            return false;
        }

        int? volNum = GetVolumeNumberFromDiskPart(GetDiskPartPath(), letter);
        return volNum != null && RunDiskPartScript([$"select volume {volNum}", "remove letter=" + letter], out _);
    }

    private static string GetDiskPartPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");
    }

    /// <summary>Runs diskpart "list volume", parses output for the given drive letter, returns Volume ### or null.</summary>
    private static int? GetVolumeNumberFromDiskPart(string diskpartPath, string driveLetter)
    {
        var script = "list volume\r\n";
        var scriptPath = Path.Combine(Path.GetTempPath(), $"dw_list_{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, script);
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = diskpartPath,
                Arguments = $"/s \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);

            // Lines like "  Volume 3     E   ..." or "  Volume 0     D   ..." - find line containing " D " (space + letter + space)
            string match = $" {driveLetter} ";
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains(match, StringComparison.OrdinalIgnoreCase))
                {
                    var volMatch = System.Text.RegularExpressions.Regex.Match(line, @"Volume\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out int num))
                    {
                        return num;
                    }
                }
            }
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Elevation required - diskpart needs administrator
            return null;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
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
