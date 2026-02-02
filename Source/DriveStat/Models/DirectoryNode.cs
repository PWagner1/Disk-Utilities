namespace DriveStat.Models;

/// <summary>
/// Represents a directory or file in the disk usage tree.
/// </summary>
public sealed class DirectoryNode
{
    /// <summary>Display name (directory or file name).</summary>
    public string Name { get; }

    /// <summary>Full path.</summary>
    public string FullPath { get; }

    /// <summary>Size in bytes. For directories, includes all descendants.</summary>
    public long Size { get; set; }

    /// <summary>True if this is a directory; false if a file.</summary>
    public bool IsDirectory { get; }

    /// <summary>File extension (e.g. ".txt"). Empty for directories or files without extension.</summary>
    public string Extension { get; }

    /// <summary>Child nodes (subdirectories and files). Sorted by size descending.</summary>
    public List<DirectoryNode> Children { get; } = [];

    /// <summary>Parent node. Null for root.</summary>
    public DirectoryNode? Parent { get; }

    /// <summary>Formatted size for display.</summary>
    public string FormattedSize => FormatSize(Size);

    public DirectoryNode(string name, string fullPath, long size, bool isDirectory, DirectoryNode? parent)
    {
        Name = name;
        FullPath = fullPath;
        Size = size;
        IsDirectory = isDirectory;
        Parent = parent;
        Extension = isDirectory ? string.Empty : Path.GetExtension(fullPath);
    }

    /// <summary>Formats size for display (e.g. "1.5 GB").</summary>
    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return i == 0 ? $"{size:N0} {suffixes[i]}" : $"{size:N2} {suffixes[i]}";
    }
}
