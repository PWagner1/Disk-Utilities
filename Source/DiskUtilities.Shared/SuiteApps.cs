using System.Diagnostics;

namespace DiskUtilities.Shared;

/// <summary>Known suite applications that can be launched from the launcher or cross-links.</summary>
public enum SuiteAppId
{
    DiskEraser,
    DiskHealth,
    DiskSeer,
    DriveStat,
    HandleFinder,
    SuiteLauncher
}

/// <summary>Catalog entry for a suite tool.</summary>
public sealed record SuiteAppInfo(
    SuiteAppId Id,
    string DisplayName,
    string ExeFileName,
    string ShortDescription,
    string ElevationHint,
    bool RequiresAdminManifest,
    IReadOnlyList<SuiteAppId> RelatedApps);

/// <summary>
/// Resolves and starts suite executables from each app's output folder (or beside the current process).
/// </summary>
public static class SuiteApps
{
    /// <summary>All user-facing tools (excludes the launcher itself from the main list when filtered).</summary>
    public static IReadOnlyList<SuiteAppInfo> All { get; } = (SuiteAppInfo[])
    [
        new(SuiteAppId.DiskEraser, "DiskEraser", "DiskEraser.exe",
            "Format volumes and manage partitions.",
            "Administrator recommended for format and diskpart.",
            RequiresAdminManifest: false,
            RelatedApps: (SuiteAppId[])[SuiteAppId.HandleFinder, SuiteAppId.DriveStat]),
        new(SuiteAppId.DiskHealth, "DiskHealth", "DiskHealth.exe",
            "Signature-based file recovery and S.M.A.R.T. health.",
            "Administrator recommended for raw disk scans.",
            RequiresAdminManifest: false,
            RelatedApps: (SuiteAppId[])[SuiteAppId.DriveStat, SuiteAppId.DiskEraser]),
        new(SuiteAppId.DiskSeer, "DiskSeer", "DiskSeer.exe",
            "Browse and edit Apple and Linux file systems from Windows.",
            "Requires Administrator (raw disk access).",
            RequiresAdminManifest: true,
            RelatedApps: (SuiteAppId[])[SuiteAppId.DiskHealth, SuiteAppId.DiskEraser]),
        new(SuiteAppId.DriveStat, "DriveStat", "DriveStat.exe",
            "Disk usage: tree, treemap, and extension stats.",
            "Administrator optional (helps scan protected folders).",
            RequiresAdminManifest: false,
            RelatedApps: (SuiteAppId[])[SuiteAppId.DiskHealth, SuiteAppId.HandleFinder]),
        new(SuiteAppId.HandleFinder, "HandleFinder", "HandleFinder.exe",
            "Find which processes lock files, folders, or drives.",
            "Requires Administrator (handle enumeration).",
            RequiresAdminManifest: true,
            RelatedApps: (SuiteAppId[])[SuiteAppId.DriveStat, SuiteAppId.DiskEraser]),
        new(SuiteAppId.SuiteLauncher, "SuiteLauncher", "SuiteLauncher.exe",
            "Launch Disk Utilities tools from one place.",
            "Runs as standard user; target apps prompt for elevation as needed.",
            RequiresAdminManifest: false,
            RelatedApps: Array.Empty<SuiteAppId>())
    ];

    /// <summary>Tools shown in the suite launcher (excludes the launcher itself).</summary>
    public static IReadOnlyList<SuiteAppInfo> LauncherTools { get; } =
        All.Where(a => a.Id != SuiteAppId.SuiteLauncher).ToArray();

    /// <summary>Looks up a catalog entry by id.</summary>
    public static SuiteAppInfo? Get(SuiteAppId id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Resolves the full path to an app exe beside this process, in a sibling app folder, or under the usual repo/bin layout.
    /// </summary>
    public static string? ResolveExecutablePath(SuiteAppId id) =>
        ResolveExecutablePath(id, CurrentProcessDirectories());

    /// <summary>
    /// Resolves an app exe relative to <paramref name="processDirectory"/> without inspecting the current process.
    /// Checks the same folder, <c>{dir}\{App}\{App}.exe</c>, <c>{dir}\..\{App}\{App}.exe</c>, then a <c>bin\</c> tree above that directory.
    /// </summary>
    public static string? ResolveExecutablePath(SuiteAppId id, string? processDirectory)
    {
        if (string.IsNullOrEmpty(processDirectory))
        {
            return null;
        }

        return ResolveExecutablePath(id, (string[])[processDirectory]);
    }

    private static string? ResolveExecutablePath(SuiteAppId id, IEnumerable<string> processDirectories)
    {
        SuiteAppInfo? info = Get(id);
        if (info == null)
        {
            return null;
        }

        foreach (string path in EnumerateCandidatePaths(info.ExeFileName, processDirectories))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Candidate exe paths for <paramref name="exeFileName"/>, in preference order. Does not check that the file exists.</summary>
    internal static IEnumerable<string> EnumerateCandidatePaths(string exeFileName, IEnumerable<string> processDirectories)
    {
        string appFolder = Path.GetFileNameWithoutExtension(exeFileName);
        var dirs = new List<string>();
        foreach (string dir in processDirectories)
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            if (!dirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
            {
                dirs.Add(dir);
            }
        }

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in dirs)
        {
            foreach (string candidate in new[]
            {
                Path.Combine(dir, exeFileName),
                Path.Combine(dir, appFolder, exeFileName),
                Path.GetFullPath(Path.Combine(dir, "..", appFolder, exeFileName))
            })
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (string start in dirs)
        {
            string? dir = start;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                string binRoot = Path.Combine(dir, "bin");
                if (Directory.Exists(binRoot))
                {
                    List<string> found;
                    try
                    {
                        found = Directory.EnumerateFiles(binRoot, exeFileName, SearchOption.AllDirectories).ToList();
                    }
                    catch
                    {
                        found = [];
                    }

                    foreach (string path in found.OrderByDescending(path =>
                        string.Equals(
                            Path.GetFileName(Path.GetDirectoryName(path)),
                            appFolder,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        if (yielded.Add(path))
                        {
                            yield return path;
                        }
                    }
                }

                dir = Path.GetDirectoryName(dir);
            }
        }
    }

    private static IEnumerable<string> CurrentProcessDirectories()
    {
        string? processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(processDir))
        {
            yield return processDir;
        }

        string? moduleDir = null;
        try
        {
            moduleDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        }
        catch
        {
            // ignore
        }

        if (!string.IsNullOrEmpty(moduleDir))
        {
            yield return moduleDir;
        }
    }

    /// <summary>Starts a suite app. Returns false if the exe is missing or start fails.</summary>
    public static bool TryLaunch(SuiteAppId id, IEnumerable<string>? arguments = null, bool elevated = false)
    {
        string? path = ResolveExecutablePath(id);
        if (string.IsNullOrEmpty(path))
        {
            AppLog.Warning($"Could not find executable for {id}.", source: nameof(SuiteApps));
            return false;
        }

        string? workDir = Path.GetDirectoryName(path);
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            WorkingDirectory = string.IsNullOrEmpty(workDir) ? string.Empty : workDir
        };
        if (elevated)
        {
            startInfo.Verb = "runas";
        }

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
        catch (Exception ex)
        {
            AppLog.Warning($"Failed to launch {id}.", ex, nameof(SuiteApps));
            return false;
        }
    }

    /// <summary>Opens HandleFinder GUI with an optional path pre-filled (<c>--gui</c>).</summary>
    public static bool TryLaunchHandleFinderGui(string? path = null)
    {
        var args = new List<string> { "--gui" };
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add(path.Trim());
        }

        return TryLaunch(SuiteAppId.HandleFinder, args);
    }
}
