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
    Xfs,
    // Apple file systems
    HfsPlus,
    Apfs // Note: APFS has limited support
}