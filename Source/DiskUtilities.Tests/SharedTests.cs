using DiskUtilities.Shared;

namespace DiskUtilities.Tests;

public class SharedTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(512UL, "512 B")]
    [InlineData(1024UL, "1 KB")]
    [InlineData(1536UL, "1.5 KB")]
    [InlineData(1048576UL, "1 MB")]
    public void ByteFormatter_FormatsBinaryUnits(ulong bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.Format(bytes));
    }

    [Fact]
    public void ByteFormatter_NegativeTreatedAsZero()
    {
        Assert.Equal("0 B", ByteFormatter.Format(-1));
    }

    [Fact]
    public void AppLog_ErrorSetsAndConsumeClearsLastError()
    {
        AppLog.ClearLastError();
        AppLog.Error("unit-test failure", source: "SharedTests");
        Assert.Equal("unit-test failure", AppLog.LastError);
        Assert.Equal("unit-test failure", AppLog.ConsumeLastError());
        Assert.Null(AppLog.LastError);
    }

    [Fact]
    public void DestructiveConfirm_FormatVolumeMessage_MentionsOverwriteLimits()
    {
        string plain = DestructiveConfirm.FormatVolumeMessage("D:", "NTFS", freeSpaceOverwrite: false, wipePasses: 0);
        Assert.Contains("D:", plain);
        Assert.Contains("NTFS", plain);
        Assert.DoesNotContain("Secure Erase", plain);

        string withWipe = DestructiveConfirm.FormatVolumeMessage("E:", "exFAT", freeSpaceOverwrite: true, wipePasses: 3);
        Assert.Contains("3 pass", withWipe);
        Assert.Contains("not hardware Secure Erase", withWipe);
    }

    [Fact]
    public void DestructiveConfirm_DeleteAndProcessMessages()
    {
        Assert.Contains("cannot be undone", DestructiveConfirm.DeletePartitionMessage("Partition 2"));
        Assert.Contains("2 items", DestructiveConfirm.DeleteItemsMessage(2));
        Assert.Contains("1 item?", DestructiveConfirm.DeleteItemsMessage(1));
        Assert.Contains("PID 42", DestructiveConfirm.EndProcessMessage("notepad", 42));
        Assert.Contains("could crash", DestructiveConfirm.CloseHandleMessage("explorer", 4));
    }
}
