using System.Text;

namespace HandleFinder.Services;

/// <summary>
/// Enumerates handles held by processes that reference a given file or directory path.
/// Uses NtQuerySystemInformation and NtQueryObject to find locking processes.
/// </summary>
public sealed class HandleService
{
    private const int SystemExtendedHandleInformation = 64;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const uint STATUS_SUCCESS = 0;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;
    private const uint FILE_NAME_NORMALIZED = 0x1;
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        nint systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtDuplicateObject(
        nint sourceProcessHandle,
        nint sourceHandle,
        nint targetProcessHandle,
        out nint targetHandle,
        uint desiredAccess,
        uint attributes,
        uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetFinalPathNameByHandleW(
        nint handle,
        [Out] StringBuilder path,
        int pathLen,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SystemHandleTableEntryInfo
    {
        public nint Object;
        public nuint UniqueProcessId;
        public nuint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SystemHandleInformationEx
    {
        public nuint HandleCount;
        public nuint Reserved;
        public SystemHandleTableEntryInfo Handles;
    }

    /// <summary>
    /// Object type indices for file-like handles. 28 = File on Windows 10/11; may vary on older versions.
    /// </summary>
    private static readonly ushort[] FileObjectTypeIndices = [28, 27, 29, 30, 31];

    /// <summary>
    /// Finds all processes that have handles open to the given file or directory path.
    /// </summary>
    /// <param name="targetPath">Full path to a file or directory (e.g. C:\path\to\file.txt).</param>
    /// <param name="comprehensiveSearch">If true, tries all handle types (slower but works across Windows versions).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>List of handle information for processes locking the path.</returns>
    public async Task<IReadOnlyList<HandleInfo>> FindHandlesForPathAsync(
        string targetPath,
        bool comprehensiveSearch = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return [];

        var normalizedTarget = NormalizePath(targetPath.Trim());
        var results = new List<HandleInfo>();

        var bufferSize = 0x10000; // 64 KB initial
        nint buffer = nint.Zero;

        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(bufferSize);
                var status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out var returnLength);

                if (status == 0)
                    break;

                Marshal.FreeHGlobal(buffer);
                buffer = nint.Zero;

                if (status == unchecked((int)STATUS_INFO_LENGTH_MISMATCH))
                {
                    bufferSize = Math.Max(bufferSize, returnLength) + 0x1000;
                    continue;
                }

                return [];
            }

            var handleCount = Marshal.ReadInt64(buffer);
            var baseOffset = IntPtr.Size == 8 ? 16 : 12; // Header size varies by architecture
            var entrySize = Marshal.SizeOf<SystemHandleTableEntryInfo>();

            for (var i = 0; i < handleCount && i < 100000; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryOffset = buffer + baseOffset + (i * entrySize);
                var entry = Marshal.PtrToStructure<SystemHandleTableEntryInfo>(entryOffset);

                // Filter by object type: 28 = File on Windows 10/11; comprehensive mode tries all
                if (!comprehensiveSearch && !FileObjectTypeIndices.Contains(entry.ObjectTypeIndex))
                    continue;

                var processId = (int)entry.UniqueProcessId;
                var handleValue = entry.HandleValue;

                var path = await Task.Run(() => TryGetPathForHandle(processId, handleValue), cancellationToken);
                if (string.IsNullOrEmpty(path))
                    continue;

                var normalizedPath = NormalizePath(path);
                if (!PathMatches(normalizedTarget, normalizedPath))
                    continue;

                var processName = GetProcessName(processId);
                results.Add(new HandleInfo
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    HandleValue = handleValue,
                    ObjectTypeIndex = entry.ObjectTypeIndex,
                    Path = path,
                    GrantedAccess = entry.GrantedAccess
                });
            }
        }
        finally
        {
            if (buffer != nint.Zero)
                Marshal.FreeHGlobal(buffer);
        }

        // Fallback: if no results with type filter, retry with comprehensive search (different Windows versions)
        if (results.Count == 0 && !comprehensiveSearch)
        {
            return await FindHandlesForPathAsync(targetPath, comprehensiveSearch: true, cancellationToken);
        }

        return results;
    }

    private static string? TryGetPathForHandle(int processId, nuint handleValue)
    {
        nint processHandle = nint.Zero;
        nint duplicateHandle = nint.Zero;

        try
        {
            processHandle = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (processHandle == nint.Zero)
                return null;

            var status = NtDuplicateObject(
                processHandle,
                (nint)handleValue,
                GetCurrentProcess(),
                out duplicateHandle,
                0,
                0,
                DUPLICATE_SAME_ACCESS);

            if (status != 0 || duplicateHandle == nint.Zero)
                return null;

            var sb = new StringBuilder(512);
            var len = GetFinalPathNameByHandleW(duplicateHandle, sb, sb.Capacity, FILE_NAME_NORMALIZED);
            if (len <= 0)
                return null;

            if (len > sb.Capacity)
            {
                sb.Capacity = len;
                len = GetFinalPathNameByHandleW(duplicateHandle, sb, len, FILE_NAME_NORMALIZED);
            }

            if (len <= 0)
                return null;

            var path = sb.ToString();
            // Strip \??\ or \\?\ prefix
            if (path.StartsWith(@"\??\", StringComparison.Ordinal))
                path = path[4..];
            else if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                path = @"\\" + path[7..];
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path[4..];

            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (duplicateHandle != nint.Zero)
                CloseHandle(duplicateHandle);
            if (processHandle != nint.Zero)
                CloseHandle(processHandle);
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.ToUpperInvariant();
        }
        catch
        {
            return path.ToUpperInvariant();
        }
    }

    private static bool PathMatches(string target, string handlePath)
    {
        // Exact match
        if (target.Equals(handlePath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Target is a directory - match if handle path is inside it
        if (handlePath.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        if (handlePath.StartsWith(target + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {processId}";
        }
    }

    /// <summary>
    /// Attempts to close a handle in another process without terminating it.
    /// Uses NtDuplicateObject with DUPLICATE_CLOSE_SOURCE. May not work on all Windows versions
    /// or for protected processes.
    /// </summary>
    public static bool TryCloseHandle(int processId, nuint handleValue)
    {
        nint processHandle = nint.Zero;
        try
        {
            processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, processId);
            if (processHandle == nint.Zero)
                return false;

            var status = NtDuplicateObject(
                processHandle,
                (nint)handleValue,
                nint.Zero,
                out var dummyHandle,
                0,
                0,
                DUPLICATE_CLOSE_SOURCE);

            return status == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processHandle != nint.Zero)
                CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// Attempts to terminate a process by ID. Use with caution.
    /// </summary>
    public static bool TryTerminateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
