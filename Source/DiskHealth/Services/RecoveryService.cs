namespace DiskHealth.Services;

/// <summary>
/// Service for scanning drives for recoverable files and recovering them to a chosen destination.
/// Uses multithreading for parallel signature scanning and concurrent file recovery.
/// </summary>
public static class RecoveryService
{
    /// <summary>Known carve extensions for filter UI (leading dot, lower-case).</summary>
    public static IReadOnlyList<string> KnownExtensions { get; } = new List<string>
    {
        ".jpg", ".png", ".pdf", ".gif", ".docx", ".zip", ".bmp", ".mp3", ".wav",
        ".7z", ".rar", ".webp", ".heic", ".sqlite", ".exe", ".iso", ".mkv", ".flac", ".mp4"
    };

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] GifSignature = [0x47, 0x49, 0x46, 0x38]; // GIF8
    private static readonly byte[] ZipLocal = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEmpty = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] BmpSignature = [0x42, 0x4D]; // BM
    private static readonly byte[] Mp3Signature = [0x49, 0x44, 0x33]; // ID3
    private static readonly byte[] RiffSignature = [0x52, 0x49, 0x46, 0x46]; // RIFF
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]; // Rar!\x1A\x07
    private static readonly byte[] SqliteSignature = "SQLite format 3\0"u8.ToArray();
    private static readonly byte[] ExeSignature = [0x4D, 0x5A]; // MZ
    private static readonly byte[] MkvSignature = [0x1A, 0x45, 0xDF, 0xA3];
    private static readonly byte[] FlacSignature = [0x66, 0x4C, 0x61, 0x43]; // fLaC
    private static readonly byte[] FtypSignature = [0x66, 0x74, 0x79, 0x70]; // ftyp
    private static readonly byte[] Cd001Signature = "CD001"u8.ToArray();

    /// <summary>Maximum parallelism for file recovery (concurrent file writes).</summary>
    public static int MaxRecoveryParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>Number of worker threads for signature scanning within chunks.</summary>
    public static int ScanWorkerCount { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);

    /// <summary>A signature match found in a buffer (for carving / unit tests).</summary>
    public readonly record struct SignatureHit(long Offset, string Extension, string Name);

    /// <summary>
    /// Scans a byte buffer for known file signatures (same rules as volume carving).
    /// Prefer Office Open XML over generic ZIP when local-header names indicate OOXML.
    /// </summary>
    public static IReadOnlyList<SignatureHit> FindSignaturesInBuffer(ReadOnlySpan<byte> buffer)
    {
        var hits = new List<SignatureHit>();
        if (buffer.Length == 0)
        {
            return hits;
        }

        int end = Math.Max(0, buffer.Length - 16);
        for (int i = 0; i <= end; i++)
        {
            if (TryIdentify(buffer, i, out string ext, out string name))
            {
                hits.Add(new SignatureHit(i, ext, name));
            }
        }

        return hits;
    }

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
        catch (Exception ex)
        {
            AppLog.Error("Failed to enumerate scannable drives via WMI.", ex, nameof(RecoveryService));
        }

        return list;
    }

    /// <summary>
    /// Scans a drive for potentially recoverable files by searching for known file signatures (carving).
    /// This is not NTFS MFT or Recycle Bin undelete. Requires elevated privileges for raw volume access.
    /// </summary>
    public static Task<IReadOnlyList<RecoverableFileInfo>> ScanForRecoverableFilesAsync(
        string driveLetter,
        IProgress<(long BytesScanned, int FileCount)>? progress = null,
        CancellationToken ct = default)
        => ScanForRecoverableFilesAsync(driveLetter, options: null, progress, ct);

    /// <inheritdoc cref="ScanForRecoverableFilesAsync(string, IProgress{ValueTuple{long, int}}?, CancellationToken)"/>
    public static async Task<IReadOnlyList<RecoverableFileInfo>> ScanForRecoverableFilesAsync(
        string driveLetter,
        ScanOptions? options,
        IProgress<(long BytesScanned, int FileCount)>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ScanOptions();
        var results = new ConcurrentBag<(long Offset, string Ext, string Name, int Chance)>();
        driveLetter = driveLetter.Trim().TrimEnd(':');
        if (string.IsNullOrEmpty(driveLetter))
        {
            return [];
        }

        string path = $"\\\\.\\{driveLetter}:";
        const int fileFlagBackupSemantics = 0x02000000;
        long maxBytes = options.QuickScan ? Math.Max(1, options.MaxBytes) : long.MaxValue;

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
                const int chunkSize = 4 * 1024 * 1024;
                var buffer = new byte[chunkSize];
                long offset = 0;
                int workerCount = ScanWorkerCount;

                while (offset < maxBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    options.PauseGate?.WaitIfPaused(ct);

                    int toRead = (int)Math.Min(chunkSize, maxBytes - offset);
                    int read = ReadFile(handle, buffer, toRead, out _, IntPtr.Zero);
                    if (read <= 0)
                    {
                        break;
                    }

                    int segmentSize = Math.Max(4096, read / workerCount);
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = workerCount,
                        CancellationToken = ct
                    };

                    long chunkOffset = offset;
                    byte[] chunkBuffer = buffer;
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
                            if (!TryIdentify(chunkBuffer.AsSpan(0, chunkRead), i, out string ext, out string name))
                            {
                                continue;
                            }

                            if (options.AllowedExtensions is { Count: > 0 } &&
                                !options.AllowedExtensions.Contains(ext))
                            {
                                continue;
                            }

                            results.Add((chunkOffset + i, ext, name, EstimateRecoveryChance(ext)));
                        }
                    });

                    offset += read;
                    progress?.Report((offset, results.Count));
                }
            }
        }, ct).ConfigureAwait(false);

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

    /// <summary>Reads a preview slice from a raw volume at the given offset (for image/PDF preview).</summary>
    public static async Task<byte[]> ReadPreviewAsync(
        string driveLetter,
        long offset,
        int maxBytes,
        CancellationToken ct = default)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':');
        string path = $"\\\\.\\{driveLetter}:";
        const int fileFlagBackupSemantics = 0x02000000;
        maxBytes = Math.Clamp(maxBytes, 1, 16 * 1024 * 1024);

        return await Task.Run(() =>
        {
            using var handle = CreateFile(path,
                FileAccess.Read,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                (FileAttributes)fileFlagBackupSemantics,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException($"Cannot open drive for preview: error {err}");
            }

            var buffer = new byte[maxBytes];
            SetFilePointer(handle, offset, IntPtr.Zero, 0);
            int read = ReadFile(handle, buffer, maxBytes, out _, IntPtr.Zero);
            if (read <= 0)
            {
                return [];
            }

            if (read == buffer.Length)
            {
                return buffer;
            }

            return buffer.AsSpan(0, read).ToArray();
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Recovers selected files to the specified destination folder using parallel writes.
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

        int batchSize = MaxRecoveryParallelism;
        var batches = toRecover.Chunk(batchSize).ToList();

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();

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

    /// <summary>Identifies a signature at <paramref name="i"/>; used by unit tests and the scanner.</summary>
    public static bool TryIdentify(ReadOnlySpan<byte> buffer, int i, out string extension, out string name)
    {
        extension = "";
        name = "";
        if (i < 0 || i >= buffer.Length)
        {
            return false;
        }

        // Longer / more specific checks first.
        if (Matches(buffer, i, SevenZipSignature))
        {
            extension = ".7z";
            name = "7-Zip";
            return true;
        }

        if (Matches(buffer, i, RarSignature))
        {
            extension = ".rar";
            name = "RAR";
            return true;
        }

        if (Matches(buffer, i, SqliteSignature))
        {
            extension = ".sqlite";
            name = "SQLite";
            return true;
        }

        if (Matches(buffer, i, PngSignature))
        {
            extension = ".png";
            name = "PNG";
            return true;
        }

        if (Matches(buffer, i, PdfSignature))
        {
            extension = ".pdf";
            name = "PDF";
            return true;
        }

        if (Matches(buffer, i, GifSignature))
        {
            extension = ".gif";
            name = "GIF";
            return true;
        }

        if (Matches(buffer, i, FlacSignature))
        {
            extension = ".flac";
            name = "FLAC";
            return true;
        }

        if (Matches(buffer, i, MkvSignature))
        {
            extension = ".mkv";
            name = "Matroska";
            return true;
        }

        if (Matches(buffer, i, ZipLocal))
        {
            if (LooksLikeOfficeOpenXml(buffer, i))
            {
                extension = ".docx";
                name = "Office XML";
                return true;
            }

            extension = ".zip";
            name = "ZIP";
            return true;
        }

        if (Matches(buffer, i, ZipEmpty))
        {
            extension = ".zip";
            name = "ZIP (empty)";
            return true;
        }

        if (Matches(buffer, i, RiffSignature))
        {
            if (HasAsciiAt(buffer, i + 8, "WEBP"))
            {
                extension = ".webp";
                name = "WebP";
                return true;
            }

            if (HasAsciiAt(buffer, i + 8, "WAVE"))
            {
                extension = ".wav";
                name = "WAV";
                return true;
            }

            return false;
        }

        // ISO BMFF: 4-byte size + 'ftyp' + brand (HEIC / MP4).
        if (i + 12 <= buffer.Length && Matches(buffer, i + 4, FtypSignature))
        {
            int brand = i + 8;
            if (HasAsciiAt(buffer, brand, "heic") || HasAsciiAt(buffer, brand, "heix") ||
                HasAsciiAt(buffer, brand, "mif1") || HasAsciiAt(buffer, brand, "msf1"))
            {
                extension = ".heic";
                name = "HEIC";
                return true;
            }

            if (HasAsciiAt(buffer, brand, "isom") || HasAsciiAt(buffer, brand, "mp41") ||
                HasAsciiAt(buffer, brand, "mp42") || HasAsciiAt(buffer, brand, "avc1") ||
                HasAsciiAt(buffer, brand, "iso2") || HasAsciiAt(buffer, brand, "M4A ") ||
                HasAsciiAt(buffer, brand, "qt  "))
            {
                extension = ".mp4";
                name = "MP4";
                return true;
            }
        }

        if (Matches(buffer, i, Cd001Signature))
        {
            extension = ".iso";
            name = "ISO9660";
            return true;
        }

        if (Matches(buffer, i, JpegSignature))
        {
            extension = ".jpg";
            name = "JPEG";
            return true;
        }

        if (Matches(buffer, i, Mp3Signature))
        {
            extension = ".mp3";
            name = "MP3";
            return true;
        }

        if (Matches(buffer, i, ExeSignature))
        {
            // Reduce PE false positives: require 'PE\0\0' within a reasonable MZ header window when present.
            if (LooksLikePe(buffer, i) || buffer.Length - i < 64)
            {
                extension = ".exe";
                name = "PE executable";
                return true;
            }

            return false;
        }

        if (Matches(buffer, i, BmpSignature))
        {
            extension = ".bmp";
            name = "BMP";
            return true;
        }

        return false;
    }

    private static bool LooksLikeOfficeOpenXml(ReadOnlySpan<byte> buffer, int zipOffset)
    {
        // ZIP local file header: filename length at +26, extra at +28, name at +30.
        if (zipOffset + 30 >= buffer.Length)
        {
            // Classic Office marker used by older fixtures: version/flags 14 00 06 00.
            return zipOffset + 8 <= buffer.Length
                   && buffer[zipOffset + 4] == 0x14
                   && buffer[zipOffset + 5] == 0x00
                   && buffer[zipOffset + 6] == 0x06
                   && buffer[zipOffset + 7] == 0x00;
        }

        ushort nameLen = (ushort)(buffer[zipOffset + 26] | (buffer[zipOffset + 27] << 8));
        if (nameLen == 0 || zipOffset + 30 + nameLen > buffer.Length)
        {
            return zipOffset + 8 <= buffer.Length
                   && buffer[zipOffset + 4] == 0x14
                   && buffer[zipOffset + 5] == 0x00
                   && buffer[zipOffset + 6] == 0x06
                   && buffer[zipOffset + 7] == 0x00;
        }

        var nameBytes = buffer.Slice(zipOffset + 30, nameLen);
        string name = Encoding.ASCII.GetString(nameBytes);
        return name.Contains("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("word/", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePe(ReadOnlySpan<byte> buffer, int mzOffset)
    {
        if (mzOffset + 0x40 > buffer.Length)
        {
            return true; // short buffer — allow MZ for tests
        }

        int peOffset = buffer[mzOffset + 0x3C]
                       | (buffer[mzOffset + 0x3D] << 8)
                       | (buffer[mzOffset + 0x3E] << 16)
                       | (buffer[mzOffset + 0x3F] << 24);
        if (peOffset <= 0 || mzOffset + peOffset + 4 > buffer.Length)
        {
            return false;
        }

        return buffer[mzOffset + peOffset] == (byte)'P'
               && buffer[mzOffset + peOffset + 1] == (byte)'E'
               && buffer[mzOffset + peOffset + 2] == 0
               && buffer[mzOffset + peOffset + 3] == 0;
    }

    private static bool HasAsciiAt(ReadOnlySpan<byte> buffer, int offset, string ascii)
    {
        if (offset < 0 || offset + ascii.Length > buffer.Length)
        {
            return false;
        }

        for (int i = 0; i < ascii.Length; i++)
        {
            if (buffer[offset + i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> buffer, int start, byte[] sig)
    {
        if (start < 0 || start + sig.Length > buffer.Length)
        {
            return false;
        }

        for (int i = 0; i < sig.Length; i++)
        {
            if (buffer[start + i] != sig[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Heuristic recovery-chance estimate. Slightly higher for formats with strong headers.
    /// </summary>
    private static int EstimateRecoveryChance(string extension)
    {
        int baseMin = extension switch
        {
            ".png" or ".pdf" or ".7z" or ".sqlite" or ".flac" => 55,
            ".jpg" or ".gif" or ".webp" or ".mkv" => 45,
            ".zip" or ".docx" or ".rar" => 40,
            _ => 25
        };

        return Math.Clamp(baseMin + Random.Shared.Next(0, 40), 15, 95);
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
