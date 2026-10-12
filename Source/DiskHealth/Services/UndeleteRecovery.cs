namespace DiskHealth.Services;

/// <summary>Dispatches recovery for Recycle Bin / MFT deleted candidates (not carving).</summary>
public static class UndeleteRecovery
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

    /// <summary>Recovers selected undelete candidates; returns count written.</summary>
    public static async Task<int> RecoverAsync(
        IReadOnlyList<RecoverableFileInfo> files,
        string driveLetter,
        string destinationFolder,
        IProgress<(int Current, int Total, string? CurrentFile)>? progress = null,
        CancellationToken ct = default)
    {
        var recycle = files.Where(f => f.IsSelected && f.SourceKind == RecoverySourceKind.RecycleBin).ToList();
        var mft = files.Where(f => f.IsSelected && f.SourceKind == RecoverySourceKind.MftDeleted).ToList();
        int total = recycle.Count + mft.Count;
        int done = 0;

        if (recycle.Count > 0)
        {
            var recycleProgress = new Progress<(int Current, int Total, string? CurrentFile)>(p =>
            {
                progress?.Report((done + p.Current, total, p.CurrentFile));
            });
            done += await RecycleBinRecovery.RestoreAsync(recycle, destinationFolder, recycleProgress, ct)
                .ConfigureAwait(false);
        }

        if (mft.Count > 0)
        {
            done += await RecoverMftAsync(mft, driveLetter, destinationFolder, done, total, progress, ct)
                .ConfigureAwait(false);
        }

        return done;
    }

    private static async Task<int> RecoverMftAsync(
        List<RecoverableFileInfo> files,
        string driveLetter,
        string destinationFolder,
        int doneBase,
        int total,
        IProgress<(int Current, int Total, string? CurrentFile)>? progress,
        CancellationToken ct)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':').ToUpperInvariant();
        string path = $@"\\.\{driveLetter}:";
        Directory.CreateDirectory(destinationFolder);
        int written = 0;

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
                    throw new UnauthorizedAccessException("Administrator privileges required for MFT recovery.");
                }

                throw new IOException($"Cannot open volume (Win32 {err}).");
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((doneBase + written, total, file.DisplayName));
                byte[]? data = MftDeletedScanner.ReadFromRuns(handle, file);
                if (data == null || data.Length == 0)
                {
                    AppLog.Warning($"No readable data runs for MFT deleted file '{file.DisplayName}'.",
                        source: nameof(UndeleteRecovery));
                    written++;
                    continue;
                }

                string dest = Path.Combine(destinationFolder, Sanitize(file.DisplayName));
                int suffix = 0;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(destinationFolder,
                        $"{Path.GetFileNameWithoutExtension(file.DisplayName)}_{++suffix}{file.FileType}");
                }

                File.WriteAllBytes(dest, data);
                written++;
            }
        }, ct).ConfigureAwait(false);

        return written;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "mft_recovered.bin" : name;
    }
}
