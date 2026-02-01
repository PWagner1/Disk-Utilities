namespace DiskEraser.Models;

/// <summary>
/// Represents a physical disk and its partitions for the layout diagram.
/// Populated from WMI Win32_DiskDrive and Win32_DiskPartition.
/// </summary>
public sealed class DiskInfo
{
    /// <summary>Physical disk index (0-based).</summary>
    public int DiskIndex { get; set; }

    /// <summary>Disk model/caption (e.g. "Samsung SSD 870").</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Total disk size in bytes.</summary>
    public ulong Size { get; set; }

    /// <summary>Disk status from WMI (e.g. "OK", "Pred Fail"); empty if unknown.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Partitions on this disk, in order.</summary>
    public List<PartitionInfo> Partitions { get; set; } = [];

    /// <summary>Short display caption (e.g. "Disk 0 - 500 GB").</summary>
    public string DisplayCaption => $"Disk {DiskIndex} - {FormattedSize}";

    /// <summary>Formatted size string.</summary>
    public string FormattedSize => FormatBytes(Size);

    private static string FormatBytes(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
