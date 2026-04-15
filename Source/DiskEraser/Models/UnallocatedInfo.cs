namespace DiskEraser.Models;

/// <summary>
/// Represents unallocated space on a disk for the partition management UI.
/// </summary>
public sealed class UnallocatedInfo
{
    /// <summary>
    /// Gets or sets the index of the disk.
    /// </summary>
    /// <value>
    /// The index of the disk.
    /// </value>
    public int DiskIndex { get; set; }

    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    /// <value>
    /// The size.
    /// </value>
    public ulong Size { get; set; }

    /// <summary>
    /// Gets the size of the formatted.
    /// </summary>
    /// <value>
    /// The size of the formatted.
    /// </value>
    public string FormattedSize => FormatBytes(Size);

    /// <summary>
    /// Formats the bytes.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns></returns>
    private static string FormatBytes(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
