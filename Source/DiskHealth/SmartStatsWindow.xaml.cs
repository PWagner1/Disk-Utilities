namespace DiskHealth;

public sealed partial class SmartStatsWindow : Window
{
    public SmartStatsWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        _ = LoadSmartDrivesAsync();
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
        }
        catch
        {
            // ignored
        }
    }

    private void TrySetExtendedTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch
        {
            // ignored
        }
    }

    private async Task LoadSmartDrivesAsync()
    {
        RefreshSmartButton.IsEnabled = false;
        SmartHealthText.Text = "Loading...";

        try
        {
            var drives = await SmartService.GetSmartDrivesAsync();
            SmartDriveCombo.Items.Clear();
            foreach (var d in drives)
            {
                SmartDriveCombo.Items.Add(d);
            }

            if (SmartDriveCombo.Items.Count > 0)
            {
                SmartDriveCombo.SelectedIndex = 0;
            }
            else
            {
                SmartHealthText.Text = "No S.M.A.R.T. drives found";
            }
        }
        catch (Exception ex)
        {
            SmartHealthText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshSmartButton.IsEnabled = true;
        }
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

    private async void RefreshSmartButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadSmartDrivesAsync();
    }
}
