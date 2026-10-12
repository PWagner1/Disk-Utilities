using DiskUtilities.Shared;

namespace DiskUtilities.Tests;

public class ExplorerShellTests
{
    [Fact]
    public void VerbRootPaths_CoverFileFolderDrive()
    {
        Assert.Contains(ExplorerShellIntegration.VerbRootPaths, p => p.Contains(@"*\shell", StringComparison.Ordinal));
        Assert.Contains(ExplorerShellIntegration.VerbRootPaths, p => p.Contains(@"Directory\shell", StringComparison.Ordinal));
        Assert.Contains(ExplorerShellIntegration.VerbRootPaths, p => p.Contains(@"Drive\shell", StringComparison.Ordinal));
        Assert.Equal("DiskUtilities.WhatHasThisLocked", ExplorerShellIntegration.HandleFinderVerb);
        Assert.Equal("DiskUtilities.AnalyzeDiskUsage", ExplorerShellIntegration.DriveStatVerb);
    }
}
