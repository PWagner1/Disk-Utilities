using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DriveWiper.Models;
using DriveWiper.Services;

namespace DriveWiper;

/// <summary>
/// Main window for the Drive Wiper app: lists formattable drives, format options, and runs format via <see cref="DriveService"/>.
/// The system drive (C:) is never shown and cannot be formatted.
/// </summary>
public sealed partial class MainWindow : Window
{
    private IReadOnlyList<DriveItem> _drives = Array.Empty<DriveItem>();

    public MainWindow()
    {
        InitializeComponent();
        LoadFileSystems();
        LoadSecureWipePresets();
        RefreshDrives();
        SecureDeletionCheck.Checked += (_, _) => UpdateSecureWipePresetState();
        SecureDeletionCheck.Unchecked += (_, _) => UpdateSecureWipePresetState();
        UpdateSecureWipePresetState();
    }

    /// <summary>Populates the secure wipe preset combo (1 pass, DoD 3-pass, Military grade 7-pass).</summary>
    private void LoadSecureWipePresets()
    {
        SecureWipePresetCombo.Items.Clear();
        foreach (var (displayName, _) in DriveService.SecureWipePresets)
            SecureWipePresetCombo.Items.Add(displayName);
        SecureWipePresetCombo.SelectedIndex = 0;
    }

    private void UpdateSecureWipePresetState()
    {
        if (SecureWipePresetPanel != null)
            SecureWipePresetPanel.IsEnabled = SecureDeletionCheck?.IsChecked == true;
        if (SecureWipePresetPanel != null)
            SecureWipePresetPanel.Opacity = SecureDeletionCheck?.IsChecked == true ? 1.0 : 0.7;
    }

    /// <summary>Populates the file system combo with all supported file systems.</summary>
    private void LoadFileSystems()
    {
        FileSystemCombo.Items.Clear();
        foreach (string fs in DriveService.SupportedFileSystems)
            FileSystemCombo.Items.Add(fs);
        FileSystemCombo.SelectedIndex = 0;
    }

    /// <summary>Refreshes the list of formattable drives (excluding C:) and updates the drive combo.</summary>
    private void RefreshDrives()
    {
        _drives = DriveService.GetFormattableDrives();
        DriveCombo.Items.Clear();
        foreach (DriveItem d in _drives)
            DriveCombo.Items.Add(d);
        if (_drives.Count > 0)
            DriveCombo.SelectedIndex = 0;
        else
            DriveCombo.PlaceholderText = "No other drives found (C: is protected).";
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Optional: enable/disable format based on selection
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        RefreshDrives();
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (DriveCombo.SelectedItem is not DriveItem selected)
        {
            ShowStatus("Please select a drive.", isError: true);
            return;
        }

        string driveLetter = selected.DriveLetter;
        Debug.Assert(!driveLetter.Equals(DriveService.ProtectedDriveLetter, StringComparison.OrdinalIgnoreCase),
            "C: must not be offered in the list.");

        string fileSystem = FileSystemCombo.SelectedItem?.ToString() ?? "NTFS";
        string label = VolumeLabelBox?.Text?.Trim() ?? string.Empty;
        uint clusterSize = 0;
        if (!string.IsNullOrWhiteSpace(ClusterSizeBox?.Text) && uint.TryParse(ClusterSizeBox.Text.Trim(), out uint parsed))
            clusterSize = parsed;

        bool quick = QuickFormatCheck?.IsChecked == true;
        bool compression = CompressionCheck?.IsChecked == true;
        bool secureDeletion = SecureDeletionCheck?.IsChecked == true;
        int wipePasses = 1;
        if (secureDeletion && SecureWipePresetCombo?.SelectedIndex >= 0 && SecureWipePresetCombo.SelectedIndex < DriveService.SecureWipePresets.Length)
            wipePasses = DriveService.SecureWipePresets[SecureWipePresetCombo.SelectedIndex].Passes;

        SetBusy(true);
        HideStatus();

        try
        {
            if (secureDeletion && wipePasses > 0)
            {
                ShowProgressBar(determinate: true);
                var progress = new Progress<(int pass, int totalPasses, double percent)>(p =>
                {
                    if (ProgressBar != null) ProgressBar.Value = p.percent;
                    if (ProgressLabel != null)
                    {
                        ProgressLabel.Text = $"Secure wipe: pass {p.pass}/{p.totalPasses} ({p.percent:F0}%)";
                        ProgressLabel.Visibility = Visibility.Visible;
                    }
                });
                bool wiped = await DriveService.SecureWipeAsync(driveLetter, wipePasses, progress);
                ShowProgressBar(determinate: false);
                if (ProgressLabel != null) ProgressLabel.Visibility = Visibility.Collapsed;
                if (!wiped)
                {
                    ShowStatus("Secure wipe failed or was skipped (e.g. protected drive).", isError: true);
                    return;
                }
            }

            uint result = await DriveService.FormatAsync(driveLetter, fileSystem, quick, clusterSize, label, compression);
            string message = DriveService.GetFormatResultMessage(result);
            if (result == 0)
            {
                ShowStatus(secureDeletion ? "Secure wipe and format completed." : message, isError: false);
                RefreshDrives();
            }
            else
                ShowStatus(message, isError: true);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Operation canceled.", isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
            ShowProgressBar(determinate: false);
            if (ProgressLabel != null) ProgressLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowProgressBar(bool determinate)
    {
        if (ProgressBar == null) return;
        ProgressBar.IsIndeterminate = !determinate;
        ProgressBar.Visibility = Visibility.Visible;
        if (!determinate) ProgressBar.Value = 0;
    }

    private void SetBusy(bool busy)
    {
        if (ProgressBar != null) ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (FormatButton != null) FormatButton.IsEnabled = !busy;
        if (RefreshButton != null) RefreshButton.IsEnabled = !busy;
        if (DriveCombo != null) DriveCombo.IsEnabled = !busy;
        if (SecureDeletionCheck != null) SecureDeletionCheck.IsEnabled = !busy;
        if (SecureWipePresetCombo != null) SecureWipePresetCombo.IsEnabled = !busy;
    }

    private void ShowStatus(string text, bool isError)
    {
        if (StatusText == null) return;
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        // Keep default critical brush for errors; could switch to success brush when !isError
    }

    private void HideStatus()
    {
        if (StatusText != null) StatusText.Visibility = Visibility.Collapsed;
    }
}
