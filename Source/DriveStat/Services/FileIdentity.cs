using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DriveStat.Services;

/// <summary>
/// Windows file identity helpers for hard-link deduplication and reparse detection.
/// </summary>
internal static class FileIdentity
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    /// <summary>Returns true if the path is a reparse point (junction, symlink, mount point, etc.).</summary>
    public static bool IsReparsePoint(FileSystemInfo info)
        => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Tries to get a volume+file-index key for hard-link deduplication.
    /// Returns false if the handle cannot be opened (still safe to count the file once).
    /// </summary>
    public static bool TryGetFileKey(string path, out ulong key)
    {
        key = 0;
        using SafeFileHandle handle = CreateFile(
            path,
            FileReadAttributes,
            FileShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
        {
            return false;
        }

        key = ((ulong)info.VolumeSerialNumber << 32)
            ^ (((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        return true;
    }
}
