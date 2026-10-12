namespace DiskEraser.Models;

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

    /// <summary>Free space in bytes (from volume); 0 if not available.</summary>
    public ulong FreeSpace { get; set; }

    /// <summary>True if this is a protected system/boot/EFI/recovery volume.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Short display label for the partition block (e.g. "C: (System)").</summary>
    public string DisplayLabel => string.IsNullOrEmpty(DriveLetter)
        ? (IsProtected ? "No letter (protected)" : "No letter")
        : IsProtected
            ? (DriveLetter.Equals("C:", StringComparison.OrdinalIgnoreCase)
                ? $"{DriveLetter} (System)"
                : $"{DriveLetter} (Protected)")
            : (string.IsNullOrWhiteSpace(Label) ? DriveLetter : $"{DriveLetter} ({Label})");

    /// <summary>Formatted size string (e.g. "100 GB").</summary>
    public string FormattedSize => FormatBytes(Size);

    /// <summary>Formatted free space string (e.g. "45 GB"); empty if unknown.</summary>
    public string FormattedFreeSpace => Capacity > 0 ? FormatBytes(FreeSpace) : string.Empty;

    /// <summary>Percent of capacity that is free (0–100); null if capacity unknown or zero.</summary>
    public double? PercentFree => Capacity > 0 ? (double)FreeSpace / Capacity * 100.0 : null;

    /// <summary>Percent of capacity that is used (0–100); null if capacity unknown or zero.</summary>
    public double? PercentUsed => PercentFree.HasValue ? 100.0 - PercentFree.Value : null;

    /// <summary>Builds rich tooltip text: label, size, file system, percent free/used, and optional disk health.</summary>
    public string GetTooltipText(string? diskHealth = null)
    {
        var lines = new List<string>
        {
            $"{DisplayLabel} — {FormattedSize} — {FileSystem}"
        };
        if (PercentFree.HasValue)
        {
            double pctFree = PercentFree.Value;
            double pctUsed = 100.0 - pctFree;
            lines.Add($"{pctUsed:0}% used, {pctFree:0}% free ({FormattedFreeSpace} free)");
        }
        if (!string.IsNullOrWhiteSpace(diskHealth))
        {
            lines.Add($"Health: {diskHealth.Trim()}");
        }
        return string.Join("\n", lines);
    }

    private static string FormatBytes(ulong bytes) => ByteFormatter.Format(bytes);
}
