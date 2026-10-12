using System.Runtime.InteropServices;
using DriveStat.Models;
using DriveStat.Services;

namespace DiskUtilities.Tests;

public class ScanServiceTests
{
    [Fact]
    public async Task ScanAsync_TempTree_TotalsAndExtensions()
    {
        string root = Path.Combine(Path.GetTempPath(), "DiskUtilities.Tests.Scan." + Guid.NewGuid().ToString("N"));
        string sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "a.txt"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(root, "b.bin"), new byte[50]);
            await File.WriteAllBytesAsync(Path.Combine(sub, "c.txt"), new byte[25]);
            await File.WriteAllBytesAsync(Path.Combine(sub, "readme"), new byte[10]);

            DirectoryNode tree = await ScanService.ScanAsync(root);
            Assert.Equal(185, tree.Size);

            var extensions = ScanService.BuildExtensionList(tree);
            Assert.Contains(extensions, e => e.Extension == ".txt" && e.FileCount == 2 && e.Size == 125);
            Assert.Contains(extensions, e => e.Extension == ".bin" && e.FileCount == 1 && e.Size == 50);
            Assert.Contains(extensions, e => e.Extension == "(no extension)" && e.FileCount == 1 && e.Size == 10);

            string csv = ExportService.BuildCsv(tree, extensions);
            Assert.Contains("a.txt", csv);
            Assert.Contains(".txt", csv);
            string html = ExportService.BuildHtml(tree, extensions);
            Assert.Contains("DriveStat report", html);
            Assert.Contains("Extensions", html);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ScanAsync_HardLink_CountsDataOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "DiskUtilities.Tests.HardLink." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "original.dat");
        string link = Path.Combine(root, "link.dat");

        try
        {
            await File.WriteAllBytesAsync(original, new byte[200]);
            if (!CreateHardLink(link, original, IntPtr.Zero))
            {
                // Hard links may be unavailable on some volumes; don't fail the suite.
                return;
            }

            DirectoryNode tree = await ScanService.ScanAsync(root);
            Assert.Equal(200, tree.Size);
            Assert.Equal(1, tree.Children.Count(c => c.IsHardLinkDuplicate));
            Assert.Equal(2, tree.Children.Count(c => c.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [Fact]
    public async Task ScanAsync_PermissionDeniedChild_SkipsAndContinues()
    {
        string root = Path.Combine(Path.GetTempPath(), "DiskUtilities.Tests.ScanDenied." + Guid.NewGuid().ToString("N"));
        string denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(denied);
        await File.WriteAllBytesAsync(Path.Combine(denied, "secret.bin"), new byte[99]);
        await File.WriteAllBytesAsync(Path.Combine(root, "ok.dat"), new byte[40]);

        try
        {
            RunIcacls($"\"{denied}\" /deny Everyone:(OI)(CI)F");

            DirectoryNode tree = await ScanService.ScanAsync(root);
            Assert.Equal(40, tree.Size);
            Assert.Contains(tree.Children, c => !c.IsDirectory && c.Name == "ok.dat");
            // Denied child may appear as an empty directory node or be skipped entirely.
            Assert.DoesNotContain(tree.Children, c => c.Name == "secret.bin");
        }
        finally
        {
            try
            {
                RunIcacls($"\"{denied}\" /reset /T /C /Q");
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void RunIcacls(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "icacls",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        process?.WaitForExit(15_000);
    }
}
