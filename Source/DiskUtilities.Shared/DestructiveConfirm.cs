namespace DiskUtilities.Shared;

/// <summary>
/// Shared confirmation dialogs for irreversible or high-risk operations.
/// Cancel is always the default; data-destroying ops require an acknowledgement checkbox.
/// </summary>
public static class DestructiveConfirm
{
    /// <summary>Standard checkbox label for irreversible data loss.</summary>
    public const string DefaultAcknowledgeLabel =
        "I understand this cannot be undone and I want to proceed";

    /// <summary>
    /// Simple Yes/Cancel confirmation. Cancel is the default button (safer for accidental Enter).
    /// Use for disruptive but recoverable-ish actions (e.g. close a file handle).
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Confirmation that requires checking an acknowledgement box before Primary succeeds.
    /// Use for format, wipe, partition delete, process kill, and permanent file delete.
    /// </summary>
    public static async Task<bool> ConfirmWithAcknowledgementAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string primaryButtonText,
        string? acknowledgeLabel = null)
    {
        // Suite seed may prefer a simpler confirm; never skip confirmation entirely.
        if (!SuiteSettings.GetRequireDestructiveAcknowledgement())
        {
            return await ConfirmAsync(xamlRoot, title, message, primaryButtonText).ConfigureAwait(true);
        }

        var confirmCheckBox = new CheckBox
        {
            Content = acknowledgeLabel ?? DefaultAcknowledgeLabel
        };
        var errorText = new TextBlock
        {
            Text = "Please check the box above to confirm.",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 0, 0)),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(confirmCheckBox);
        panel.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        dialog.Closing += (_, e) =>
        {
            if (e.Result == ContentDialogResult.Primary && confirmCheckBox.IsChecked != true)
            {
                e.Cancel = true;
                errorText.Visibility = Visibility.Visible;
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Builds the DiskEraser format / free-space-overwrite confirmation body.</summary>
    public static string FormatVolumeMessage(
        string driveLetter,
        string fileSystem,
        bool freeSpaceOverwrite,
        int wipePasses)
    {
        string body = $"You are about to format drive {driveLetter} as {fileSystem}.\n\n"
            + "All data on this drive will be permanently erased. This cannot be undone.";
        if (freeSpaceOverwrite)
        {
            body += $"\n\nFree-space overwrite ({wipePasses} pass(es)) will run first. "
                + "This overwrites unused space only — it is not hardware Secure Erase / NVMe Sanitize.";
        }

        return body;
    }

    /// <summary>Builds a partition-delete confirmation body.</summary>
    public static string DeletePartitionMessage(string partitionLabel)
        => $"Delete {partitionLabel}?\n\nAll data on this partition will be permanently lost. This cannot be undone.";

    /// <summary>Builds a permanent file/folder delete confirmation body.</summary>
    public static string DeleteItemsMessage(int count)
        => $"Delete {count} item{(count == 1 ? "" : "s")}?\n\nThis permanently removes data from the volume and cannot be undone.";

    /// <summary>Builds an end-process confirmation body.</summary>
    public static string EndProcessMessage(string processName, int processId)
        => $"End process '{processName}' (PID {processId})?\n\n"
            + "Unsaved work in that process may be lost, and other open files it holds may become unstable.";

    /// <summary>Builds a close-handle confirmation body.</summary>
    public static string CloseHandleMessage(string processName, int processId)
        => $"Close the handle in '{processName}' (PID {processId})?\n\n"
            + "The process will keep running; closing the handle may allow the file to be deleted or moved. "
            + "The process could crash if it is actively using the file.";
}
