using Microsoft.UI.Xaml;

namespace DriveWiper;

/// <summary>
/// Application entry point and global resources for the Drive Wiper WinUI app.
/// </summary>
public partial class App : Application
{
    /// <summary>Creates the application and sets the main window.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Invoked when the application is launched. Creates and activates the main window.</summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();
    }

    private MainWindow? m_window;
}
