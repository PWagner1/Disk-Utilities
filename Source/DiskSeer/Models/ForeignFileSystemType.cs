namespace DiskSeer.Models;

/// <summary>
/// Supported foreign file system types.
/// </summary>
public enum ForeignFileSystemType
{
    Unknown,
    // Linux file systems
    Ext2,
    Ext3,
    Ext4,
    Btrfs,
    XFS,
    // Apple file systems
    HFSPlus,
    APFS // Note: APFS has limited support
}