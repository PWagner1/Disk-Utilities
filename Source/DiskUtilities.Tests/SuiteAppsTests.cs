using DiskUtilities.Shared;

namespace DiskUtilities.Tests;

public class SuiteAppsTests
{
    [Fact]
    public void LauncherTools_IncludesFiveApps_ExcludesLauncher()
    {
        Assert.Equal(5, SuiteApps.LauncherTools.Count);
        Assert.DoesNotContain(SuiteApps.LauncherTools, a => a.Id == SuiteAppId.SuiteLauncher);
        Assert.Contains(SuiteApps.LauncherTools, a => a.Id == SuiteAppId.DiskEraser);
        Assert.Contains(SuiteApps.LauncherTools, a => a.Id == SuiteAppId.HandleFinder);
    }

    [Fact]
    public void Get_ReturnsCatalogEntry()
    {
        var info = SuiteApps.Get(SuiteAppId.DriveStat);
        Assert.NotNull(info);
        Assert.Equal("DriveStat.exe", info!.ExeFileName);
        Assert.Contains(SuiteAppId.HandleFinder, info.RelatedApps);
    }

    [Fact]
    public void SuiteSettings_ThemeRoundTrip()
    {
        string previous = SuiteSettings.GetTheme();
        try
        {
            SuiteSettings.SetTheme(SuiteSettings.ThemeDark);
            Assert.Equal(SuiteSettings.ThemeDark, SuiteSettings.GetTheme());
            SuiteSettings.SetTheme("bogus");
            Assert.Equal(SuiteSettings.ThemeDefault, SuiteSettings.GetTheme());
        }
        finally
        {
            SuiteSettings.SetTheme(previous);
        }
    }
}
