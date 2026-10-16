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
    public void ResolveExecutablePath_FindsSiblingAppFolder()
    {
        using var layout = new SuiteLayout();
        string expected = layout.Touch("DiskEraser", "DiskEraser.exe");

        string? found = SuiteApps.ResolveExecutablePath(SuiteAppId.DiskEraser, layout.AppDir("SuiteLauncher"));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void ResolveExecutablePath_PrefersExeBesideProcess()
    {
        using var layout = new SuiteLayout();
        string expected = layout.TouchFile(layout.Root, "DriveStat.exe");
        layout.Touch("DriveStat", "DriveStat.exe");

        string? found = SuiteApps.ResolveExecutablePath(SuiteAppId.DriveStat, layout.Root);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void ResolveExecutablePath_FindsAppSubfolder()
    {
        using var layout = new SuiteLayout();
        string expected = layout.Touch("HandleFinder", "HandleFinder.exe");

        string? found = SuiteApps.ResolveExecutablePath(SuiteAppId.HandleFinder, layout.Root);

        Assert.Equal(expected, found);
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

    private sealed class SuiteLayout : IDisposable
    {
        public SuiteLayout()
        {
            Root = Path.Combine(Path.GetTempPath(), "du-suite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string AppDir(string app) => Path.Combine(Root, app);

        public string Touch(string app, string exeFileName)
        {
            string dir = AppDir(app);
            Directory.CreateDirectory(dir);
            return TouchFile(dir, exeFileName);
        }

        public string TouchFile(string directory, string exeFileName)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, exeFileName);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Temp leftovers are harmless if a file is briefly locked.
            }
        }
    }
}
