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

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
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
        if (RefreshButton != null)
            RefreshButton.IsEnabled = false;
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
                SmartHealthText.Text = "No drives found. Try running as administrator. S.M.A.R.T. supports SATA/ATA drives; NVMe shows health status only.";
                UpdateHealthVisualization(null);
            }
        }
        catch (Exception ex)
        {
            SmartHealthText.Text = $"Error: {ex.Message}";
            UpdateHealthVisualization(null);
        }
        finally
        {
            if (RefreshButton != null)
                RefreshButton.IsEnabled = true;
        }
    }

    private void SmartDriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SmartDriveCombo.SelectedItem is SmartDriveInfo info)
        {
            SmartHealthText.Text = info.HealthStatus;
            SmartAttributesList.ItemsSource = info.Attributes;
            UpdateHealthVisualization(info);
        }
        else
        {
            SmartHealthText.Text = "—";
            SmartAttributesList.ItemsSource = null;
            UpdateHealthVisualization(null);
        }
    }

    private void UpdateHealthVisualization(SmartDriveInfo? info)
    {
        if (info == null)
        {
            HealthScoreBar.Value = 0;
            HealthScoreText.Text = "—";
            HealthSummaryText.Text = "No drive selected.";
            HealthCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            return;
        }

        int score = CalculateHealthScore(info);
        HealthScoreBar.Value = score;
        HealthScoreText.Text = $"{score}/100";

        if (score >= 80)
        {
            HealthSummaryText.Text = "Healthy. No critical S.M.A.R.T. issues detected.";
            HealthCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
        }
        else if (score >= 50)
        {
            HealthSummaryText.Text = "Watch. Some S.M.A.R.T. indicators need monitoring.";
            HealthCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
        }
        else
        {
            HealthSummaryText.Text = "At risk. Backup data and consider replacing this drive.";
            HealthCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        }
    }

    private static int CalculateHealthScore(SmartDriveInfo info)
    {
        if (info.PredictFailure == true)
            return 15;

        int score = 100;
        var attrs = info.Attributes;

        // Penalize known critical attributes when raw values are non-zero.
        score -= attrs.Where(a => a.IsCritical && a.Raw > 0).Sum(_ => 12);

        // Extra penalties for common hard-failure markers.
        score -= attrs.Where(a => a.Id is 0x05 or 0xC5 or 0xC6 && a.Raw > 0).Sum(_ => 10);

        // If no attributes are available (often NVMe via WMI), keep a neutral score.
        if (attrs.Count == 0 && info.PredictFailure is null)
            score = 65;

        return Math.Clamp(score, 0, 100);
    }

}
