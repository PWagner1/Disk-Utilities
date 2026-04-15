namespace DiskEraser.Models;

/// <summary>
/// A single row in the partition management list: either a partition or unallocated space.
/// </summary>
public sealed class PartitionListEntry
{
    /// <summary>
    /// Gets or sets the display text.
    /// </summary>
    /// <value>
    /// The display text.
    /// </value>
    public string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this instance is unallocated.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is unallocated; otherwise, <c>false</c>.
    /// </value>
    public bool IsUnallocated { get; set; }

    /// <summary>
    /// Gets or sets the partition.
    /// </summary>
    /// <value>
    /// The partition.
    /// </value>
    public PartitionInfo? Partition { get; set; }

    /// <summary>
    /// Gets or sets the unallocated.
    /// </summary>
    /// <value>
    /// The unallocated.
    /// </value>
    public UnallocatedInfo? Unallocated { get; set; }

    /// <summary>
    /// Gets or sets the index of the disk.
    /// </summary>
    /// <value>
    /// The index of the disk.
    /// </value>
    public int DiskIndex { get; set; }
}
