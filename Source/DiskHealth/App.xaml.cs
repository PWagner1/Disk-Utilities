namespace DiskHealth;

/// <summary>
/// Application entry point and global resources for the Disk Health WinUI app.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();
    }

    private MainWindow? m_window;
}
