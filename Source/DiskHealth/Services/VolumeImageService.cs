namespace DiskHealth.Services;

/// <summary>
/// Optional forensic-lite helper: copies a raw volume to an image file (read-only source open).
/// Does not mount or modify the source.
/// </summary>
public static class VolumeImageService
{
    private const int FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        FileAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    /// <summary>
    /// Creates a raw image of up to <paramref name="maxBytes"/> from the volume (0 = entire volume size if known).
    /// </summary>
    public static async Task CreateImageAsync(
        string driveLetter,
        string destinationPath,
        long maxBytes,
        IProgress<(long Written, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':').ToUpperInvariant();
        string path = $@"\\.\{driveLetter}:";

        await Task.Run(() =>
        {
            using var handle = CreateFile(
                path,
                FileAccess.Read,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                (FileAttributes)FileFlagBackupSemantics,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 5)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to image the volume.");
                }

                throw new IOException($"Cannot open volume (Win32 {err}).");
            }

            long total = maxBytes;
            if (total <= 0)
            {
                try
                {
                    var di = new DriveInfo(driveLetter + ":");
                    total = di.TotalSize;
                }
                catch
                {
                    total = 8L * 1024 * 1024 * 1024; // fallback 8 GB
                }
            }

            string? dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var buffer = new byte[1024 * 1024];
            long written = 0;
            using var outFs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan);
            while (written < total)
            {
                ct.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(buffer.Length, total - written);
                if (!SetFilePointerEx(handle, written, out _, 0))
                {
                    break;
                }

                if (!ReadFile(handle, buffer, toRead, out int read, IntPtr.Zero) || read <= 0)
                {
                    break;
                }

                outFs.Write(buffer, 0, read);
                written += read;
                if (written % (16L * 1024 * 1024) < read)
                {
                    progress?.Report((written, total));
                }
            }

            progress?.Report((written, total));
            AppLog.Info($"Volume image written: {destinationPath} ({written} bytes).", source: nameof(VolumeImageService));
        }, ct).ConfigureAwait(false);
    }
}
