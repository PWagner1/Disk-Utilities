namespace DiskSeer.Services;

/// <summary>
/// Describes what DiskSeer can safely do per foreign file system type.
/// </summary>
public static class FileSystemCapabilities
{
    public static bool IsApfsLibraryAvailable => Apfs.ApfsFileSystem.IsAvailable;

    /// <summary>True when the adapter may attempt write operations (still subject to DiscUtils limits).</summary>
    public static bool SupportsWriteAttempts(ForeignFileSystemType type) => type switch
    {
        ForeignFileSystemType.Apfs => false,
        ForeignFileSystemType.Unknown => false,
        _ => true
    };

    public static string Describe(ForeignFileSystemType type) => type switch
    {
        ForeignFileSystemType.Apfs => IsApfsLibraryAvailable
            ? "APFS: browse and copy out only (libfsapfs). Writes are not supported."
            : "APFS detected, but libfsapfs-1.dll was not found next to DiskSeer.exe. See Docs/LIBFSAPFS_BUILD.md.",
        ForeignFileSystemType.Ext2 or ForeignFileSystemType.Ext3 or ForeignFileSystemType.Ext4
            => "ext: browse/copy via DiscUtils. Writes are experimental — prefer read-only and copy-out.",
        ForeignFileSystemType.HfsPlus
            => "HFS+: browse/copy via DiscUtils. Writes are experimental — prefer read-only and copy-out.",
        ForeignFileSystemType.Btrfs
            => "Btrfs: browse/copy via DiscUtils. Writes are experimental — prefer read-only and copy-out.",
        ForeignFileSystemType.Xfs
            => "XFS: browse/copy via DiscUtils. Writes are experimental — prefer read-only and copy-out.",
        _ => "Unknown or unsupported file system."
    };

    public static string ExpectedApfsDllNames => "libfsapfs-1.dll (plus its dependency DLLs)";

    public static string GetApfsLibrarySearchHint()
    {
        string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return $"Place {ExpectedApfsDllNames} in:\n{dir}";
    }
}
