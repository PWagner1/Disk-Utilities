#nullable enable

using System.Diagnostics;
using System.Threading;

namespace DiskSeer.Services.Apfs;

/// <summary>
/// Read-only APFS file system adapter using libfsapfs.
/// Requires libfsapfs.dll to be built and placed in the application directory.
/// </summary>
public sealed class ApfsFileSystem : IForeignFileSystemAdapter
{
    private IntPtr _container;
    private IntPtr _volume;
    private readonly string _tempFilePath;
    private const string VOLUME_LABEL = "APFS Volume";
    private bool _disposed;

    private ApfsFileSystem(IntPtr container, IntPtr volume, string tempFilePath)
    {
        _container = container;
        _volume = volume;
        _tempFilePath = tempFilePath;
    }

    /// <summary>Returns true if libfsapfs is available.</summary>
    public static bool IsAvailable => LibFsApfsNative.IsAvailable;

    /// <summary>Maximum partition size that will be staged to a temp file for libfsapfs.</summary>
    public const long MaxTempCopyBytes = 128L * 1024 * 1024 * 1024; // 128 GB

    /// <summary>
    /// Creates an APFS file system from a stream (partition content).
    /// Stages to a temporary file for libfsapfs (up to <see cref="MaxTempCopyBytes"/>).
    /// </summary>
    public static ApfsFileSystem? Open(
        Stream partitionStream,
        IProgress<(long Copied, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!LibFsApfsNative.IsAvailable)
            return null;

        long length = partitionStream.CanSeek ? partitionStream.Length : -1;
        if (length > MaxTempCopyBytes)
        {
            throw new NotSupportedException(
                $"APFS partition exceeds {MaxTempCopyBytes / (1024L * 1024 * 1024)} GB staging limit for libfsapfs temp copy.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "DiskSeer_apfs_" + Guid.NewGuid().ToString("N") + ".raw");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                partitionStream.Position = 0;
                var buffer = new byte[1024 * 1024];
                long copied = 0;
                long total = length > 0 ? length : 0;
                progress?.Report((0, total));
                int read;
                while ((read = partitionStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fs.Write(buffer, 0, read);
                    copied += read;
                    if (total <= 0)
                    {
                        total = copied;
                    }

                    progress?.Report((copied, total));
                }
            }

            if (LibFsApfsNative.ContainerInitialize(out var container, out var err) != 1)
            {
                LibFsApfsNative.ErrorFree(ref err);
                return null;
            }

            var accessFlags = LibFsApfsNative.GetAccessFlagsRead();
            if (LibFsApfsNative.ContainerOpen(container, tempPath, accessFlags, out err) != 1)
            {
                LibFsApfsNative.ContainerFree(ref container, ref err);
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception e)
                {
                    // Ignore deletion errors, but log if needed
                    Debug.Write("Failed to delete temporary file: " + tempPath);
                }
                return null;
            }

            if (LibFsApfsNative.ContainerGetNumberOfVolumes(container, out var volCount, out err) != 1 || volCount < 1)
            {
                LibFsApfsNative.ContainerClose(container, out _);
                LibFsApfsNative.ContainerFree(ref container, ref err);
                try { File.Delete(tempPath); } catch (Exception e) { Debug.Write("Failed to delete temporary file: " + tempPath); }
                return null;
            }

            if (LibFsApfsNative.ContainerGetVolumeByIndex(container, 0, out var volume, out err) != 1)
            {
                LibFsApfsNative.ContainerClose(container, out _);
                LibFsApfsNative.ContainerFree(ref container, ref err);
                try { File.Delete(tempPath); } catch (Exception e) { Debug.Write("Failed to delete temporary file: " + tempPath); }
                return null;
            }

            return new ApfsFileSystem(container, volume, tempPath);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (Exception e) { Debug.Write("Failed to delete temporary file: " + tempPath); }
            throw;
        }
    }

    public bool DirectoryExists(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) return false;
        try { return IsDirectory(entry); }
        finally { FreeEntry(entry); }
    }

    public bool Exists(string path) => GetFileEntry(path) != IntPtr.Zero;

    public IEnumerable<string> GetDirectories(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) yield break;
        try
        {
            if (!IsDirectory(entry)) yield break;
            var basePath = NormalizeBasePath(path);
            if (LibFsApfsNative.FileEntryGetNumberOfSubFileEntries(entry, out var count, out _) != 1) yield break;
            for (int i = 0; i < count; i++)
            {
                if (LibFsApfsNative.FileEntryGetSubFileEntryByIndex(entry, i, out var sub, out _) != 1) continue;
                try
                {
                    if (IsDirectory(sub))
                    {
                        var name = GetUtf8Name(sub);
                        if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
                            yield return basePath + "/" + name;
                    }
                }
                finally { FreeEntry(sub); }
            }
        }
        finally { FreeEntry(entry); }
    }

    public IEnumerable<string> GetFiles(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) yield break;
        try
        {
            if (!IsDirectory(entry)) yield break;
            var basePath = NormalizeBasePath(path);
            if (LibFsApfsNative.FileEntryGetNumberOfSubFileEntries(entry, out var count, out _) != 1) yield break;
            for (int i = 0; i < count; i++)
            {
                if (LibFsApfsNative.FileEntryGetSubFileEntryByIndex(entry, i, out var sub, out _) != 1) continue;
                try
                {
                    if (!IsDirectory(sub))
                    {
                        var name = GetUtf8Name(sub);
                        if (!string.IsNullOrEmpty(name))
                            yield return basePath + "/" + name;
                    }
                }
                finally { FreeEntry(sub); }
            }
        }
        finally { FreeEntry(entry); }
    }

    public (DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetDirectoryInfo(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) throw new FileNotFoundException("Path not found.", path);
        try
        {
            if (!IsDirectory(entry)) throw new FileNotFoundException("Not a directory.", path);
            return GetEntryInfo(entry);
        }
        finally { FreeEntry(entry); }
    }

    public (long Length, DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetFileInfo(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) throw new FileNotFoundException("Path not found.", path);
        try
        {
            if (IsDirectory(entry)) throw new FileNotFoundException("Not a file.", path);
            var (mod, create, access, attr) = GetEntryInfo(entry);
            LibFsApfsNative.FileEntryGetSize(entry, out var size, out _);
            return ((long)size, mod, create, access, attr);
        }
        finally { FreeEntry(entry); }
    }

    public Stream OpenFileRead(string path)
    {
        var entry = GetFileEntry(path);
        if (entry == IntPtr.Zero) throw new FileNotFoundException("Path not found.", path);
        if (IsDirectory(entry))
        {
            FreeEntry(entry);
            throw new FileNotFoundException("Cannot open directory as file.", path);
        }
        if (LibFsApfsNative.FileEntryGetSize(entry, out var size, out _) != 1)
        {
            FreeEntry(entry);
            throw new IOException("Failed to get file size.");
        }
        return new ApfsFileStream(entry, (long)size);
    }

    public string GetVolumeLabel() => VOLUME_LABEL;

    public void CreateDirectory(string path) => throw new NotSupportedException("APFS is read-only.");
    public void DeleteFile(string path) => throw new NotSupportedException("APFS is read-only.");
    public void DeleteDirectory(string path, bool recursive) => throw new NotSupportedException("APFS is read-only.");
    public void MoveFile(string source, string dest) => throw new NotSupportedException("APFS is read-only.");
    public Stream OpenFileWrite(string path) => throw new NotSupportedException("APFS is read-only.");

    public void Dispose()
    {
        if (_disposed) return;
        IntPtr err = IntPtr.Zero;
        if (_volume != IntPtr.Zero)
        {
            var v = _volume;
            LibFsApfsNative.VolumeFree(ref v, ref err);
            _volume = IntPtr.Zero;
        }
        if (_container != IntPtr.Zero)
        {
            LibFsApfsNative.ContainerClose(_container, out err);
            var c = _container;
            LibFsApfsNative.ContainerFree(ref c, ref err);
            _container = IntPtr.Zero;
        }
        try { File.Delete(_tempFilePath); } catch (Exception e) { Debug.Write("Failed to delete temporary file: " + _tempFilePath); }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private IntPtr GetFileEntry(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        if (path == "/" || path == "")
        {
            return LibFsApfsNative.VolumeGetRootDirectory(_volume, out var root, out _) == 1 ? root : IntPtr.Zero;
        }
        var utf8 = Encoding.UTF8.GetBytes(path);
        return LibFsApfsNative.VolumeGetFileEntryByUtf8Path(_volume, utf8, (UIntPtr)utf8.Length, out var entry, out _) == 1 ? entry : IntPtr.Zero;
    }

    private static void FreeEntry(IntPtr entry)
    {
        if (entry == IntPtr.Zero) return;
        var e = entry;
        IntPtr err = IntPtr.Zero;
        LibFsApfsNative.FileEntryFree(ref e, ref err);
    }

    private static bool IsDirectory(IntPtr entry)
    {
        return LibFsApfsNative.FileEntryGetFileMode(entry, out var mode, out _) == 1 && (mode & LibFsApfsNative.SIfdir) != 0;
    }

    private static string GetUtf8Name(IntPtr entry)
    {
        if (LibFsApfsNative.FileEntryGetUtf8NameSize(entry, out var size, out _) != 1 || size == UIntPtr.Zero) return string.Empty;
        var buf = new byte[(int)size];
        if (LibFsApfsNative.FileEntryGetUtf8Name(entry, buf, size, out _) != 1) return string.Empty;
        return Encoding.UTF8.GetString(buf, 0, buf.Length - 1);
    }

    private static string NormalizeBasePath(string path)
    {
        var p = path.TrimEnd('/');
        return p.StartsWith('/') ? p : "/" + p;
    }

    private static (DateTime mod, DateTime create, DateTime access, FileAttributes attr) GetEntryInfo(IntPtr entry)
    {
        LibFsApfsNative.FileEntryGetModificationTime(entry, out var modNanos, out _);
        LibFsApfsNative.FileEntryGetCreationTime(entry, out var createNanos, out _);
        return (PosixNanosToUtc(modNanos), PosixNanosToUtc(createNanos), PosixNanosToUtc(modNanos), FileAttributes.Normal);
    }

    private static DateTime PosixNanosToUtc(long posixNanos)
    {
        if (posixNanos == 0) return DateTime.MinValue;
        var seconds = posixNanos / 1_000_000_000;
        var nanos = posixNanos % 1_000_000_000;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.AddTicks(nanos / 100);
    }

    private sealed class ApfsFileStream : Stream
    {
        private readonly IntPtr _entry;
        private readonly long _length;
        private long _position;
        private bool _disposed;

        public ApfsFileStream(IntPtr entry, long length)
        {
            _entry = entry;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => _position = Math.Clamp(value, 0, _length); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            var toRead = (int)Math.Min(count, _length - _position);
            var buf = new byte[toRead];
            var read = LibFsApfsNative.FileEntryReadBufferAtOffset(_entry, buf, (UIntPtr)toRead, _position, out _);
            if (read <= 0) return 0;
            Array.Copy(buf, 0, buffer, offset, (int)read);
            _position += read;
            return (int)read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position
            };
            return Position = Math.Clamp(_position, 0, _length);
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            FreeEntry(_entry);
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
