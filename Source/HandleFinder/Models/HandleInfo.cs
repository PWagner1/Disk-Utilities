namespace HandleFinder.Models;

/// <summary>
/// Represents a handle held by a process that references a file or directory.
/// </summary>
public sealed class HandleInfo
{
    /// <summary>Process ID that owns the handle.</summary>
    public int ProcessId { get; init; }

    /// <summary>Process name (e.g. explorer.exe).</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Handle value in the process.</summary>
    public nuint HandleValue { get; init; }

    /// <summary>Object type index (e.g. File, Directory).</summary>
    public ushort ObjectTypeIndex { get; init; }

    /// <summary>Resolved path of the file or directory (if available).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Access rights granted on the handle.</summary>
    public uint GrantedAccess { get; init; }
}
