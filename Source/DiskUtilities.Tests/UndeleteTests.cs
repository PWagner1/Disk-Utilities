using System.Text;
using DiskHealth.Services;

namespace DiskUtilities.Tests;

public class UndeleteTests
{
    [Fact]
    public void RecycleBin_ParseInfoBytes_Version2()
    {
        string path = @"C:\Users\Test\Documents\report.docx";
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(2L)); // version
        ms.Write(BitConverter.GetBytes(12345L)); // size
        ms.Write(BitConverter.GetBytes(DateTimeOffset.UtcNow.ToFileTime()));
        ms.Write(BitConverter.GetBytes(path.Length));
        ms.Write(Encoding.Unicode.GetBytes(path));

        Assert.True(RecycleBinRecovery.TryParseInfoBytes(ms.ToArray(), out var meta));
        Assert.Equal(path, meta.OriginalPath);
        Assert.Equal(12345L, meta.FileSize);
    }

    [Fact]
    public void Mft_ParseDataRuns_RelativeLcn()
    {
        // length 1 byte = 0x10 clusters, offset 1 byte = +0x20 → LCN 0x20
        byte[] runs = [0x11, 0x10, 0x20, 0x00];
        var list = new List<(long Lcn, long Clusters)>();
        MftDeletedScanner.ParseDataRuns(runs, list);
        Assert.Single(list);
        Assert.Equal(0x20, list[0].Lcn);
        Assert.Equal(0x10, list[0].Clusters);
    }

    [Fact]
    public void Mft_TryParseDeletedRecord_RequiresUnusedFlagAndFileName()
    {
        // Minimal synthetic FILE record with unused flag + resident $FILE_NAME is complex;
        // ensure in-use records are rejected.
        var record = new byte[1024];
        record[0] = (byte)'F';
        record[1] = (byte)'I';
        record[2] = (byte)'L';
        record[3] = (byte)'E';
        BitConverter.GetBytes((ushort)0x0038).CopyTo(record, 0x14); // first attr
        BitConverter.GetBytes((ushort)0x0001).CopyTo(record, 0x16); // in use
        Assert.False(MftDeletedScanner.TryParseDeletedRecord(record, 4096, out _));
    }
}
