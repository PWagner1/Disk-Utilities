#nullable enable

namespace DiskSeer.Services.Apfs;

/// <summary>
/// P/Invoke bindings for libfsapfs (Apple File System access library).
/// Requires libfsapfs.dll and its dependencies to be built and placed in the application directory.
/// See Docs/LIBFSAPFS_BUILD.md for build instructions.
/// </summary>
internal static class LibFsApfsNative
{
    private const string DllName = "libfsapfs-1";

    // S_IFDIR from sys/stat.h
    public const int S_IFDIR = 0x4000;

    /// <summary>Returns 1 if the library is available (DLL loaded).</summary>
    public static bool IsAvailable { get; } = LoadLibrary();

    private static bool LoadLibrary()
    {
        try
        {
            _ = GetVersion();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_get_version")]
    public static extern IntPtr GetVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_get_access_flags_read")]
    public static extern int GetAccessFlagsRead();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_initialize")]
    public static extern int ContainerInitialize(out IntPtr container, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_free")]
    public static extern int ContainerFree(ref IntPtr container, ref IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_open")]
    public static extern int ContainerOpen(IntPtr container, [MarshalAs(UnmanagedType.LPStr)] string filename, int accessFlags, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_open_wide")]
    public static extern int ContainerOpenWide(IntPtr container, [MarshalAs(UnmanagedType.LPWStr)] string filename, int accessFlags, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_close")]
    public static extern int ContainerClose(IntPtr container, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_get_number_of_volumes")]
    public static extern int ContainerGetNumberOfVolumes(IntPtr container, out int count, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_container_get_volume_by_index")]
    public static extern int ContainerGetVolumeByIndex(IntPtr container, int index, out IntPtr volume, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_volume_free")]
    public static extern int VolumeFree(ref IntPtr volume, ref IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_volume_get_root_directory")]
    public static extern int VolumeGetRootDirectory(IntPtr volume, out IntPtr fileEntry, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_volume_get_file_entry_by_utf8_path")]
    public static extern int VolumeGetFileEntryByUtf8Path(IntPtr volume, byte[] utf8Path, UIntPtr pathLen, out IntPtr fileEntry, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_free")]
    public static extern int FileEntryFree(ref IntPtr fileEntry, ref IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_file_mode")]
    public static extern int FileEntryGetFileMode(IntPtr fileEntry, out ushort mode, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_utf8_name_size")]
    public static extern int FileEntryGetUtf8NameSize(IntPtr fileEntry, out UIntPtr size, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_utf8_name")]
    public static extern int FileEntryGetUtf8Name(IntPtr fileEntry, byte[] buffer, UIntPtr bufferSize, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_size")]
    public static extern int FileEntryGetSize(IntPtr fileEntry, out ulong size, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_modification_time")]
    public static extern int FileEntryGetModificationTime(IntPtr fileEntry, out long posixNanos, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_creation_time")]
    public static extern int FileEntryGetCreationTime(IntPtr fileEntry, out long posixNanos, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_number_of_sub_file_entries")]
    public static extern int FileEntryGetNumberOfSubFileEntries(IntPtr fileEntry, out int count, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_get_sub_file_entry_by_index")]
    public static extern int FileEntryGetSubFileEntryByIndex(IntPtr fileEntry, int index, out IntPtr subEntry, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_file_entry_read_buffer_at_offset")]
    public static extern nint FileEntryReadBufferAtOffset(IntPtr fileEntry, byte[] buffer, UIntPtr bufferSize, long offset, out IntPtr error);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "libfsapfs_error_free")]
    public static extern void ErrorFree(ref IntPtr error);
}
