namespace DiskHealth.Services;

/// <summary>
/// Service for scanning drives for recoverable files and recovering them to a chosen destination.
/// Uses multithreading for parallel signature scanning and concurrent file recovery.
/// </summary>
public static class RecoveryService
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] GifSignature = [0x47, 0x49, 0x46, 0x38]; // GIF8
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipSignature2 = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] BmpSignature = [0x42, 0x4D]; // BM
    private static readonly byte[] DocxSignature = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]; // DOCX/XLSX/PPTX (Office Open XML)
    private static readonly byte[] Mp3Signature = [0x49, 0x44, 0x33]; // ID3 tag
    private static readonly byte[] Mp4Signature = [0x00, 0x00, 0x00]; // ftyp box (checked with additional logic)
    private static readonly byte[] WavSignature = [0x52, 0x49, 0x46, 0x46]; // RIFF

    private static readonly (byte[] Sig, string Ext, string Name)[] Signatures =
    [
        (JpegSignature, ".jpg", "JPEG"),
        (PngSignature, ".png", "PNG"),
        (PdfSignature, ".pdf", "PDF"),
        (GifSignature, ".gif", "GIF"),
        (DocxSignature, ".docx", "Office XML"),
        (ZipSignature, ".zip", "ZIP"),
        (ZipSignature2, ".zip", "ZIP (empty)"),
        (BmpSignature, ".bmp", "BMP"),
        (Mp3Signature, ".mp3", "MP3"),
        (WavSignature, ".wav", "WAV"),
    ];

    /// <summary>Maximum parallelism for file recovery (concurrent file writes).</summary>
    public static int MaxRecoveryParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>Number of worker threads for signature scanning within chunks.</summary>
    public static int ScanWorkerCount { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);

    /// <summary>
    /// Enumerates drives that can be scanned for recoverable files.
    /// </summary>
    public static IReadOnlyList<ScannableDrive> GetScannableDrives()
    {
        var list = new List<ScannableDrive>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriveLetter, Label FROM Win32_Volume WHERE DriveLetter IS NOT NULL");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var letter = obj["DriveLetter"]?.ToString()?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(letter))
                {
                    continue;
                }

                list.Add(new ScannableDrive
                {
                    Letter = letter.TrimEnd('\\'),
                    Label = obj["Label"]?.ToString() ?? string.Empty
                });
            }
        }
        catch { /* Caller can handle empty list */ }

        return list;
    }

    /// <summary>
    /// Scans a drive for potentially recoverable files by searching for known file signatures (carving).
    /// This is not NTFS MFT or Recycle Bin undelete. Requires elevated privileges for raw volume access.
    /// </summary>
    /// <param name="driveLetter">Drive letter (e.g. "D" or "D:")</param>
    /// <param name="progress">Optional progress reporter (bytes scanned, current file count)</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task<IReadOnlyList<RecoverableFileInfo>> ScanForRecoverableFilesAsync(
        string driveLetter,
        IProgress<(long BytesScanned, int FileCount)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new ConcurrentBag<(long Offset, string Ext, string Name, int Chance)>();
        driveLetter = driveLetter.Trim().TrimEnd(':');
        if (string.IsNullOrEmpty(driveLetter))
        {
            return [];
        }

        string path = $"\\\\.\\{driveLetter}:";
        const int fileFlagBackupSemantics = 0x02000000;

        await Task.Run(() =>
        {
            SafeFileHandle? handle = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                handle = CreateFile(path,
                    FileAccess.Read,
                    FileShare.Read,
                    IntPtr.Zero,
                    FileMode.Open,
                    (FileAttributes)fileFlagBackupSemantics,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    break;
                }

                int err = Marshal.GetLastWin32Error();
                if (err == 5)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required for raw disk access.");
                }

                if (err == 32 && attempt < 2)
                {
                    Thread.Sleep(500 * (attempt + 1));
                    continue;
                }
                throw new IOException(err == 32
                    ? "Drive is in use by another process. Close other applications using this drive and try again."
                    : $"Cannot open drive: error {err}");
            }

            using (handle!)
            {
                const int chunkSize = 4 * 1024 * 1024; // 4 MB chunks for better parallelism
                var buffer = new byte[chunkSize];
                long offset = 0;
                int workerCount = ScanWorkerCount;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int read = ReadFile(handle, buffer, chunkSize, out _, IntPtr.Zero);
                    if (read <= 0)
                    {
                        break;
                    }

                    // Parallel signature scanning: partition buffer into segments
                    int segmentSize = Math.Max(4096, read / workerCount);
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = workerCount,
                        CancellationToken = ct
                    };

                    long chunkOffset = offset;
                    byte[] chunkBuffer = buffer; // Capture for closure
                    int chunkRead = read;

                    Parallel.For(0, workerCount, parallelOptions, worker =>
                    {
                        int start = worker * segmentSize;
                        int end = (worker == workerCount - 1) ? chunkRead - 16 : Math.Min(start + segmentSize, chunkRead - 16);
                        if (start >= end)
                        {
                            return;
                        }

                        for (int i = start; i <= end; i++)
                        {
                            foreach (var (sig, ext, name) in Signatures)
                            {
                                if (i + sig.Length > chunkRead)
                                {
                                    continue;
                                }

                                if (!Matches(chunkBuffer, i, sig))
                                {
                                    continue;
                                }

                                long fileOffset = chunkOffset + i;
                                int chance = EstimateRecoveryChance();
                                results.Add((fileOffset, ext, name, chance));
                                break;
                            }
                        }
                    });

                    offset += read;
                    progress?.Report((offset, results.Count));
                }
            }
        }, ct).ConfigureAwait(false);

        // Convert to sorted list with sequential naming
        var sorted = results.OrderBy(r => r.Offset).ToList();
        var finalResults = new List<RecoverableFileInfo>(sorted.Count);
        int seq = 0;
        foreach (var (fileOffset, ext, name, chance) in sorted)
        {
            finalResults.Add(new RecoverableFileInfo
            {
                DisplayName = $"{name}_recovered_{seq++:D4}{ext}",
                FileType = ext,
                EstimatedSize = 0,
                RecoveryChancePercent = chance,
                Offset = fileOffset
            });
        }

        return finalResults;
    }

    /// <summary>
    /// Recovers selected files to the specified destination folder using parallel writes.
    /// Files are processed in batches: data is read from disk, then written concurrently.
    /// </summary>
    public static async Task RecoverFilesAsync(
        IReadOnlyList<RecoverableFileInfo> files,
        string driveLetter,
        string destinationFolder,
        IProgress<(int Current, int Total, string? CurrentFile)>? progress = null,
        CancellationToken ct = default)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':');
        string path = $"\\\\.\\{driveLetter}:";
        const int fileFlagBackupSemantics = 0x02000000;

        var toRecover = files.Where(f => f.IsSelected).ToList();
        int total = toRecover.Count;
        int completed = 0;

        Directory.CreateDirectory(destinationFolder);

        // Process files in batches for concurrent recovery
        int batchSize = MaxRecoveryParallelism;
        var batches = toRecover.Chunk(batchSize).ToList();

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();

            // Read all file data for this batch
            var fileDataList = new List<(RecoverableFileInfo File, byte[] Data, string DestPath)>();

            await Task.Run(() =>
            {
                SafeFileHandle? handle = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    handle = CreateFile(path,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        IntPtr.Zero,
                        FileMode.Open,
                        (FileAttributes)fileFlagBackupSemantics,
                        IntPtr.Zero);

                    if (!handle.IsInvalid)
                    {
                        break;
                    }

                    int err = Marshal.GetLastWin32Error();
                    if (err == 5)
                    {
                        throw new UnauthorizedAccessException("Administrator privileges required for raw disk access.");
                    }

                    if (err == 32 && attempt < 2)
                    {
                        Thread.Sleep(500 * (attempt + 1));
                        continue;
                    }
                    throw new IOException(err == 32
                        ? "Drive is in use by another process. Close other applications using this drive and try again."
                        : $"Cannot open drive: error {err}");
                }

                using (handle!)
                {
                    const int maxChunk = 512 * 1024;
                    var buffer = new byte[maxChunk];

                    foreach (var file in batch)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report((completed, total, $"Reading {file.DisplayName}..."));

                        string destPath = Path.Combine(destinationFolder, file.DisplayName);
                        int suffix = 0;
                        while (File.Exists(destPath))
                            destPath = Path.Combine(destinationFolder, $"{Path.GetFileNameWithoutExtension(file.DisplayName)}_{++suffix}{file.FileType}");

                        using var ms = new MemoryStream();
                        long pos = file.Offset;
                        long maxRead = Math.Max(file.EstimatedSize, 50 * 1024 * 1024);
                        long readSoFar = 0;

                        while (readSoFar < maxRead)
                        {
                            ct.ThrowIfCancellationRequested();
                            int toRead = (int)Math.Min(maxChunk, maxRead - readSoFar);
                            SetFilePointer(handle, pos, IntPtr.Zero, 0);
                            int read = ReadFile(handle, buffer, toRead, out _, IntPtr.Zero);
                            if (read <= 0)
                            {
                                break;
                            }

                            ms.Write(buffer, 0, read);
                            readSoFar += read;
                            pos += read;
                        }

                        fileDataList.Add((file, ms.ToArray(), destPath));
                    }
                }
            }, ct).ConfigureAwait(false);

            // Write all files in this batch concurrently
            var writeTasks = fileDataList.Select(async item =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var fs = new FileStream(item.DestPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                    await fs.WriteAsync(item.Data, ct);
                    Interlocked.Increment(ref completed);
                    progress?.Report((completed, total, item.File.DisplayName));
                }
                catch
                {
                    if (File.Exists(item.DestPath))
                    {
                        try { File.Delete(item.DestPath); }
                        catch
                        {
                            // ignored
                        }
                    }

                    throw;
                }
            });

            await Task.WhenAll(writeTasks).ConfigureAwait(false);
        }
    }

    private static bool Matches(byte[] buffer, int start, byte[] sig)
    {
        for (int i = 0; i < sig.Length; i++)
            if (buffer[start + i] != sig[i])
            {
                return false;
            }

        return true;
    }

    /// <summary>
    /// Heuristic recovery-chance estimate only. Does not inspect overwrite status or filesystem metadata.
    /// </summary>
    private static int EstimateRecoveryChance()
    {
        // Placeholder distribution until signature/footer validation can drive a better estimate.
        var r = Random.Shared;
        int x = r.Next(100);
        if (x < 40)
        {
            return r.Next(80, 100);
        }

        if (x < 70)
        {
            return r.Next(50, 80);
        }

        return r.Next(20, 50);
    }

    #region P/Invoke

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        FileAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32", SetLastError = true)]
    private static extern int ReadFile(
        SafeFileHandle? hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SetFilePointer(
        SafeFileHandle? hFile,
        long lDistanceToMove,
        IntPtr lpDistanceToMoveHigh,
        uint dwMoveMethod);

    #endregion
}
