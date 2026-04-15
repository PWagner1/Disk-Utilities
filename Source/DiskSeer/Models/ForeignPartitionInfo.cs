using DiskSeer.Services.Apfs;

namespace DiskSeer.Models;

/// <summary>
/// Represents a partition that may contain an Apple or Linux file system.
/// </summary>
public class ForeignPartitionInfo
{
    /// <summary>Partition index (0-based within the disk).</summary>
    public int PartitionIndex { get; set; }

    /// <summary>Starting offset in bytes.</summary>
    public ulong Offset { get; set; }

    /// <summary>Partition size in bytes.</summary>
    public ulong Size { get; set; }

    /// <summary>Detected file system type.</summary>
    public ForeignFileSystemType FileSystemType { get; set; } = ForeignFileSystemType.Unknown;

    /// <summary>Volume label if available.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Windows drive letter if mounted, empty otherwise.</summary>
    public string DriveLetter { get; set; } = string.Empty;

    /// <summary>True if this partition can be read by DiskSeer.</summary>
    public bool IsSupported => FileSystemType switch
    {
        ForeignFileSystemType.Unknown => false,
        ForeignFileSystemType.Apfs => ApfsFileSystem.IsAvailable,
        _ => true
    };

    /// <summary>Display text for UI.</summary>
    public string DisplayText
    {
        get
        {
            var label = string.IsNullOrEmpty(Label) ? "No Label" : Label;
            var letter = string.IsNullOrEmpty(DriveLetter) ? "" : $" ({DriveLetter})";
            return $"Partition {PartitionIndex + 1}{letter}: {label} - {FileSystemType} ({FormatSize(Size)})";
        }
    }

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