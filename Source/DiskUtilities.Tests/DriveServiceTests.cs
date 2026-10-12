using EraserDriveService = DiskEraser.Services.DriveService;

namespace DiskUtilities.Tests;

public class DriveServiceTests
{
    [Fact]
    public void IsProtectedVolume_SystemDriveLetter_IsProtected()
    {
        Assert.True(EraserDriveService.IsProtectedVolume("C:"));
        Assert.True(EraserDriveService.IsProtectedVolume("C:\\"));
        Assert.True(EraserDriveService.IsProtectedVolume("c:"));
    }

    [Fact]
    public void IsProtectedVolume_BootOrSystemFlags_IsProtected()
    {
        Assert.True(EraserDriveService.IsProtectedVolume("D:", bootVolume: true));
        Assert.True(EraserDriveService.IsProtectedVolume("D:", systemVolume: true));
    }

    [Fact]
    public void IsProtectedVolume_ProtectedLabelsAndPartitionTypes()
    {
        Assert.True(EraserDriveService.IsProtectedVolume("D:", label: "EFI"));
        Assert.True(EraserDriveService.IsProtectedVolume("D:", label: "Recovery"));
        Assert.True(EraserDriveService.IsProtectedVolume("D:", partitionType: "EFI System Partition"));
        Assert.True(EraserDriveService.IsProtectedVolume("D:", partitionType: "Recovery"));
    }

    [Fact]
    public void IsProtectedVolume_OrdinaryDataVolume_NotProtected()
    {
        Assert.False(EraserDriveService.IsProtectedVolume("D:", label: "Data", bootVolume: false, systemVolume: false));
        Assert.False(EraserDriveService.IsProtectedVolume("E:", label: "Backup"));
    }

    [Fact]
    public void FormatFileSystems_NativeOptionsAreSelectable()
    {
        Assert.Contains(EraserDriveService.FormatFileSystems, f => f.Name == "NTFS" && f.IsNativelySupported);
        Assert.Contains(EraserDriveService.FormatFileSystems, f => f.Name == "exFAT" && f.IsNativelySupported);
        Assert.Contains(EraserDriveService.FormatFileSystems, f => f.Name == "ext4" && !f.IsNativelySupported);
        Assert.Contains(EraserDriveService.FormatFileSystems, f => f.Name == "APFS" && !f.IsNativelySupported);
    }

    [Fact]
    public void BuildCreatePartitionCommands_WithAndWithoutSize()
    {
        Assert.Equal(
            new[] { "select disk 2", "create partition primary size=1024" },
            EraserDriveService.BuildCreatePartitionCommands(2, 1024));
        Assert.Equal(
            new[] { "select disk 1", "create partition primary" },
            EraserDriveService.BuildCreatePartitionCommands(1, 0));
    }

    [Fact]
    public void BuildDeletePartitionCommands()
    {
        Assert.Equal(
            new[] { "select disk 3", "select partition 2", "delete partition" },
            EraserDriveService.BuildDeletePartitionCommands(3, 2));
    }

    [Fact]
    public void BuildExtendAndShrinkVolumeCommands()
    {
        Assert.Equal(
            new[] { "select volume 5", "extend size=512" },
            EraserDriveService.BuildExtendVolumeCommands(5, 512));
        Assert.Equal(
            new[] { "select volume 5", "extend" },
            EraserDriveService.BuildExtendVolumeCommands(5, null));
        Assert.Equal(
            new[] { "select volume 4", "shrink desired=256" },
            EraserDriveService.BuildShrinkVolumeCommands(4, 256));
    }

    [Fact]
    public void WipeProgress_EstimateRemaining_ScalesWithBytesLeft()
    {
        var eta = DiskEraser.Models.WipeProgress.EstimateRemaining(
            TimeSpan.FromSeconds(10),
            bytesWritten: 100,
            bytesTotal: 200);
        Assert.NotNull(eta);
        Assert.InRange(eta!.Value.TotalSeconds, 9, 11);
    }

    [Fact]
    public void WipeProgress_EstimateRemaining_ReturnsNullWhenNoProgress()
    {
        Assert.Null(DiskEraser.Models.WipeProgress.EstimateRemaining(TimeSpan.Zero, 100, 200));
        Assert.Null(DiskEraser.Models.WipeProgress.EstimateRemaining(TimeSpan.FromSeconds(1), 0, 200));
        Assert.Null(DiskEraser.Models.WipeProgress.EstimateRemaining(TimeSpan.FromSeconds(1), 200, 200));
    }

    [Fact]
    public void HardwareErase_ParseNvmeSanicap_Bits()
    {
        var (crypto, block, overwrite) = DiskEraser.Services.HardwareEraseService.ParseNvmeSanicap(0b111);
        Assert.True(crypto);
        Assert.True(block);
        Assert.True(overwrite);
        var none = DiskEraser.Services.HardwareEraseService.ParseNvmeSanicap(0);
        Assert.False(none.crypto);
    }

    [Fact]
    public void HardwareErase_ParseAtaSecurityWords_Frozen()
    {
        var (supported, frozen) = DiskEraser.Services.HardwareEraseService.ParseAtaSecurityWords(0x0002, 0, 0x0008);
        Assert.True(supported);
        Assert.True(frozen);
    }
}
