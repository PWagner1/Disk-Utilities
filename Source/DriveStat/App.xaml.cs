namespace DriveStat;

/// <summary>
/// Application entry point for the DriveStat disk usage statistics app.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? initialPath = null;
        for (int i = 0; i < cmd.Length; i++)
        {
            if (cmd[i] is "--path" or "-p" && i + 1 < cmd.Length)
            {
                initialPath = cmd[i + 1].Trim().Trim('"');
                break;
            }

            if (!cmd[i].StartsWith('-') && Directory.Exists(cmd[i].Trim().Trim('"')))
            {
                initialPath = cmd[i].Trim().Trim('"');
                break;
            }
        }

        var window = new MainWindow(initialPath);
        window.Activate();
    }
}
