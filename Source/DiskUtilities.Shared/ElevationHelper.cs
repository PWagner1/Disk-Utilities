namespace DiskUtilities.Shared;

/// <summary>
/// Detects process elevation and relaunches the current executable with Administrator rights (UAC).
/// </summary>
public static class ElevationHelper
{
    /// <summary>True if the current process token is elevated (running as Administrator).</summary>
    public static bool IsElevated { get; } = DetectElevated();

    private static bool DetectElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a new elevated instance of this executable via UAC (<c>runas</c>).
    /// Returns false if the path cannot be resolved or the user cancels the UAC prompt.
    /// </summary>
    /// <param name="arguments">Optional arguments to pass to the new process (e.g. CLI args).</param>
    /// <returns>True if the elevated process was started.</returns>
    public static bool TryRelaunchElevated(IEnumerable<string>? arguments = null)
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            try
            {
                path = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                path = null;
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas"
        };

        if (arguments != null)
        {
            foreach (string arg in arguments)
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }
        }

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            // User cancelled UAC or elevation is not allowed.
            return false;
        }
        catch
        {
            return false;
        }
    }
}
