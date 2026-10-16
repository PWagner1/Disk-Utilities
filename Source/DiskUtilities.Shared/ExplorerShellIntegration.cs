using Microsoft.Win32;

namespace DiskUtilities.Shared;

/// <summary>
/// Registers / removes per-user Explorer context menu verbs for HandleFinder and DriveStat (HKCU).
/// No installer required; SuiteLauncher can install and uninstall.
/// </summary>
public static class ExplorerShellIntegration
{
    public const string HandleFinderVerb = "DiskUtilities.WhatHasThisLocked";
    public const string DriveStatVerb = "DiskUtilities.AnalyzeDiskUsage";

    /// <summary>True if both verbs appear under HKCU Directory\shell.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{HandleFinderVerb}");
            using var key2 = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{DriveStatVerb}");
            return key != null && key2 != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers Explorer verbs pointing at the given executables.
    /// HandleFinder: file, folder, drive. DriveStat: folder, drive.
    /// </summary>
    public static bool TryInstall(string handleFinderExe, string driveStatExe, out string message)
    {
        if (!File.Exists(handleFinderExe) || !File.Exists(driveStatExe))
        {
            message = "HandleFinder.exe and DriveStat.exe must exist (build the suite so each app has its own output folder).";
            return false;
        }

        try
        {
            string hf = Quote(handleFinderExe);
            string ds = Quote(driveStatExe);

            // Files
            WriteVerb(@"Software\Classes\*\shell\", HandleFinderVerb, "What has this locked? (HandleFinder)",
                $"{hf} --gui \"%1\"", handleFinderExe);

            // Folders
            WriteVerb(@"Software\Classes\Directory\shell\", HandleFinderVerb, "What has this locked? (HandleFinder)",
                $"{hf} --gui \"%1\"", handleFinderExe);
            WriteVerb(@"Software\Classes\Directory\shell\", DriveStatVerb, "Analyze disk usage (DriveStat)",
                $"{ds} --path \"%1\"", driveStatExe);

            // Folder background (current folder)
            WriteVerb(@"Software\Classes\Directory\Background\shell\", DriveStatVerb, "Analyze disk usage (DriveStat)",
                $"{ds} --path \"%V\"", driveStatExe);

            // Drives
            WriteVerb(@"Software\Classes\Drive\shell\", HandleFinderVerb, "What has this locked? (HandleFinder)",
                $"{hf} --gui \"%1\"", handleFinderExe);
            WriteVerb(@"Software\Classes\Drive\shell\", DriveStatVerb, "Analyze disk usage (DriveStat)",
                $"{ds} --path \"%1\"", driveStatExe);

            message = "Explorer context menus installed for this user (HKCU). Restart Explorer windows if items do not appear yet.";
            AppLog.Info(message, source: nameof(ExplorerShellIntegration));
            return true;
        }
        catch (Exception ex)
        {
            message = "Failed to write HKCU shell verbs: " + ex.Message;
            AppLog.Error(message, ex, nameof(ExplorerShellIntegration));
            return false;
        }
    }

    /// <summary>Removes the suite Explorer verbs from HKCU.</summary>
    public static bool TryUninstall(out string message)
    {
        try
        {
            DeleteVerb(@"Software\Classes\*\shell\", HandleFinderVerb);
            DeleteVerb(@"Software\Classes\Directory\shell\", HandleFinderVerb);
            DeleteVerb(@"Software\Classes\Directory\shell\", DriveStatVerb);
            DeleteVerb(@"Software\Classes\Directory\Background\shell\", DriveStatVerb);
            DeleteVerb(@"Software\Classes\Drive\shell\", HandleFinderVerb);
            DeleteVerb(@"Software\Classes\Drive\shell\", DriveStatVerb);
            message = "Explorer context menus removed for this user.";
            AppLog.Info(message, source: nameof(ExplorerShellIntegration));
            return true;
        }
        catch (Exception ex)
        {
            message = "Failed to remove HKCU shell verbs: " + ex.Message;
            AppLog.Error(message, ex, nameof(ExplorerShellIntegration));
            return false;
        }
    }

    /// <summary>Registry relative paths used for install (tests).</summary>
    public static IReadOnlyList<string> VerbRootPaths { get; } = (string[])
    [
        @"Software\Classes\*\shell\",
        @"Software\Classes\Directory\shell\",
        @"Software\Classes\Directory\Background\shell\",
        @"Software\Classes\Drive\shell\"
    ];

    private static void WriteVerb(string root, string verb, string label, string command, string iconPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(root + verb);
        key.SetValue("", label);
        key.SetValue("Icon", iconPath);
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", command);
    }

    private static void DeleteVerb(string root, string verb)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(root + verb, throwOnMissingSubKey: false);
        }
        catch
        {
            // ignore missing
        }
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "") + "\"";
}
