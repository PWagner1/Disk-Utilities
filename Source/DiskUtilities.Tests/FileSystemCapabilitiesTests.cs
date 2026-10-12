using DiskSeer.Models;
using DiskSeer.Services;
using DiskSeer.Services.Apfs;

namespace DiskUtilities.Tests;

public class FileSystemCapabilitiesTests
{
    [Fact]
    public void Apfs_NeverSupportsWriteAttempts()
    {
        Assert.False(FileSystemCapabilities.SupportsWriteAttempts(ForeignFileSystemType.Apfs));
    }

    [Fact]
    public void ExtAndHfs_AllowWriteAttempts()
    {
        Assert.True(FileSystemCapabilities.SupportsWriteAttempts(ForeignFileSystemType.Ext4));
        Assert.True(FileSystemCapabilities.SupportsWriteAttempts(ForeignFileSystemType.HfsPlus));
    }

    [Fact]
    public void Describe_ApfsMentionsLibraryOrReadOnly()
    {
        string text = FileSystemCapabilities.Describe(ForeignFileSystemType.Apfs);
        Assert.Contains("APFS", text);
        Assert.True(
            text.Contains("libfsapfs", StringComparison.OrdinalIgnoreCase)
            || text.Contains("read-only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Read-only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("copy out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApfsTempCopyLimit_IsAtLeast128GiB()
    {
        Assert.True(ApfsFileSystem.MaxTempCopyBytes >= 128L * 1024 * 1024 * 1024);
    }
}
