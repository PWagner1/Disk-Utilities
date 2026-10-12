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

        // Suite / Explorer cross-link: open GUI with optional path (does not enter CLI mode).
        if (cmdArgs.Any(a => a is "--gui" or "-g"))
        {
            string? path = cmdArgs.FirstOrDefault(a => a is not ("--gui" or "-g") && !a.StartsWith('-'));
            _mainWindow = new MainWindow(path);
            _mainWindow.Activate();
            return;
        }

        if (cmdArgs.Length > 0)
        {
            var exitCode = await CliRunner.RunAsync(cmdArgs);
            Environment.Exit(exitCode);
            return;
        }

        _mainWindow = new MainWindow();
        // Mica applied in MainWindow via WindowChrome
        _mainWindow.Activate();
    }

    private MainWindow? _mainWindow;
}
