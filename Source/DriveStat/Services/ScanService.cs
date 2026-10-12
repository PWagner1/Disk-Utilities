namespace DriveStat.Services;

/// <summary>
/// Scans directories and builds the disk usage tree.
/// Skips reparse-point directories (junctions/symlinks) and counts hard-linked file data once.
/// </summary>
public static class ScanService
{
    private static readonly Windows.UI.Color[] ExtensionColors =
    [
        Windows.UI.Color.FromArgb(255, 66, 133, 244),   // Blue
        Windows.UI.Color.FromArgb(255, 52, 168, 83),   // Green
        Windows.UI.Color.FromArgb(255, 251, 188, 4),  // Yellow
        Windows.UI.Color.FromArgb(255, 234, 67, 53),  // Red
        Windows.UI.Color.FromArgb(255, 156, 39, 176),  // Purple
        Windows.UI.Color.FromArgb(255, 0, 172, 193),  // Cyan
        Windows.UI.Color.FromArgb(255, 255, 152, 0),  // Orange
        Windows.UI.Color.FromArgb(255, 121, 85, 72),  // Brown
        Windows.UI.Color.FromArgb(255, 96, 125, 139),  // Blue Grey
        Windows.UI.Color.FromArgb(255, 233, 30, 99),  // Pink
    ];

    /// <summary>
    /// Scans a directory and builds the tree. Reports progress and supports cancellation.
    /// </summary>
    public static async Task<DirectoryNode> ScanAsync(
        string path,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = new DirectoryNode(Path.GetFileName(path) ?? path, path, 0, true, null);
        long totalSize = 0;
        int fileCount = 0;
        var seenFileKeys = new HashSet<ulong>();

        await Task.Run(() =>
        {
            ScanDirectory(path, root, ref totalSize, ref fileCount, seenFileKeys, progress, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        root.Size = totalSize;
        SortChildrenRecursive(root);
        return root;
    }

    private static void ScanDirectory(
        string path,
        DirectoryNode parent,
        ref long totalSize,
        ref int fileCount,
        HashSet<ulong> seenFileKeys,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        DirectoryInfo? dirInfo;
        try
        {
            dirInfo = new DirectoryInfo(path);
        }
        catch
        {
            return;
        }

        IEnumerable<FileSystemInfo>? entries;
        try
        {
            entries = dirInfo.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Warning($"Skipping inaccessible directory: {path}", ex, nameof(ScanService));
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry is FileInfo fileInfo)
            {
                long size = 0;
                try
                {
                    size = fileInfo.Length;
                }
                catch
                {
                    // Ignore inaccessible files
                }

                bool hardLinkDuplicate = false;
                if (FileIdentity.TryGetFileKey(fileInfo.FullName, out ulong key))
                {
                    if (!seenFileKeys.Add(key))
                    {
                        hardLinkDuplicate = true;
                    }
                }

                long countedSize = hardLinkDuplicate ? 0 : size;
                var node = new DirectoryNode(fileInfo.Name, fileInfo.FullName, size, false, parent)
                {
                    IsHardLinkDuplicate = hardLinkDuplicate
                };
                parent.Children.Add(node);
                totalSize += countedSize;
                if (!hardLinkDuplicate)
                {
                    fileCount++;
                }

                if (fileCount % 500 == 0)
                {
                    progress?.Report(new ScanProgress(totalSize, fileCount, path));
                }
            }
            else if (entry is DirectoryInfo subDir)
            {
                bool reparse = FileIdentity.IsReparsePoint(subDir);
                var child = new DirectoryNode(subDir.Name, subDir.FullName, 0, true, parent)
                {
                    IsReparsePoint = reparse
                };
                parent.Children.Add(child);

                if (reparse)
                {
                    // Do not descend into junctions/symlinks (avoids cycles and double-counting).
                    continue;
                }

                long subSize = 0;
                int subCount = 0;
                ScanDirectory(subDir.FullName, child, ref subSize, ref subCount, seenFileKeys, progress, cancellationToken);
                child.Size = subSize;
                totalSize += subSize;
                fileCount += subCount;
            }
        }
    }

    private static void SortChildrenRecursive(DirectoryNode node)
    {
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var child in node.Children)
        {
            SortChildrenRecursive(child);
        }
    }

    /// <summary>
    /// Builds extension statistics from the tree.
    /// Hard-link duplicates do not contribute size or file count.
    /// </summary>
    public static List<ExtensionInfo> BuildExtensionList(
        DirectoryNode root,
        IReadOnlyDictionary<string, Windows.UI.Color>? preferredColors = null)
    {
        var extMap = new Dictionary<string, (long Size, int Count)>(StringComparer.OrdinalIgnoreCase);

        void Collect(DirectoryNode n)
        {
            if (!n.IsDirectory && !n.IsHardLinkDuplicate)
            {
                var ext = string.IsNullOrEmpty(n.Extension) ? "(no extension)" : n.Extension.ToLowerInvariant();
                if (extMap.TryGetValue(ext, out var existing))
                {
                    extMap[ext] = (existing.Size + n.Size, existing.Count + 1);
                }
                else
                {
                    extMap[ext] = (n.Size, 1);
                }
            }
            foreach (var c in n.Children)
            {
                Collect(c);
            }
        }

        Collect(root);
        long total = root.Size;
        if (total == 0) total = 1;

        var list = extMap
            .Select(kv => new
            {
                kv.Key,
                kv.Value.Size,
                kv.Value.Count,
                Percentage = 100.0 * kv.Value.Size / total
            })
            .OrderByDescending(x => x.Size)
            .ToList();

        var result = new List<ExtensionInfo>();
        int paletteIndex = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Windows.UI.Color color;
            if (preferredColors != null && preferredColors.TryGetValue(list[i].Key, out var preferred))
            {
                color = preferred;
            }
            else
            {
                color = ExtensionColors[paletteIndex % ExtensionColors.Length];
                paletteIndex++;
            }

            result.Add(new ExtensionInfo(list[i].Key, list[i].Size, list[i].Count, list[i].Percentage, color));
        }
        return result;
    }

    /// <summary>
    /// Gets available drive roots for the drive selector.
    /// </summary>
    public static List<DriveInfo> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .OrderBy(d => d.Name)
            .ToList();
    }
}

/// <summary>
/// Progress reported during directory scan.
/// </summary>
public record ScanProgress(long TotalBytes, int FileCount, string CurrentPath);
