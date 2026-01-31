namespace DiskEraser.Models;

/// <summary>
/// A single row in the partition management list: either a partition or unallocated space.
/// </summary>
public sealed class PartitionListEntry
{
    public string DisplayText { get; set; } = string.Empty;
    public bool IsUnallocated { get; set; }
    public PartitionInfo? Partition { get; set; }
    public UnallocatedInfo? Unallocated { get; set; }
    public int DiskIndex { get; set; }
}
