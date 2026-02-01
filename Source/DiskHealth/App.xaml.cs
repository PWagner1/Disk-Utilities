namespace DiskHealth;

/// <summary>
/// Application entry point and global resources for the Disk Health WinUI app.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var msg = e.Exception?.Message ?? "An unexpected error occurred.";
        if (e.Exception is UnauthorizedAccessException)
            msg = "Administrator privileges are required for raw disk access. Please run the application as administrator.";
        ShowMessageBox("Disk Health - Error", msg);
    }

    /// <summary>Shows a native message box as a fallback when ContentDialog may fail.</summary>
    internal static void ShowMessageBox(string title, string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, title, 0x40); // MB_OK | MB_ICONINFORMATION
        }
        catch { /* Last resort - ignore */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private MainWindow? m_window;
}
