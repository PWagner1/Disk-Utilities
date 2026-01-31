namespace DiskEraser.Models;

/// <summary>
/// Represents unallocated space on a disk for the partition management UI.
/// </summary>
public sealed class UnallocatedInfo
{
    public int DiskIndex { get; set; }
    public ulong Size { get; set; }
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
