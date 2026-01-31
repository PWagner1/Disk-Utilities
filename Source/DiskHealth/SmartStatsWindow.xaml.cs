namespace DiskHealth;

public sealed partial class SmartStatsWindow : Window
{
    public SmartStatsWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        LoadSmartDrives();
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch { }
    }

    private void TrySetExtendedTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch { }
    }

    private void LoadSmartDrives()
    {
        var drives = SmartService.GetSmartDrives();
        SmartDriveCombo.Items.Clear();
        foreach (var d in drives)
            SmartDriveCombo.Items.Add(d);
        if (SmartDriveCombo.Items.Count > 0)
            SmartDriveCombo.SelectedIndex = 0;
    }

    private void SmartDriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SmartDriveCombo.SelectedItem is SmartDriveInfo info)
        {
            SmartHealthText.Text = info.HealthStatus;
            SmartAttributesList.ItemsSource = info.Attributes;
        }
        else
        {
            SmartHealthText.Text = "—";
            SmartAttributesList.ItemsSource = null;
        }
    }

    private void RefreshSmartButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSmartDrives();
    }
}
