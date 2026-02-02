namespace DriveStat.Models;

/// <summary>
/// Statistics for a file extension (e.g. .txt, .exe).
/// </summary>
public sealed class ExtensionInfo
{
    /// <summary>Extension including the dot (e.g. ".txt"). "(no extension)" for files without extension.</summary>
    public string Extension { get; }

    /// <summary>Total size in bytes for this extension.</summary>
    public long Size { get; }

    /// <summary>Number of files with this extension.</summary>
    public int FileCount { get; }

    /// <summary>Percentage of total size (0–100).</summary>
    public double Percentage { get; }

    /// <summary>Color for treemap display.</summary>
    public Windows.UI.Color Color { get; }

    /// <summary>Formatted size string for display.</summary>
    public string SizeFormatted => DirectoryNode.FormatSize(Size);

    /// <summary>Formatted file count for display.</summary>
    public string FileCountDisplay => $"{FileCount:N0} files";

    /// <summary>Brush for extension list color swatch.</summary>
    public SolidColorBrush ColorBrush => new(Color);

    public ExtensionInfo(string extension, long size, int fileCount, double percentage, Windows.UI.Color color)
    {
        Extension = extension;
        Size = size;
        FileCount = fileCount;
        Percentage = percentage;
        Color = color;
    }
}
