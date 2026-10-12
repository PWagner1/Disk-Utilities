using HandleFinder.Services;

namespace DiskUtilities.Tests;

public class HandleServiceTests
{
    [Fact]
    public void AccessRightsFormatter_DecodesCommonBits()
    {
        string text = AccessRightsFormatter.Format(0x00120089); // Sync + ReadControl + ReadAttr + Read
        Assert.Contains("0x00120089", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sync", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizePath_TrimsTrailingSeparatorsAndUppercases()
    {
        string normalized = HandleService.NormalizePath(@"C:\Temp\Folder\");
        Assert.Equal(@"C:\TEMP\FOLDER", normalized);
        Assert.False(normalized.EndsWith('\\') || normalized.EndsWith('/'));
    }

    [Fact]
    public void PathMatches_ExactPath()
    {
        string target = HandleService.NormalizePath(@"C:\Temp\file.txt");
        string handle = HandleService.NormalizePath(@"C:\Temp\file.txt");
        Assert.True(HandleService.PathMatches(target, handle));
    }

    [Fact]
    public void PathMatches_ChildUnderFolderPrefix()
    {
        string target = HandleService.NormalizePath(@"C:\Temp\Folder");
        string child = HandleService.NormalizePath(@"C:\Temp\Folder\nested\a.txt");
        Assert.True(HandleService.PathMatches(target, child));
    }

    [Fact]
    public void PathMatches_SiblingOrUnrelated_DoesNotMatch()
    {
        string target = HandleService.NormalizePath(@"C:\Temp\Folder");
        string sibling = HandleService.NormalizePath(@"C:\Temp\Folder2\a.txt");
        string other = HandleService.NormalizePath(@"D:\Other\file.txt");
        Assert.False(HandleService.PathMatches(target, sibling));
        Assert.False(HandleService.PathMatches(target, other));
    }

    [Fact]
    public void PathMatches_PrefixWithoutSeparator_DoesNotMatch()
    {
        // "C:\Temp\File" must not match "C:\Temp\FileExtra"
        string target = HandleService.NormalizePath(@"C:\Temp\File");
        string similar = HandleService.NormalizePath(@"C:\Temp\FileExtra");
        Assert.False(HandleService.PathMatches(target, similar));
    }
}
