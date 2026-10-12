using DiskUtilities.Shared.Localization;

namespace SuiteLauncher;

/// <summary>Lists suite tools, suite theme seed, and launches sibling executables.</summary>
public sealed partial class MainWindow : Window
{
    private bool _loadingSettings;

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        WindowChrome.TrySetSize(this, 780, 820);
        ApplyLocalizedUi();
        ConfigureElevationBanner();
        LoadSettingsUi();
        RefreshAppList();
        RefreshShellStatus();
    }

    private void ApplyLocalizedUi()
    {
        SuiteResources.EnsureLoaded();
        Title = SuiteResources.Get("Suite.Title", "Disk Utilities");
        if (TitleText != null)
        {
            TitleText.Text = Title;
        }

        if (IntroText != null)
        {
            IntroText.Text = SuiteResources.Get("Suite.Intro", IntroText.Text);
        }

        InstallShellButton.Content = SuiteResources.Get("Suite.InstallShell", "Install Explorer menus");
        RemoveShellButton.Content = SuiteResources.Get("Suite.RemoveShell", "Remove Explorer menus");
        AckCheck.Content = SuiteResources.Get("Suite.AckDestructive", AckCheck.Content?.ToString() ?? "");
        RestartAsAdminButton.Content = SuiteResources.Get("Suite.RestartAdmin", "Restart as administrator");

        AccessibilityUi.SetName(ThemeCombo, SuiteResources.Get("A11y.ThemeCombo"));
        AccessibilityUi.SetName(AckCheck, SuiteResources.Get("A11y.AckDestructive"));
        AccessibilityUi.SetName(InstallShellButton, SuiteResources.Get("A11y.InstallShell"));
        AccessibilityUi.SetName(RemoveShellButton, SuiteResources.Get("A11y.RemoveShell"));
        AccessibilityUi.SetName(AppList, SuiteResources.Get("A11y.AppList"));
        AccessibilityUi.SetHelpText(InstallShellButton, SuiteResources.Get("A11y.InstallShell"));

        InstallShellButton.AccessKey = "I";
        RemoveShellButton.AccessKey = "R";
        ThemeCombo.AccessKey = "T";
    }

    private void RefreshShellStatus()
    {
        if (ShellStatusText == null)
        {
            return;
        }

        ShellStatusText.Text = ExplorerShellIntegration.IsInstalled()
            ? "Explorer menus: installed (this user)."
            : "Explorer menus: not installed.";
    }

    private void InstallShellButton_Click(object sender, RoutedEventArgs e)
    {
        string? hf = SuiteApps.ResolveExecutablePath(SuiteAppId.HandleFinder);
        string? ds = SuiteApps.ResolveExecutablePath(SuiteAppId.DriveStat);
        if (hf == null || ds == null)
        {
            FooterStatus.Text = "Build HandleFinder and DriveStat into this folder first.";
            return;
        }

        bool ok = ExplorerShellIntegration.TryInstall(hf, ds, out string message);
        FooterStatus.Text = message;
        RefreshShellStatus();
        if (!ok)
        {
            return;
        }
    }

    private void RemoveShellButton_Click(object sender, RoutedEventArgs e)
    {
        ExplorerShellIntegration.TryUninstall(out string message);
        FooterStatus.Text = message;
        RefreshShellStatus();
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Launcher is not elevated. Tools that require Administrator will still prompt when you launch them (or use Launch elevated).",
            RestartAsAdminButton,
            null,
            "Restart the launcher as Administrator if you want child processes to inherit elevation where the OS allows.");
    }

    private void LoadSettingsUi()
    {
        _loadingSettings = true;
        string theme = SuiteSettings.GetTheme();
        ThemeCombo.SelectedIndex = theme switch
        {
            SuiteSettings.ThemeLight => 1,
            SuiteSettings.ThemeDark => 2,
            _ => 0
        };
        AckCheck.IsChecked = SuiteSettings.GetRequireDestructiveAcknowledgement();
        _loadingSettings = false;
    }

    private void RefreshAppList()
    {
        var items = new List<LauncherAppItem>();
        foreach (var app in SuiteApps.LauncherTools)
        {
            string? path = SuiteApps.ResolveExecutablePath(app.Id);
            string related = app.RelatedApps.Count == 0
                ? string.Empty
                : "Related: " + string.Join(", ", app.RelatedApps.Select(id => SuiteApps.Get(id)?.DisplayName ?? id.ToString()));

            items.Add(new LauncherAppItem
            {
                Id = app.Id,
                DisplayName = app.DisplayName,
                Description = app.ShortDescription,
                ElevationHint = app.ElevationHint,
                RelatedHint = related,
                StatusText = path == null ? $"Not found beside launcher (build {app.ExeFileName} into the same folder)." : string.Empty,
                ElevateVisible = ElevationHelper.IsElevated
                    ? Visibility.Collapsed
                    : Visibility.Visible
            });
        }

        AppList.ItemsSource = items;
        FooterStatus.Text = ElevationHelper.IsElevated
            ? "Running as Administrator."
            : "Running as standard user. Suite settings: " + SuiteSettings.SettingsPath;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string theme = item.Tag as string ?? SuiteSettings.ThemeDefault;
        SuiteSettings.SetTheme(theme);
        App.ApplyTheme(this);
    }

    private void AckCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        SuiteSettings.SetRequireDestructiveAcknowledgement(AckCheck.IsChecked == true);
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuiteAppId id })
        {
            Launch(id, elevated: false);
        }
    }

    private void LaunchElevatedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuiteAppId id })
        {
            Launch(id, elevated: true);
        }
    }

    private void Launch(SuiteAppId id, bool elevated)
    {
        bool ok = SuiteApps.TryLaunch(id, elevated: elevated);
        FooterStatus.Text = ok
            ? $"Started {SuiteApps.Get(id)?.DisplayName ?? id.ToString()}{(elevated ? " (elevated)" : string.Empty)}."
            : $"Could not start {id}. Build the suite so executables share one output folder, then retry.";
        if (!ok)
        {
            RefreshAppList();
        }
    }
}
