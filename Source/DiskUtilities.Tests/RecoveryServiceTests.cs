using System.Text;
using DiskHealth.Models;
using DiskHealth.Services;

namespace DiskUtilities.Tests;

public class RecoveryServiceTests
{
    private static byte[] Pad(byte[] signature, int totalLength = 64)
    {
        var buffer = new byte[totalLength];
        signature.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public void FindSignaturesInBuffer_Empty_ReturnsEmpty()
    {
        Assert.Empty(RecoveryService.FindSignaturesInBuffer([]));
    }

    [Fact]
    public void FindSignaturesInBuffer_ShortBufferWithJpeg_FindsHit()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        var hits = RecoveryService.FindSignaturesInBuffer(jpeg);
        Assert.Single(hits);
        Assert.Equal(0, hits[0].Offset);
        Assert.Equal(".jpg", hits[0].Extension);
        Assert.Equal("JPEG", hits[0].Name);
    }

    [Fact]
    public void FindSignaturesInBuffer_JpegPngPdf_AtStart()
    {
        Assert.Equal("JPEG", RecoveryService.FindSignaturesInBuffer(Pad([0xFF, 0xD8, 0xFF]))[0].Name);
        Assert.Equal("PNG", RecoveryService.FindSignaturesInBuffer(Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))[0].Name);
        Assert.Equal("PDF", RecoveryService.FindSignaturesInBuffer(Pad([0x25, 0x50, 0x44, 0x46]))[0].Name);
    }

    [Fact]
    public void FindSignaturesInBuffer_DocxPreferredOverZip()
    {
        byte[] docx = Pad([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]);
        var hits = RecoveryService.FindSignaturesInBuffer(docx);
        Assert.Contains(hits, h => h.Offset == 0 && h.Extension == ".docx" && h.Name == "Office XML");
        Assert.DoesNotContain(hits, h => h.Offset == 0 && h.Extension == ".zip");
    }

    [Fact]
    public void FindSignaturesInBuffer_PlainZip_NotDocx()
    {
        byte[] zip = Pad([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00]);
        var hits = RecoveryService.FindSignaturesInBuffer(zip);
        Assert.Contains(hits, h => h.Offset == 0 && h.Extension == ".zip" && h.Name == "ZIP");
        Assert.DoesNotContain(hits, h => h.Offset == 0 && h.Extension == ".docx");
    }

    [Fact]
    public void FindSignaturesInBuffer_OfficeByLocalFileName()
    {
        // ZIP local header with filename "[Content_Types].xml"
        var buf = new byte[64];
        buf[0] = 0x50; buf[1] = 0x4B; buf[2] = 0x03; buf[3] = 0x04;
        string name = "[Content_Types].xml";
        buf[26] = (byte)name.Length;
        Encoding.ASCII.GetBytes(name).CopyTo(buf, 30);
        var hits = RecoveryService.FindSignaturesInBuffer(buf);
        Assert.Contains(hits, h => h.Offset == 0 && h.Extension == ".docx");
    }

    [Fact]
    public void FindSignaturesInBuffer_NewFormats()
    {
        Assert.Equal("7-Zip", RecoveryService.FindSignaturesInBuffer(Pad([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))[0].Name);
        Assert.Equal("RAR", RecoveryService.FindSignaturesInBuffer(Pad([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]))[0].Name);
        Assert.Equal("SQLite", RecoveryService.FindSignaturesInBuffer(Pad("SQLite format 3\0"u8.ToArray()))[0].Name);
        Assert.Equal("Matroska", RecoveryService.FindSignaturesInBuffer(Pad([0x1A, 0x45, 0xDF, 0xA3]))[0].Name);
        Assert.Equal("FLAC", RecoveryService.FindSignaturesInBuffer(Pad([0x66, 0x4C, 0x61, 0x43]))[0].Name);

        var webp = Pad([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P']);
        Assert.Equal("WebP", RecoveryService.FindSignaturesInBuffer(webp)[0].Name);

        var mp4 = new byte[64];
        mp4[4] = (byte)'f'; mp4[5] = (byte)'t'; mp4[6] = (byte)'y'; mp4[7] = (byte)'p';
        mp4[8] = (byte)'i'; mp4[9] = (byte)'s'; mp4[10] = (byte)'o'; mp4[11] = (byte)'m';
        Assert.Contains(RecoveryService.FindSignaturesInBuffer(mp4), h => h.Name == "MP4");
    }

    [Fact]
    public void FindSignaturesInBuffer_OffsetInsidePaddedBuffer()
    {
        var buffer = new byte[64];
        buffer[20] = 0xFF;
        buffer[21] = 0xD8;
        buffer[22] = 0xFF;
        var hits = RecoveryService.FindSignaturesInBuffer(buffer);
        Assert.Contains(hits, h => h.Offset == 20 && h.Name == "JPEG");
    }

    [Fact]
    public void RecoveryExport_CsvAndJson()
    {
        var files = new[]
        {
            new RecoverableFileInfo
            {
                DisplayName = "JPEG_recovered_0000.jpg",
                FileType = ".jpg",
                Offset = 100,
                RecoveryChancePercent = 70,
                IsSelected = true
            }
        };

        string csv = RecoveryExport.ToCsv(files);
        Assert.Contains("JPEG_recovered_0000.jpg", csv);
        Assert.Contains(".jpg", csv);
        string json = RecoveryExport.ToJson(files);
        Assert.Contains("\"Offset\": 100", json);
    }
}
