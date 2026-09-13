namespace DiskUtilities.Shared;

/// <summary>
/// WinUI helpers for elevation banners and "Restart as administrator" prompts.
/// </summary>
public static class ElevationUi
{
    /// <summary>
    /// Shows <paramref name="banner"/> when the process is not elevated; otherwise collapses it.
    /// Wires <paramref name="restartButton"/> to a UAC relaunch prompt when provided.
    /// </summary>
    public static void ConfigureBanner(
        Border banner,
        TextBlock messageText,
        string unelevatedMessage,
        Button? restartButton,
        XamlRoot? xamlRoot,
        string? dialogReason = null)
    {
        if (ElevationHelper.IsElevated)
        {
            banner.Visibility = Visibility.Collapsed;
            return;
        }

        banner.Visibility = Visibility.Visible;
        messageText.Text = unelevatedMessage;

        if (restartButton == null)
        {
            return;
        }

        restartButton.Click += async (_, _) =>
        {
            XamlRoot? root = xamlRoot ?? banner.XamlRoot;
            if (root == null)
            {
                return;
            }

            await PromptAndRelaunchAsync(
                root,
                dialogReason ?? unelevatedMessage).ConfigureAwait(true);
        };
    }

    /// <summary>
    /// Asks the user to restart elevated; on confirm, relaunches via UAC and exits the current app.
    /// </summary>
    /// <returns>True if relaunch was started (current process should exit or is exiting).</returns>
    public static async Task<bool> PromptAndRelaunchAsync(XamlRoot xamlRoot, string reason)
    {
        if (ElevationHelper.IsElevated)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = "Administrator required",
            Content = new TextBlock
            {
                Text = reason + "\n\nRestart this app as Administrator? Windows will show a UAC prompt.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Restart as administrator",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        // Preserve current CLI args when relaunching (skip argv[0]).
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!ElevationHelper.TryRelaunchElevated(args))
        {
            var failed = new ContentDialog
            {
                Title = "Elevation cancelled",
                Content = "The app was not restarted as Administrator. You can try again or continue with limited access.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            await failed.ShowAsync();
            return false;
        }

        try
        {
            Application.Current?.Exit();
        }
        catch
        {
            Environment.Exit(0);
        }

        return true;
    }

    /// <summary>
    /// Convenience: if not elevated, prompt to relaunch. Returns true when the caller should abort the current operation
    /// (either because relaunch started, or the user declined and the op cannot continue without admin).
    /// When <paramref name="requireElevation"/> is true and the user declines, returns true (abort).
    /// When false and user declines, returns false (caller may continue).
    /// </summary>
    public static async Task<bool> EnsureElevatedOrAbortAsync(
        XamlRoot xamlRoot,
        string reason,
        bool requireElevation = true)
    {
        if (ElevationHelper.IsElevated)
        {
            return false;
        }

        bool relaunched = await PromptAndRelaunchAsync(xamlRoot, reason).ConfigureAwait(true);
        if (relaunched)
        {
            return true;
        }

        return requireElevation;
    }

    /// <summary>Applies a light warning background suitable for unelevated banners.</summary>
    public static void StyleWarningBanner(Border banner)
    {
        banner.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 180, 0));
        banner.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 200, 120, 0));
        banner.BorderThickness = new Thickness(1);
        banner.CornerRadius = new CornerRadius(4);
        banner.Padding = new Thickness(12, 8, 12, 8);
    }
}
