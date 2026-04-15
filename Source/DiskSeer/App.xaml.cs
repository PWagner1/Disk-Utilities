namespace DiskSeer;

/// <summary>
/// Application entry point and global resources for the DiskSeer WinUI app.
/// Provides access to Apple (HFS+, APFS) and Linux (ext2/3/4, Btrfs, XFS) file systems on Windows.
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
        _mWindow.Activate();
    }

    private MainWindow? _mWindow;
}
