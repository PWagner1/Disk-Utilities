namespace DiskEraser;

/// <summary>
/// Application entry point and global resources for the Disk Eraser WinUI app.
/// </summary>
public partial class App : Application
{
    /// <summary>Creates the application and sets the main window.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Invoked when the application is launched. Creates and activates the main window.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mWindow = new MainWindow();
        ApplyTheme(_mWindow);
        _mWindow.Activate();
    }

    /// <summary>Applies the saved theme (Light/Dark/Default) to a window's root content.</summary>
    internal static void ApplyTheme(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = AppSettings.GetTheme() switch
        {
            AppSettings.ThemeLight => ElementTheme.Light,
            AppSettings.ThemeDark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    /// <summary>Main window reference for applying theme when changed in Settings.</summary>
    internal static Window? MainWindow => (Current as App)?._mWindow;

    private MainWindow? _mWindow;
}
