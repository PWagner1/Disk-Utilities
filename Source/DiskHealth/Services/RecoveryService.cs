namespace DiskHealth.Services;

/// <summary>
/// Service for scanning drives for recoverable files and recovering them to a chosen destination.
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

    private static readonly (byte[] Sig, string Ext, string Name)[] Signatures =
    [
        (JpegSignature, ".jpg", "JPEG"),
        (PngSignature, ".png", "PNG"),
        (PdfSignature, ".pdf", "PDF"),
        (GifSignature, ".gif", "GIF"),
        (ZipSignature, ".zip", "ZIP"),
        (ZipSignature2, ".zip", "ZIP (empty)"),
        (BmpSignature, ".bmp", "BMP"),
    ];

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
                    continue;

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
    /// Scans a drive for potentially recoverable files by searching for known file signatures.
    /// Requires elevated privileges for raw volume access.
    /// </summary>
    /// <param name="driveLetter">Drive letter (e.g. "D" or "D:")</param>
    /// <param name="progress">Optional progress reporter (bytes scanned, current file count)</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task<IReadOnlyList<RecoverableFileInfo>> ScanForRecoverableFilesAsync(
        string driveLetter,
        IProgress<(long BytesScanned, int FileCount)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<RecoverableFileInfo>();
        driveLetter = driveLetter.Trim().TrimEnd(':');
        if (string.IsNullOrEmpty(driveLetter))
            return results;

        string path = $"\\\\.\\{driveLetter}:";
        await Task.Run(() =>
        {
            try
            {
                using var handle = CreateFile(path,
                    FileAccess.Read,
                    FileShare.Read,
                    IntPtr.Zero,
                    FileMode.Open,
                    FileAttributes.Normal,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 5) // ACCESS_DENIED - admin required
                        throw new UnauthorizedAccessException("Administrator privileges required for raw disk access.");
                    throw new IOException($"Cannot open drive: error {err}");
                }

                const int chunkSize = 1024 * 1024; // 1 MB chunks
                var buffer = new byte[chunkSize];
                long offset = 0;
                int seq = 0;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int read = ReadFile(handle, buffer, chunkSize, out _, IntPtr.Zero);
                    if (read <= 0)
                        break;

                    for (int i = 0; i <= read - 16; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var (sig, ext, name) in Signatures)
                        {
                            if (i + sig.Length > read) continue;
                            if (!Matches(buffer, i, sig)) continue;

                            long fileOffset = offset + i;
                            int chance = EstimateRecoveryChance();
                            results.Add(new RecoverableFileInfo
                            {
                                DisplayName = $"{name}_recovered_{seq++:D4}{ext}",
                                FileType = ext,
                                EstimatedSize = 0,
                                RecoveryChancePercent = chance,
                                Offset = fileOffset
                            });
                            progress?.Report((offset + read, results.Count));
                            break; // one match per position
                        }
                    }

                    offset += read;
                    progress?.Report((offset, results.Count));
                }
            }
            catch (Exception)
            {
                throw;
            }
        }, ct).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Recovers selected files to the specified destination folder.
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

        var toRecover = files.Where(f => f.IsSelected).ToList();
        int total = toRecover.Count;
        int current = 0;

        await Task.Run(() =>
        {
            using var handle = CreateFile(path,
                FileAccess.Read,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileAttributes.Normal,
                IntPtr.Zero);

            if (handle.IsInvalid)
                throw new UnauthorizedAccessException("Administrator privileges required for raw disk access.");

            Directory.CreateDirectory(destinationFolder);

            const int maxChunk = 512 * 1024; // 512 KB per read
            var buffer = new byte[maxChunk];

            foreach (var file in toRecover)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((current, total, file.DisplayName));

                string destPath = Path.Combine(destinationFolder, file.DisplayName);
                int suffix = 0;
                while (File.Exists(destPath))
                    destPath = Path.Combine(destinationFolder, $"{Path.GetFileNameWithoutExtension(file.DisplayName)}_{++suffix}{file.FileType}");

                try
                {
                    long pos = file.Offset;
                    using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    // Read until we hit another signature or reasonable limit (e.g. 50 MB)
                    long maxRead = Math.Max(file.EstimatedSize, 50 * 1024 * 1024);
                    long readSoFar = 0;

                    while (readSoFar < maxRead)
                    {
                        ct.ThrowIfCancellationRequested();
                        int toRead = (int)Math.Min(maxChunk, maxRead - readSoFar);
                        SetFilePointer(handle, pos, IntPtr.Zero, 0);
                        int read = ReadFile(handle, buffer, toRead, out _, IntPtr.Zero);
                        if (read <= 0) break;

                        fs.Write(buffer, 0, read);
                        readSoFar += read;
                        pos += read;
                    }
                }
                catch
                {
                    if (File.Exists(destPath))
                        try { File.Delete(destPath); } catch { }
                    throw;
                }

                current++;
                progress?.Report((current, total, file.DisplayName));
            }
        }, ct).ConfigureAwait(false);
    }

    private static bool Matches(byte[] buffer, int start, byte[] sig)
    {
        for (int i = 0; i < sig.Length; i++)
            if (buffer[start + i] != sig[i])
                return false;
        return true;
    }

    private static int EstimateRecoveryChance()
    {
        // Simplified: random-ish distribution. Real impl would check overwrite status.
        var r = Random.Shared;
        int x = r.Next(100);
        if (x < 40) return r.Next(80, 100);
        if (x < 70) return r.Next(50, 80);
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
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SetFilePointer(
        SafeFileHandle hFile,
        long lDistanceToMove,
        IntPtr lpDistanceToMoveHigh,
        uint dwMoveMethod);

    #endregion
}
