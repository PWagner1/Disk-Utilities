namespace HandleFinder;

/// <summary>
/// Application entry point for HandleFinder - finds processes locking files or folders.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (cmdArgs.Length > 0)
        {
            var exitCode = await CliRunner.RunAsync(cmdArgs);
            Environment.Exit(exitCode);
            return;
        }

        _mainWindow = new MainWindow();
        ApplyMicaBackdrop(_mainWindow);
        _mainWindow.Activate();
    }

    private static void ApplyMicaBackdrop(Window window)
    {
        try
        {
            if (MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
        }
        catch
        {
            // Mica not supported
        }
    }

    private MainWindow? _mainWindow;
}
