namespace DiskEraser.Services;

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
    [
        ("1 pass (quick overwrite)", 1),
        ("3 passes (DoD 5220.22-M)", 3),
        ("7 passes (Military grade)", 7)
    ];

    /// <summary>
    /// File systems offered by the format UI (Windows, Linux, and Apple).
    /// Win32_Volume.Format natively supports NTFS, FAT, FAT32; exFAT/ReFS may work on newer Windows.
    /// ext, APFS, HFS, etc. are listed for completeness; formatting them on Windows usually returns "Unsupported file system" unless third-party tools are used.
    /// </summary>
    public static readonly string[] SupportedFileSystems =
    [
        "NTFS", "FAT32", "FAT", "exFAT", "ReFS",
        "ext2", "ext3", "ext4", "Btrfs", "XFS",
        "HFS", "HFS+", "APFS"
    ];

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
                {
                    continue;
                }

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
                    $"SELECT DeviceID, Index, Size, StartingOffset FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");
                foreach (ManagementBaseObject partObj in partitionSearcher.Get())
                {
                    using var partition = (ManagementObject)partObj;
                    int partIndex = Convert.ToInt32(partition["Index"]);
                    ulong partSize = partition["Size"] != null ? Convert.ToUInt64(partition["Size"]) : 0;
                    ulong partOffset = partition["StartingOffset"] != null ? Convert.ToUInt64(partition["StartingOffset"]) : 0;

                    string driveLetter = string.Empty;
                    string label = string.Empty;
                    string fileSystem = string.Empty;
                    ulong capacity = partSize;
                    ulong freeSpace = 0;

                    try
                    {
                        using var logicalDisks = partition.GetRelated("Win32_LogicalDisk");
                        foreach (ManagementBaseObject ldObj in logicalDisks)
                        {
                            using var ld = (ManagementObject)ldObj;
                            driveLetter = ld["DeviceID"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                            if (!string.IsNullOrEmpty(driveLetter) && volumeInfoByLetter.TryGetValue(driveLetter, out var vol))
                            {
                                label = vol.Label;
                                fileSystem = vol.FileSystem;
                                capacity = vol.Capacity;
                                freeSpace = vol.FreeSpace;
                            }
                            break;
                        }
                    }
                    catch
                    {
                        // Partition may have no drive letter (e.g. recovery, EFI)
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
                        IsProtected = driveLetter.Equals(ProtectedDriveLetter, StringComparison.OrdinalIgnoreCase)
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

    /// <summary>Builds a map of drive letter to volume label, file system, capacity, and free space from Win32_Volume.</summary>
    private static Dictionary<string, (string Label, string FileSystem, ulong Capacity, ulong FreeSpace)> GetVolumeInfoByDriveLetter()
    {
        var map = new Dictionary<string, (string, string, ulong, ulong)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label, FileSystem, Capacity, FreeSpace FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var letter = obj["DriveLetter"]?.ToString()?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(letter))
                {
                    continue;
                }

                map[letter] = (
                    obj["Label"]?.ToString() ?? string.Empty,
                    obj["FileSystem"]?.ToString() ?? string.Empty,
                    obj["Capacity"] != null ? Convert.ToUInt64(obj["Capacity"]) : 0,
                    obj["FreeSpace"] != null ? Convert.ToUInt64(obj["FreeSpace"]) : 0);
            }
        }
        catch { /* ignore */ }
        return map;
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
        var letter = driveLetter.Trim().ToUpperInvariant();
        if (!letter.EndsWith(':'))
        {
            letter += ":";
        }

        if (letter.Equals(ProtectedDriveLetter, StringComparison.Ordinal))
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
                                double pct = totalBytesToWipe > 0 ? (double)totalWritten / totalBytesToWipe * 100.0 : 0;
                                progress?.Report((pass + 1, passes, Math.Min(100, pct)));
                            }
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        } catch { /* ignore */ }
                    }
                    fileIndex++;
                    if (GetFreeSpaceBytes(rootPath) <= 0)
                    {
                        break; // drive full, delete freed space and continue next pass
                    }
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
        string? volumeLabel,
        bool enableCompression,
        CancellationToken cancellationToken = default)
    {
        var letter = driveLetter.Trim().ToUpperInvariant();
        if (!letter.EndsWith(':'))
        {
            letter += ":";
        }

        if (letter.Equals(ProtectedDriveLetter, StringComparison.Ordinal))
        {
            return 3; // Access denied
        }

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

    /// <summary>Runs a diskpart script (one command per line). Returns true if exit code 0.</summary>
    public static bool RunDiskPartScript(IEnumerable<string> commands, out string output)
    {
        output = string.Empty;
        try
        {
            string diskpartPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");
            if (!File.Exists(diskpartPath))
            {
                return false;
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
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                return p.ExitCode == 0;
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { /* ignore */ }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Elevation required (e.g. "The requested operation requires elevation")
            output = "Administrator rights required.";
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Creates a primary partition on the given disk. Size in MB; 0 = use all unallocated. May require administrator.</summary>
    public static bool CreatePartition(int diskIndex, uint sizeMb)
    {
        var commands = new List<string> { $"select disk {diskIndex}" };
        commands.Add(sizeMb > 0 ? $"create partition primary size={sizeMb}" : "create partition primary");

        return RunDiskPartScript(commands, out _);
    }

    /// <summary>Deletes the given partition on the disk. May require administrator. Cannot delete system/boot partitions from user mode.</summary>
    public static bool DeletePartition(int diskIndex, int partitionIndex)
    {
        int? partNum = GetPartitionNumberFromDiskPart(diskIndex, partitionIndex);
        return partNum != null && RunDiskPartScript([$"select disk {diskIndex}", $"select partition {partNum}", "delete partition"], out _);
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
    {
        var commands = new List<string> { $"select volume {volumeNumber}" };
        if (sizeMb is > 0)
        {
            commands.Add($"extend size={sizeMb}");
        }
        else
        {
            commands.Add("extend");
        }

        return RunDiskPartScript(commands, out _);
    }

    /// <summary>Shrinks the volume by the desired amount in MB. May require administrator.</summary>
    public static bool ShrinkVolume(int volumeNumber, uint desiredMb)
    {
        return desiredMb != 0 && RunDiskPartScript([$"select volume {volumeNumber}", $"shrink desired={desiredMb}"], out _);
    }

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
