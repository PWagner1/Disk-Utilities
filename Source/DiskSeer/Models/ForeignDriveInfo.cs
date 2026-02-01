namespace DiskSeer.Models;

/// <summary>
/// Represents a physical drive that may contain foreign (non-Windows) file systems.
/// </summary>
public class ForeignDriveInfo
{
    /// <summary>Physical drive index (e.g., 0 for \\.\PhysicalDrive0).</summary>
    public int DriveIndex { get; set; }

    /// <summary>Drive model name from WMI.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Total drive size in bytes.</summary>
    public ulong Size { get; set; }

    /// <summary>List of partitions on this drive.</summary>
    public List<ForeignPartitionInfo> Partitions { get; set; } = [];

    /// <summary>Display text for UI.</summary>
    public string DisplayText => $"Disk {DriveIndex}: {Model} ({FormatSize(Size)})";

    private static string FormatSize(ulong bytes)
    {
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