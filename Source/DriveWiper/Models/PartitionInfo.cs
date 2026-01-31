namespace DriveWiper.Models;

/// <summary>
/// Represents a partition on a disk for the layout diagram.
/// Populated from WMI (Win32_DiskPartition, Win32_LogicalDisk, Win32_Volume).
/// </summary>
public sealed class PartitionInfo
{
    /// <summary>Partition index on the disk (0-based).</summary>
    public int PartitionIndex { get; set; }

    /// <summary>Partition size in bytes.</summary>
    public ulong Size { get; set; }

    /// <summary>Starting offset on disk in bytes.</summary>
    public ulong Offset { get; set; }

    /// <summary>Drive letter and colon (e.g. "C:") if assigned; otherwise empty.</summary>
    public string DriveLetter { get; set; } = string.Empty;

    /// <summary>Volume label from Win32_Volume.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>File system (e.g. NTFS, FAT32).</summary>
    public string FileSystem { get; set; } = string.Empty;

    /// <summary>Total capacity in bytes (from volume); may match <see cref="Size"/>.</summary>
    public ulong Capacity { get; set; }

    /// <summary>True if this is the protected system drive (C:).</summary>
    public bool IsProtected { get; set; }

    /// <summary>Short display label for the partition block (e.g. "C: System").</summary>
    public string DisplayLabel => string.IsNullOrEmpty(DriveLetter)
        ? "No letter"
        : (IsProtected ? $"{DriveLetter} (System)" : string.IsNullOrWhiteSpace(Label) ? DriveLetter : $"{DriveLetter} ({Label})");

    /// <summary>Formatted size string (e.g. "100 GB").</summary>
    public string FormattedSize => FormatBytes(Size);

    private static string FormatBytes(ulong bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
