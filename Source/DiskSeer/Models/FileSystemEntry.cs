namespace DiskSeer.Models;

/// <summary>
/// Represents a file or directory entry in a foreign file system.
/// </summary>
public class FileSystemEntry
{
    /// <summary>Entry name (file or directory name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path within the file system.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>True if this is a directory; false if it's a file.</summary>
    public bool IsDirectory { get; set; }

    /// <summary>File size in bytes (0 for directories).</summary>
    public long Size { get; set; }

    /// <summary>Last modification time.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>Creation time if available.</summary>
    public DateTime? CreationTime { get; set; }

    /// <summary>Last access time if available.</summary>
    public DateTime? LastAccessTime { get; set; }

    /// <summary>File attributes/permissions as a string (e.g., "rwxr-xr-x").</summary>
    public string Permissions { get; set; } = string.Empty;

    /// <summary>Owner user ID (for Unix-style file systems).</summary>
    public int? OwnerId { get; set; }

    /// <summary>Owner group ID (for Unix-style file systems).</summary>
    public int? GroupId { get; set; }

    /// <summary>Icon glyph for UI display.</summary>
    public string IconGlyph => IsDirectory ? "\uE8B7" : GetFileIconGlyph();

    /// <summary>Display text for size.</summary>
    public string DisplaySize => IsDirectory ? "" : FormatSize(Size);

    /// <summary>Display text for last modified date.</summary>
    public string DisplayDate => LastModified.ToString("yyyy-MM-dd HH:mm");

    private string GetFileIconGlyph()
    {
        var ext = Path.GetExtension(Name).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".log" or ".md" => "\uE8A5", // Document
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "\uEB9F", // Image
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "\uE8D6", // Music
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "\uE8B2", // Video
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "\uF12B", // Archive
            ".exe" or ".msi" or ".app" => "\uE74C", // Application
            ".pdf" => "\uEA90", // PDF
            ".doc" or ".docx" => "\uE8A5", // Word
            ".xls" or ".xlsx" => "\uE80A", // Excel
            ".ppt" or ".pptx" => "\uE8A1", // PowerPoint
            ".cs" or ".cpp" or ".c" or ".h" or ".py" or ".js" or ".ts" => "\uE943", // Code
            ".sh" or ".bash" or ".zsh" => "\uE756", // Script
            ".conf" or ".cfg" or ".ini" or ".json" or ".xml" or ".yaml" or ".yml" => "\uE713", // Config
            _ => "\uE7C3" // Generic file
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "";
        }

        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
