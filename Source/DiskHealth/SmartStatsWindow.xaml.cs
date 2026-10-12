namespace DiskHealth;

public sealed partial class SmartStatsWindow : Window
{
    public SmartStatsWindow()
    {
        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        _ = LoadSmartDrivesAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadSmartDrivesAsync();
    }

    private async Task LoadSmartDrivesAsync()
    {
        if (RefreshButton != null)
            RefreshButton.IsEnabled = false;
        SmartHealthText.Text = "Loading...";

        try
        {
            AppLog.ClearLastError();
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
                string? error = AppLog.ConsumeLastError();
                SmartHealthText.Text = !string.IsNullOrEmpty(error)
                    ? error
                    : "No drives found. Try running as administrator.";
                UpdateHealthVisualization(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("S.M.A.R.T. window failed to load drives.", ex, nameof(SmartStatsWindow));
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
            DriveKindText.Text = info.IsNvme
                ? (info.NvmeHealth != null ? "NVMe (health log)" : "NVMe (health log unavailable)")
                : "ATA/SATA (ATAPI SMART)";
            SmartAttributesList.ItemsSource = info.Attributes;
            UpdateHealthVisualization(info);
        }
        else
        {
            SmartHealthText.Text = "—";
            DriveKindText.Text = "";
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
            TempCardText.Text = "—";
            SpareCardText.Text = "—";
            WearCardText.Text = "—";
            return;
        }

        TempCardText.Text = info.TemperatureDisplay;
        SpareCardText.Text = info.AvailableSpareDisplay;
        WearCardText.Text = info.PercentageUsedDisplay;

        int score = CalculateHealthScore(info);
        HealthScoreBar.Value = score;
        HealthScoreText.Text = $"{score}/100";

        if (score >= 80)
        {
            HealthSummaryText.Text = info.IsNvme
                ? "Healthy. NVMe health log shows no critical warnings."
                : "Healthy. No critical S.M.A.R.T. issues detected.";
            HealthCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
        }
        else if (score >= 50)
        {
            HealthSummaryText.Text = "Watch. Some health indicators need monitoring.";
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
        if (info.PredictFailure == true || info.NvmeHealth?.HasCriticalWarning == true)
            return 15;

        int score = 100;
        var attrs = info.Attributes;

        score -= attrs.Where(a => a.IsCritical && a.Raw > 0).Sum(_ => 12);
        score -= attrs.Where(a => a.Id is 0x05 or 0xC5 or 0xC6 && a.Raw > 0).Sum(_ => 10);

        if (info.NvmeHealth != null)
        {
            if (info.NvmeHealth.AvailableSparePercent < info.NvmeHealth.AvailableSpareThreshold)
                score -= 25;
            if (info.NvmeHealth.PercentageUsed >= 90)
                score -= 20;
            else if (info.NvmeHealth.PercentageUsed >= 80)
                score -= 10;
            if (info.NvmeHealth.MediaErrors > 0)
                score -= 20;
        }

        if (attrs.Count == 0 && info.NvmeHealth == null && info.PredictFailure is null)
            score = 65;

        return Math.Clamp(score, 0, 100);
    }
}
