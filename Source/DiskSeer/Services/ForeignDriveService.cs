using DiskSeer.Services.Apfs;

namespace DiskSeer.Services;

/// <summary>
/// Service for detecting and accessing Apple (HFS+) and Linux (ext2/3/4, Btrfs, XFS) file systems.
/// Uses DiscUtils library for file system parsing and raw disk access via Win32 API.
/// Requires administrator privileges for raw disk access.
/// Uses multicore parallelism for drive enumeration and supports concurrent file operations.
/// </summary>
public sealed class ForeignDriveService : IDisposable
{
    /// <summary>Default degree of parallelism for drive detection (one physical disk per core, capped).</summary>
    private static int DriveDetectionParallelism => Math.Min(Environment.ProcessorCount, 8);

    /// <summary>Default degree of parallelism for batch file copy (avoid overwhelming a single disk).</summary>
    public const int DefaultCopyParallelism = 2;
    private Disk? _currentDisk;
    private IForeignFileSystemAdapter? _currentFileSystem;
    private Stream? _currentPartitionStream;

    static ForeignDriveService()
    {
        // Register all DiscUtils file systems
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(HfsPlusFileSystem).Assembly);
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(BtrfsFileSystem).Assembly);
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(XfsFileSystem).Assembly);
    }

    /// <summary>
    /// Enumerates all physical drives and detects partitions with foreign file systems.
    /// </summary>
    /// <returns>List of drives with their partitions.</returns>
    public static IReadOnlyList<ForeignDriveInfo> GetDrivesWithForeignFileSystems()
    {
        var drives = new List<ForeignDriveInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Size, Model FROM Win32_DiskDrive");

            foreach (ManagementBaseObject diskObj in searcher.Get())
            {
                using var disk = (ManagementObject)diskObj;
                int diskIndex = Convert.ToInt32(disk["Index"]);
                ulong diskSize = disk["Size"] != null ? Convert.ToUInt64(disk["Size"]) : 0;
                string model = disk["Model"]?.ToString()?.Trim() ?? $"Disk {diskIndex}";

                var driveInfo = new ForeignDriveInfo
                {
                    DriveIndex = diskIndex,
                    Model = model,
                    Size = diskSize
                };

                // Try to detect partitions with foreign file systems
                try
                {
                    DetectForeignPartitions(driveInfo);
                }
                catch
                {
                    // Continue with empty partition list if detection fails
                }

                // Only add drives that have at least one foreign file system partition
                if (driveInfo.Partitions.Any(p => p.FileSystemType != ForeignFileSystemType.Unknown))
                {
                    drives.Add(driveInfo);
                }
            }
        }
        catch
        {
            // Return empty list on WMI or permission errors
        }

        return drives;
    }

    /// <summary>
    /// Gets all physical drives (including those without foreign file systems).
    /// </summary>
    public static IReadOnlyList<ForeignDriveInfo> GetAllPhysicalDrives()
    {
        return GetAllPhysicalDrivesInternal(parallel: false);
    }

    /// <summary>
    /// Gets all physical drives using multiple cores to detect partitions in parallel.
    /// Each physical disk is scanned on a thread-pool thread for faster enumeration.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>List of drives with partitions, ordered by drive index.</returns>
    public static async Task<IReadOnlyList<ForeignDriveInfo>> GetAllPhysicalDrivesAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GetAllPhysicalDrivesInternal(parallel: true, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Internal implementation: optionally runs partition detection in parallel across drives.
    /// </summary>
    private static IReadOnlyList<ForeignDriveInfo> GetAllPhysicalDrivesInternal(bool parallel, CancellationToken cancellationToken = default)
    {
        List<(int Index, ulong Size, string Model)>? driveMeta;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Size, Model FROM Win32_DiskDrive");

            driveMeta = [];
            foreach (ManagementBaseObject diskObj in searcher.Get())
            {
                using var disk = (ManagementObject)diskObj;
                int diskIndex = Convert.ToInt32(disk["Index"]);
                ulong diskSize = disk["Size"] != null ? Convert.ToUInt64(disk["Size"]) : 0;
                string model = disk["Model"]?.ToString()?.Trim() ?? $"Disk {diskIndex}";
                driveMeta.Add((diskIndex, diskSize, model));
            }
        }
        catch
        {
            return new List<ForeignDriveInfo>();
        }

        if (driveMeta == null || driveMeta.Count == 0)
        {
            return new List<ForeignDriveInfo>();
        }

        if (!parallel)
        {
            var drives = new List<ForeignDriveInfo>();
            foreach (var (diskIndex, diskSize, model) in driveMeta)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var driveInfo = new ForeignDriveInfo
                {
                    DriveIndex = diskIndex,
                    Model = model,
                    Size = diskSize
                };
                try { DetectForeignPartitions(driveInfo); } catch { /* continue */ }
                drives.Add(driveInfo);
            }
            return drives;
        }

        // Parallel: one drive per slot, preserve order by index
        var results = new ForeignDriveInfo?[driveMeta.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = DriveDetectionParallelism,
            CancellationToken = cancellationToken
        };

        try
        {
            Parallel.For(0, driveMeta.Count, options, i =>
            {
                var (diskIndex, diskSize, model) = driveMeta[i];
                var driveInfo = new ForeignDriveInfo
                {
                    DriveIndex = diskIndex,
                    Model = model,
                    Size = diskSize
                };
                try
                {
                    DetectForeignPartitions(driveInfo);
                }
                catch
                {
                    // Leave partition list empty
                }
                results[i] = driveInfo;
            });
        }
        catch (OperationCanceledException)
        {
            // Return partial results
        }
        catch
        {
            // Return whatever we have
        }

        return results.Where(d => d != null).Cast<ForeignDriveInfo>().OrderBy(d => d.DriveIndex).ToList();
    }

    /// <summary>
    /// Detects foreign file system partitions on a drive.
    /// </summary>
    private static void DetectForeignPartitions(ForeignDriveInfo driveInfo)
    {
        string diskPath = $@"\\.\PhysicalDrive{driveInfo.DriveIndex}";

        try
        {
            using var disk = new Disk(diskPath, FileAccess.Read);

            if (disk.Partitions == null || disk.Partitions.Count == 0)
            {
                // Try to detect a single partition spanning the entire disk
                var fsType = DetectFileSystemType(disk.Content);
                if (fsType != ForeignFileSystemType.Unknown)
                {
                    driveInfo.Partitions.Add(new ForeignPartitionInfo
                    {
                        PartitionIndex = 0,
                        Offset = 0,
                        Size = (ulong)disk.Capacity,
                        FileSystemType = fsType,
                        Label = TryGetVolumeLabel(disk.Content, fsType)
                    });
                }
                return;
            }

            int index = 0;
            foreach (var partition in disk.Partitions.Partitions)
            {
                var partInfo = new ForeignPartitionInfo
                {
                    PartitionIndex = index,
                    Offset = (ulong)(partition.FirstSector * disk.SectorSize),
                    Size = (ulong)(partition.SectorCount * disk.SectorSize)
                };

                try
                {
                    using var partStream = partition.Open();
                    partInfo.FileSystemType = DetectFileSystemType(partStream);
                    if (partInfo.FileSystemType != ForeignFileSystemType.Unknown)
                    {
                        partInfo.Label = TryGetVolumeLabel(partStream, partInfo.FileSystemType);
                    }
                }
                catch
                {
                    // Keep Unknown type
                }

                driveInfo.Partitions.Add(partInfo);
                index++;
            }
        }
        catch
        {
            // Disk access failed - likely needs admin rights
        }
    }

    /// <summary>
    /// Detects the file system type by reading the superblock/header.
    /// </summary>
    private static ForeignFileSystemType DetectFileSystemType(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            return ForeignFileSystemType.Unknown;
        }

        try
        {
            // Check for ext2/3/4 (superblock at offset 1024, magic 0xEF53)
            if (stream.Length >= 2048)
            {
                stream.Position = 1024 + 56; // Offset to s_magic
                byte[] magic = new byte[2];
                if (stream.Read(magic, 0, 2) == 2 && magic[0] == 0x53 && magic[1] == 0xEF)
                {
                    // Check for ext4 features
                    stream.Position = 1024 + 96; // s_feature_incompat
                    byte[] features = new byte[4];
                    stream.ReadExactly(features, 0, 4);
                    uint incompat = BitConverter.ToUInt32(features, 0);
                    
                    if ((incompat & 0x40) != 0) // INCOMPAT_EXTENTS
                    {
                        return ForeignFileSystemType.Ext4;
                    }

                    if ((incompat & 0x04) != 0) // INCOMPAT_JOURNAL_DEV or has journal
                    {
                        return ForeignFileSystemType.Ext3;
                    }

                    return ForeignFileSystemType.Ext2;
                }
            }

            // Check for HFS+ (signature "H+" at offset 1024)
            if (stream.Length >= 1026)
            {
                stream.Position = 1024;
                byte[] sig = new byte[2];
                if (stream.Read(sig, 0, 2) == 2)
                {
                    if (sig[0] == 'H' && sig[1] == '+')
                    {
                        return ForeignFileSystemType.HfsPlus;
                    }

                    if (sig[0] == 'H' && sig[1] == 'X')
                    {
                        return ForeignFileSystemType.HfsPlus; // HFSX
                    }
                }
            }

            // Check for APFS (magic "NXSB" at offset 32 of container superblock)
            if (stream.Length >= 36)
            {
                stream.Position = 32;
                byte[] apfsMagic = new byte[4];
                if (stream.Read(apfsMagic, 0, 4) == 4)
                {
                    if (apfsMagic[0] == 'N' && apfsMagic[1] == 'X' && 
                        apfsMagic[2] == 'S' && apfsMagic[3] == 'B')
                    {
                        return ForeignFileSystemType.Apfs;
                    }
                }
            }

            // Check for Btrfs (magic "_BHRfS_M" at offset 64 of superblock at 65536)
            if (stream.Length >= 65536 + 72)
            {
                stream.Position = 65536 + 64;
                byte[] btrfsMagic = new byte[8];
                if (stream.Read(btrfsMagic, 0, 8) == 8)
                {
                    if (btrfsMagic[0] == '_' && btrfsMagic[1] == 'B' &&
                        btrfsMagic[2] == 'H' && btrfsMagic[3] == 'R' &&
                        btrfsMagic[4] == 'f' && btrfsMagic[5] == 'S' &&
                        btrfsMagic[6] == '_' && btrfsMagic[7] == 'M')
                    {
                        return ForeignFileSystemType.Btrfs;
                    }
                }
            }

            // Check for XFS (magic "XFSB" at offset 0)
            if (stream.Length >= 4)
            {
                stream.Position = 0;
                byte[] xfsMagic = new byte[4];
                if (stream.Read(xfsMagic, 0, 4) == 4)
                {
                    if (xfsMagic[0] == 'X' && xfsMagic[1] == 'F' &&
                        xfsMagic[2] == 'S' && xfsMagic[3] == 'B')
                    {
                        return ForeignFileSystemType.Xfs;
                    }
                }
            }
        }
        catch
        {
            // Detection failed
        }

        stream.Position = 0;
        return ForeignFileSystemType.Unknown;
    }

    /// <summary>
    /// Tries to get the volume label for a detected file system.
    /// </summary>
    private static string TryGetVolumeLabel(Stream stream, ForeignFileSystemType fsType)
    {
        try
        {
            stream.Position = 0;
            IFileSystem? fs = fsType switch
            {
                ForeignFileSystemType.Ext2 or 
                ForeignFileSystemType.Ext3 or 
                ForeignFileSystemType.Ext4 => new ExtFileSystem(stream),
                ForeignFileSystemType.HfsPlus => new HfsPlusFileSystem(stream),
                ForeignFileSystemType.Btrfs => new BtrfsFileSystem(stream),
                ForeignFileSystemType.Xfs => new XfsFileSystem(stream),
                _ => null
            };

            if (fs != null)
            {
                // Get label based on file system type
                var label = fs switch
                {
                    ExtFileSystem => "Ext Volume",
                    HfsPlusFileSystem => "HFS+ Volume",
                    BtrfsFileSystem => "Btrfs Volume",
                    XfsFileSystem => "XFS Volume",
                    _ => string.Empty
                };
                (fs as IDisposable)?.Dispose();
                return label;
            }
        }
        catch
        {
            // Label extraction failed
        }

        return string.Empty;
    }

    /// <summary>
    /// Opens a partition for browsing. Must be called before GetDirectoryContents or file operations.
    /// </summary>
    /// <param name="driveIndex">Physical drive index.</param>
    /// <param name="partitionIndex">Partition index within the drive.</param>
    /// <returns>True if opened successfully.</returns>
    public bool OpenPartition(int driveIndex, int partitionIndex)
    {
        CloseCurrentPartition();

        try
        {
            string diskPath = $@"\\.\PhysicalDrive{driveIndex}";
            _currentDisk = new Disk(diskPath, FileAccess.ReadWrite);

            if (_currentDisk.Partitions == null || _currentDisk.Partitions.Count == 0)
            {
                // Whole disk is the file system
                _currentPartitionStream = _currentDisk.Content;
            }
            else if (partitionIndex < _currentDisk.Partitions.Count)
            {
                _currentPartitionStream = _currentDisk.Partitions[partitionIndex].Open();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(partitionIndex));
            }

            var fsType = DetectFileSystemType(_currentPartitionStream);
            _currentPartitionStream.Position = 0;

            _currentFileSystem = fsType switch
            {
                ForeignFileSystemType.Ext2 or
                ForeignFileSystemType.Ext3 or
                ForeignFileSystemType.Ext4 => new DiscUtilsAdapter(new ExtFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.HfsPlus => new DiscUtilsAdapter(new HfsPlusFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Btrfs => new DiscUtilsAdapter(new BtrfsFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Xfs => new DiscUtilsAdapter(new XfsFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Apfs => OpenApfsOrThrow(_currentPartitionStream!),
                _ => throw new NotSupportedException($"File system type {fsType} is not supported for browsing.")
            };

            return true;
        }
        catch
        {
            CloseCurrentPartition();
            return false;
        }
    }

    private static IForeignFileSystemAdapter OpenApfsOrThrow(Stream partitionStream)
    {
        if (!ApfsFileSystem.IsAvailable)
            throw new NotSupportedException("APFS requires libfsapfs. See Docs/LIBFSAPFS_BUILD.md to build libfsapfs.dll.");
        var apfs = ApfsFileSystem.Open(partitionStream);
        if (apfs == null)
            throw new IOException("Failed to open APFS container.");
        return apfs;
    }

    /// <summary>
    /// Opens a partition in read-only mode.
    /// </summary>
    public bool OpenPartitionReadOnly(int driveIndex, int partitionIndex)
    {
        CloseCurrentPartition();

        try
        {
            string diskPath = $@"\\.\PhysicalDrive{driveIndex}";
            _currentDisk = new Disk(diskPath, FileAccess.Read);

            if (_currentDisk.Partitions == null || _currentDisk.Partitions.Count == 0)
            {
                _currentPartitionStream = _currentDisk.Content;
            }
            else if (partitionIndex < _currentDisk.Partitions.Count)
            {
                _currentPartitionStream = _currentDisk.Partitions[partitionIndex].Open();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(partitionIndex));
            }

            var fsType = DetectFileSystemType(_currentPartitionStream);
            _currentPartitionStream.Position = 0;

            _currentFileSystem = fsType switch
            {
                ForeignFileSystemType.Ext2 or
                ForeignFileSystemType.Ext3 or
                ForeignFileSystemType.Ext4 => new DiscUtilsAdapter(new ExtFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.HfsPlus => new DiscUtilsAdapter(new HfsPlusFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Btrfs => new DiscUtilsAdapter(new BtrfsFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Xfs => new DiscUtilsAdapter(new XfsFileSystem(_currentPartitionStream)),
                ForeignFileSystemType.Apfs => OpenApfsOrThrow(_currentPartitionStream),
                _ => throw new NotSupportedException($"File system type {fsType} is not supported.")
            };

            return true;
        }
        catch
        {
            CloseCurrentPartition();
            return false;
        }
    }

    /// <summary>
    /// Gets the contents of a directory.
    /// </summary>
    /// <param name="path">Path within the file system (e.g., "/" or "/home/user").</param>
    /// <returns>List of file system entries.</returns>
    public IReadOnlyList<FileSystemEntry> GetDirectoryContents(string path)
    {
        var entries = new List<FileSystemEntry>();

        if (_currentFileSystem == null)
        {
            return entries;
        }

        try
        {
            // Normalize path
            path = path.Replace('\\', '/');
            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }

            // Get directories
            foreach (var dir in _currentFileSystem.GetDirectories(path))
            {
                try
                {
                    var (lastWrite, creation, lastAccess, attrs) = _currentFileSystem.GetDirectoryInfo(dir);
                    entries.Add(new FileSystemEntry
                    {
                        Name = Path.GetFileName(dir.TrimEnd('/')),
                        FullPath = dir,
                        IsDirectory = true,
                        Size = 0,
                        LastModified = lastWrite,
                        CreationTime = creation,
                        LastAccessTime = lastAccess,
                        Permissions = GetPermissionsString(attrs)
                    });
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }

            // Get files
            foreach (var file in _currentFileSystem.GetFiles(path))
            {
                try
                {
                    var (length, lastWrite, creation, lastAccess, attrs) = _currentFileSystem.GetFileInfo(file);
                    entries.Add(new FileSystemEntry
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false,
                        Size = length,
                        LastModified = lastWrite,
                        CreationTime = creation,
                        LastAccessTime = lastAccess,
                        Permissions = GetPermissionsString(attrs)
                    });
                }
                catch
                {
                    // Skip inaccessible files
                }
            }

            // Sort: directories first, then alphabetically
            entries.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                {
                    return b.IsDirectory.CompareTo(a.IsDirectory);
                }

                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch
        {
            // Return empty list on errors
        }

        return entries;
    }

    /// <summary>
    /// Gets the volume label of the current partition.
    /// </summary>
    public string GetVolumeLabel()
    {
        if (_currentFileSystem == null)
        {
            return string.Empty;
        }

        try
        {
            return _currentFileSystem.GetVolumeLabel();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetExtVolumeLabel(ExtFileSystem ext)
    {
        // ExtFileSystem doesn't expose volume label easily, return generic name
        return "Ext Volume";
    }

    /// <summary>
    /// Checks if a path is a directory.
    /// </summary>
    public bool IsDirectory(string path)
    {
        if (_currentFileSystem == null)
        {
            return false;
        }

        try
        {
            path = NormalizePath(path);
            return _currentFileSystem.DirectoryExists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a path exists (file or directory).
    /// </summary>
    public bool Exists(string path)
    {
        if (_currentFileSystem == null)
        {
            return false;
        }

        try
        {
            path = NormalizePath(path);
            return _currentFileSystem.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a file and returns its contents as a byte array.
    /// </summary>
    public byte[] ReadFile(string path)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        path = NormalizePath(path);
        using var stream = _currentFileSystem.OpenFileRead(path);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Opens a file for reading and returns the stream.
    /// </summary>
    public Stream OpenFileForRead(string path)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        path = NormalizePath(path);
        return _currentFileSystem.OpenFileRead(path);
    }

    /// <summary>
    /// Copies a file from the foreign file system to a local Windows path.
    /// </summary>
    /// <param name="sourcePath">Path in the foreign file system.</param>
    /// <param name="destinationPath">Local Windows path to copy to.</param>
    /// <param name="progress">Optional progress callback (bytes copied, total bytes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyFileToLocalAsync(
        string sourcePath,
        string destinationPath,
        IProgress<(long copied, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        sourcePath = NormalizePath(sourcePath);
        var (totalBytes, _, _, _, _) = _currentFileSystem.GetFileInfo(sourcePath);

        await Task.Run(() =>
        {
            using var sourceStream = _currentFileSystem.OpenFileRead(sourcePath);
            using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);

            byte[] buffer = new byte[81920];
            long totalCopied = 0;
            int bytesRead;

            while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destStream.Write(buffer, 0, bytesRead);
                totalCopied += bytesRead;
                progress?.Report((totalCopied, totalBytes));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Copies multiple files from the foreign file system to local paths with limited parallelism.
    /// Uses multiple threads (up to <paramref name="maxConcurrency"/>) to copy files concurrently.
    /// </summary>
    /// <param name="sourceDestPairs">Pairs of (source path on foreign FS, local destination path).</param>
    /// <param name="maxConcurrency">Max concurrent copy operations (default 2 to avoid overwhelming a single disk).</param>
    /// <param name="progress">Optional progress (completed count, total count).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyFilesToLocalAsync(
        IEnumerable<(string SourcePath, string DestPath)> sourceDestPairs,
        int maxConcurrency = DefaultCopyParallelism,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pairs = sourceDestPairs.ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        int total = pairs.Count;
        int completed = 0;
        var completedLock = new object();
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = pairs.Select(async pair =>
        {
            if (semaphore != null)
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await CopyFileToLocalAsync(pair.SourcePath, pair.DestPath, null, cancellationToken)
                        .ConfigureAwait(false);
                    int current;
                    lock (completedLock)
                    {
                        completed++;
                        current = completed;
                    }

                    progress?.Report((current, total));
                }
                finally
                {
                    semaphore.Release();
                }
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies a local file to the foreign file system.
    /// Note: Write support depends on the file system and DiscUtils implementation.
    /// </summary>
    public async Task CopyFileFromLocalAsync(
        string sourcePath,
        string destinationPath,
        IProgress<(long copied, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        destinationPath = NormalizePath(destinationPath);
        var localInfo = new FileInfo(sourcePath);
        long totalBytes = localInfo.Length;

        await Task.Run(() =>
        {
            using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            using var destStream = _currentFileSystem.OpenFileWrite(destinationPath);

            byte[] buffer = new byte[81920];
            long totalCopied = 0;
            int bytesRead;

            while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destStream.Write(buffer, 0, bytesRead);
                totalCopied += bytesRead;
                progress?.Report((totalCopied, totalBytes));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Copies multiple local files to the foreign file system with limited parallelism.
    /// </summary>
    /// <param name="sourceDestPairs">Pairs of (local source path, destination path on foreign FS).</param>
    /// <param name="maxConcurrency">Max concurrent copy operations (default 2).</param>
    /// <param name="progress">Optional progress (completed count, total count).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyFilesFromLocalAsync(
        IEnumerable<(string SourcePath, string DestPath)> sourceDestPairs,
        int maxConcurrency = DefaultCopyParallelism,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pairs = sourceDestPairs.ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        int total = pairs.Count;
        int completed = 0;
        var completedLock = new object();
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = pairs.Select(async pair =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CopyFileFromLocalAsync(pair.SourcePath, pair.DestPath, null, cancellationToken)
                    .ConfigureAwait(false);
                int current;
                lock (completedLock)
                {
                    completed++;
                    current = completed;
                }
                progress?.Report((current, total));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a directory in the foreign file system.
    /// </summary>
    public void CreateDirectory(string path)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        path = NormalizePath(path);
        _currentFileSystem.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes a file from the foreign file system.
    /// </summary>
    public void DeleteFile(string path)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        path = NormalizePath(path);
        _currentFileSystem.DeleteFile(path);
    }

    /// <summary>
    /// Deletes a directory and its contents from the foreign file system.
    /// </summary>
    public void DeleteDirectory(string path, bool recursive = true)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        path = NormalizePath(path);
        
        if (recursive)
        {
            // Delete contents first
            foreach (var file in _currentFileSystem.GetFiles(path))
            {
                _currentFileSystem.DeleteFile(file);
            }
            foreach (var dir in _currentFileSystem.GetDirectories(path))
            {
                DeleteDirectory(dir, true);
            }
        }

        _currentFileSystem.DeleteDirectory(path, recursive);
    }

    /// <summary>
    /// Renames/moves a file or directory.
    /// </summary>
    public void Move(string sourcePath, string destinationPath)
    {
        if (_currentFileSystem == null)
        {
            throw new InvalidOperationException("No partition is open.");
        }

        sourcePath = NormalizePath(sourcePath);
        destinationPath = NormalizePath(destinationPath);
        _currentFileSystem.MoveFile(sourcePath, destinationPath);
    }

    /// <summary>
    /// Closes the current partition.
    /// </summary>
    public void CloseCurrentPartition()
    {
        if (_currentFileSystem is IDisposable disposableFs)
        {
            try { disposableFs.Dispose(); }
            catch
            {
                // ignored
            }
        }
        _currentFileSystem = null;

        if (_currentPartitionStream != null && _currentPartitionStream != _currentDisk?.Content)
        {
            try { _currentPartitionStream.Dispose(); } catch { }
        }
        _currentPartitionStream = null;

        try { _currentDisk?.Dispose(); } catch { }
        _currentDisk = null;
    }

    /// <summary>
    /// Normalizes a path for the file system.
    /// </summary>
    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }

    /// <summary>
    /// Converts file attributes to a permission string.
    /// </summary>
    private static string GetPermissionsString(FileAttributes attributes)
    {
        var perms = new char[10];
        perms[0] = (attributes & FileAttributes.Directory) != 0 ? 'd' : '-';
        perms[1] = 'r';
        perms[2] = (attributes & FileAttributes.ReadOnly) != 0 ? '-' : 'w';
        perms[3] = 'x';
        perms[4] = 'r';
        perms[5] = '-';
        perms[6] = 'x';
        perms[7] = 'r';
        perms[8] = '-';
        perms[9] = 'x';
        return new string(perms);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        CloseCurrentPartition();
    }
}
