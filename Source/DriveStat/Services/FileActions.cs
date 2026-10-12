using System.Runtime.InteropServices;

namespace DriveStat.Services;

/// <summary>
/// Recycle-bin and permanent delete helpers for DriveStat UI actions.
/// </summary>
public static class FileActions
{
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofSilent = 0x0004;
    private const ushort FofWantNukeWarning = 0x4000;
    private const uint FoDelete = 0x0003;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);

    /// <summary>Sends a file or directory to the Recycle Bin. Returns true on success.</summary>
    public static bool TryRecycle(string path)
        => ShellDelete(path, allowUndo: true);

    /// <summary>Permanently deletes a file or directory. Returns true on success.</summary>
    public static bool TryDeletePermanent(string path)
        => ShellDelete(path, allowUndo: false);

    private static bool ShellDelete(string path, bool allowUndo)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // SHFileOperation requires a double-null-terminated list.
        string from = path + "\0\0";
        ushort flags = (ushort)(FofNoConfirmation | FofSilent | FofWantNukeWarning);
        if (allowUndo)
        {
            flags |= FofAllowUndo;
        }

        var op = new ShFileOpStruct
        {
            hwnd = IntPtr.Zero,
            wFunc = FoDelete,
            pFrom = from,
            pTo = null,
            fFlags = flags,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        try
        {
            int result = SHFileOperation(ref op);
            return result == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Shell delete failed for '{path}'.", ex, nameof(FileActions));
            return false;
        }
    }
}
