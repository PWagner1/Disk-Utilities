using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HandleFinder;

/// <summary>
/// Command-line interface for HandleFinder, inspired by File Locksmith CLI.
/// </summary>
public static class CliRunner
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    public static async Task<int> RunAsync(string[] args)
    {
        var paths = new List<string>();
        var kill = false;
        var json = false;
        var wait = false;
        var help = false;

        foreach (var arg in args)
        {
            if (arg is "--help" or "-h" or "-?")
            {
                help = true;
            }
            else if (arg is "--kill" or "-k")
            {
                kill = true;
            }
            else if (arg is "--json" or "-j")
            {
                json = true;
            }
            else if (arg is "--wait" or "-w")
            {
                wait = true;
            }
            else if (!arg.StartsWith('-') && arg.Length > 0)
            {
                paths.Add(arg.Trim('"'));
            }
        }

        if (help || paths.Count == 0)
        {
            AllocConsole();
            try
            {
                PrintHelp();
                return 0;
            }
            finally
            {
                FreeConsole();
            }
        }

        // Normalize drive-only paths
        for (var i = 0; i < paths.Count; i++)
        {
            if (paths[i] is [_, ':'] && char.IsLetter(paths[i][0]))
            {
                paths[i] = paths[i] + "\\";
            }
        }

        var invalidPaths = paths.Where(p =>
        {
            try
            {
                if (Path.Exists(p)) return false;
                if (p.Length >= 2 && p[1] == ':' && p.EndsWith("\\") && Directory.Exists(p)) return false;
                return true;
            }
            catch { return true; }
        }).ToList();
        if (invalidPaths.Count > 0)
        {
            AllocConsole();
            try
            {
                Console.Error.WriteLine($"Path(s) not found: {string.Join(", ", invalidPaths)}");
                return 1;
            }
            finally
            {
                FreeConsole();
            }
        }

        AllocConsole();
        try
        {
            var service = new HandleService();

            if (wait)
            {
                return await WaitForReleaseAsync(service, paths, json);
            }

            var allHandles = new List<HandleInfo>();
            foreach (var path in paths)
            {
                var handles = await service.FindHandlesForPathAsync(path, false);
                allHandles.AddRange(handles);
            }

            var distinctByProcess = allHandles.GroupBy(h => h.ProcessId).Select(g => g.First()).ToList();

            if (json)
            {
                var output = new
                {
                    pathCount = paths.Count,
                    handleCount = allHandles.Count,
                    processCount = distinctByProcess.Count,
                    processes = distinctByProcess.Select(h => new
                    {
                        h.ProcessId,
                        h.ProcessName,
                        h.HandleValue,
                        h.Path,
                        h.GrantedAccess
                    }).ToList()
                };
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                foreach (var h in distinctByProcess)
                {
                    Console.WriteLine($"{h.ProcessName} (PID {h.ProcessId}): {h.Path}");
                }
                if (distinctByProcess.Count == 0)
                {
                    Console.WriteLine("No processes found locking the specified path(s).");
                }
            }

            if (kill && distinctByProcess.Count > 0)
            {
                foreach (var h in distinctByProcess)
                {
                    if (HandleService.TryTerminateProcess(h.ProcessId))
                    {
                        Console.WriteLine($"Terminated {h.ProcessName} (PID {h.ProcessId})");
                    }
                    else
                    {
                        Console.Error.WriteLine($"Failed to terminate {h.ProcessName} (PID {h.ProcessId})");
                    }
                }
            }

            return 0;
        }
        finally
        {
            FreeConsole();
        }
    }

    private static async Task<int> WaitForReleaseAsync(HandleService service, List<string> paths, bool json)
    {
        while (true)
        {
            var allHandles = new List<HandleInfo>();
            foreach (var path in paths)
            {
                var handles = await service.FindHandlesForPathAsync(path, false);
                allHandles.AddRange(handles);
            }

            if (allHandles.Count == 0)
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { released = true }));
                }
                else
                {
                    Console.WriteLine("Files are now released.");
                }
                return 0;
            }

            if (!json)
            {
                Console.WriteLine($"{allHandles.Count} handle(s) still locking. Waiting...");
            }

            await Task.Delay(1000);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            HandleFinder - Find processes locking files or directories

            Usage:
              HandleFinder.exe <path> [path2 ...] [options]
              HandleFinder.exe --help

            Arguments:
              <path>    One or more file or directory paths to check.
                        Multiple paths can be specified, separated by spaces.

            Options:
              --kill, -k    Terminate all processes locking the specified files.
              --json, -j    Output results in JSON format (for automation).
              --wait, -w    Block until the specified files are released.
                            Useful in build scripts.
              --help, -h    Show this help message.

            Examples:
              HandleFinder.exe "C:\Users\Docs\report.docx"
              HandleFinder.exe --json "C:\File1.txt" "C:\Folder\File2.dll"
              HandleFinder.exe --wait "C:\bin\output.exe"
              HandleFinder.exe --kill "C:\LockedFile.dat"

            Note: Run as Administrator to access all processes.
            """);
    }
}
