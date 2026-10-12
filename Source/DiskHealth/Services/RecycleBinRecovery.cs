namespace DiskHealth.Services;

/// <summary>
/// Enumerates and restores files from the Windows Recycle Bin on a volume ($Recycle.Bin).
/// This is true Recycle Bin recovery — not signature carving.
/// </summary>
public static class RecycleBinRecovery
{
    /// <summary>Lists recoverable Recycle Bin items on the given drive letter.</summary>
    public static async Task<IReadOnlyList<RecoverableFileInfo>> ScanAsync(
        string driveLetter,
        CancellationToken ct = default)
    {
        driveLetter = driveLetter.Trim().TrimEnd(':').ToUpperInvariant();
        string root = driveLetter + Path.DirectorySeparatorChar;
        string recycleRoot = Path.Combine(root, "$Recycle.Bin");

        return await Task.Run(() =>
        {
            var results = new List<RecoverableFileInfo>();
            if (!Directory.Exists(recycleRoot))
            {
                return (IReadOnlyList<RecoverableFileInfo>)results;
            }

            IEnumerable<string> sidDirs;
            try
            {
                sidDirs = Directory.EnumerateDirectories(recycleRoot);
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Cannot enumerate $Recycle.Bin on {driveLetter}:.", ex, nameof(RecycleBinRecovery));
                return results;
            }

            foreach (string sidDir in sidDirs)
            {
                ct.ThrowIfCancellationRequested();
                IEnumerable<string> iFiles;
                try
                {
                    iFiles = Directory.EnumerateFiles(sidDir, "$I*");
                }
                catch
                {
                    continue;
                }

                foreach (string iPath in iFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (!TryParseInfoFile(iPath, out var meta))
                        {
                            continue;
                        }

                        string fileName = Path.GetFileName(iPath);
                        if (fileName.Length < 2 || fileName[0] != '$' || fileName[1] is not ('I' or 'i'))
                        {
                            continue;
                        }

                        string rName = "$R" + fileName[2..];
                        string rPath = Path.Combine(sidDir, rName);
                        if (!File.Exists(rPath))
                        {
                            string stem = Path.GetFileNameWithoutExtension(fileName);
                            if (stem.Length > 2)
                            {
                                string? alt = Directory.EnumerateFiles(sidDir, "$R" + stem[2..] + ".*").FirstOrDefault();
                                if (alt != null)
                                {
                                    rPath = alt;
                                }
                            }

                            if (!File.Exists(rPath))
                            {
                                continue;
                            }
                        }

                        string originalName = Path.GetFileName(meta.OriginalPath);
                        if (string.IsNullOrWhiteSpace(originalName))
                        {
                            originalName = Path.GetFileName(rPath);
                        }

                        string ext = Path.GetExtension(originalName);
                        long size = meta.FileSize > 0 ? meta.FileSize : new FileInfo(rPath).Length;
                        results.Add(new RecoverableFileInfo
                        {
                            DisplayName = originalName,
                            FileType = string.IsNullOrEmpty(ext) ? ".bin" : ext.ToLowerInvariant(),
                            EstimatedSize = size,
                            RecoveryChancePercent = 90,
                            Offset = 0,
                            PathHint = rPath,
                            SourceKind = RecoverySourceKind.RecycleBin
                        });
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning($"Skipping Recycle Bin entry {iPath}.", ex, nameof(RecycleBinRecovery));
                    }
                }
            }

            return results
                .OrderByDescending(r => r.EstimatedSize)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Copies selected Recycle Bin $R files to the destination folder.</summary>
    public static async Task<int> RestoreAsync(
        IEnumerable<RecoverableFileInfo> files,
        string destinationFolder,
        IProgress<(int Current, int Total, string? CurrentFile)>? progress = null,
        CancellationToken ct = default)
    {
        var list = files.Where(f => f.IsSelected && f.SourceKind == RecoverySourceKind.RecycleBin).ToList();
        Directory.CreateDirectory(destinationFolder);
        int done = 0;
        await Task.Run(() =>
        {
            foreach (var file in list)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((done, list.Count, file.DisplayName));
                if (string.IsNullOrEmpty(file.PathHint) || !File.Exists(file.PathHint))
                {
                    continue;
                }

                string dest = Path.Combine(destinationFolder, SanitizeFileName(file.DisplayName));
                int suffix = 0;
                while (File.Exists(dest))
                {
                    string name = Path.GetFileNameWithoutExtension(file.DisplayName);
                    dest = Path.Combine(destinationFolder, $"{name}_{++suffix}{file.FileType}");
                }

                File.Copy(file.PathHint, dest, overwrite: false);
                done++;
            }
        }, ct).ConfigureAwait(false);
        return done;
    }

    /// <summary>Parses a $I metadata file (Vista+ format). Exposed for unit tests.</summary>
    public static bool TryParseInfoFile(string path, out RecycleInfo meta)
    {
        meta = default;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            return TryParseInfoBytes(data, out meta);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Parses $I bytes (Vista+). Exposed for unit tests.</summary>
    public static bool TryParseInfoBytes(ReadOnlySpan<byte> data, out RecycleInfo meta)
    {
        meta = default;
        if (data.Length < 28)
        {
            return false;
        }

        long version = BitConverter.ToInt64(data);
        if (version is not (1 or 2))
        {
            // Some systems store version as 32-bit; try first 4 bytes == 1/2
            int v32 = BitConverter.ToInt32(data);
            if (v32 is not (1 or 2))
            {
                return false;
            }

            version = v32;
        }

        long fileSize = BitConverter.ToInt64(data[8..]);
        long ft = BitConverter.ToInt64(data[16..]);
        DateTimeOffset deleted = DateTimeOffset.FromFileTime(ft);
        string originalPath;

        if (version >= 2 && data.Length >= 28)
        {
            int charCount = BitConverter.ToInt32(data[24..]);
            if (charCount <= 0 || charCount > 0x8000 || 28 + charCount * 2 > data.Length)
            {
                return false;
            }

            originalPath = Encoding.Unicode.GetString(data.Slice(28, charCount * 2)).TrimEnd('\0');
        }
        else
        {
            originalPath = Encoding.Unicode.GetString(data[24..]).TrimEnd('\0');
        }

        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return false;
        }

        meta = new RecycleInfo(originalPath, fileSize, deleted);
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "restored.bin" : name;
    }

    /// <summary>Parsed Recycle Bin $I metadata.</summary>
    public readonly record struct RecycleInfo(string OriginalPath, long FileSize, DateTimeOffset DeletedUtc);
}
