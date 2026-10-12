namespace SuiteLauncher;

/// <summary>Entry point for the Disk Utilities suite launcher.</summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (cmd.Any(a => a is "--install-shell"))
        {
            string? hf = SuiteApps.ResolveExecutablePath(SuiteAppId.HandleFinder);
            string? ds = SuiteApps.ResolveExecutablePath(SuiteAppId.DriveStat);
            if (hf != null && ds != null && ExplorerShellIntegration.TryInstall(hf, ds, out string msg))
            {
                AppLog.Info(msg, source: nameof(App));
            }

            Environment.Exit(0);
            return;
        }

        if (cmd.Any(a => a is "--uninstall-shell"))
        {
            ExplorerShellIntegration.TryUninstall(out string msg);
            AppLog.Info(msg, source: nameof(App));
            Environment.Exit(0);
            return;
        }

        var window = new MainWindow();
        ApplyTheme(window);
        window.Activate();
    }

    internal static void ApplyTheme(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = SuiteSettings.GetTheme() switch
        {
            SuiteSettings.ThemeLight => ElementTheme.Light,
            SuiteSettings.ThemeDark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
