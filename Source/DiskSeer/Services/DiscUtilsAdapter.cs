#nullable enable

namespace DiskSeer.Services;

/// <summary>
/// Wraps DiscUtils IFileSystem to implement IForeignFileSystemAdapter.
/// </summary>
internal sealed class DiscUtilsAdapter : IForeignFileSystemAdapter
{
    private readonly IFileSystem _fs;

    public DiscUtilsAdapter(IFileSystem fs)
    {
        _fs = fs;
    }

    public bool DirectoryExists(string path) => _fs.DirectoryExists(path);
    public bool Exists(string path) => _fs.Exists(path);

    public IEnumerable<string> GetDirectories(string path) => _fs.GetDirectories(path);
    public IEnumerable<string> GetFiles(string path) => _fs.GetFiles(path);

    public (DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetDirectoryInfo(string path)
    {
        var info = _fs.GetDirectoryInfo(path);
        return (info.LastWriteTimeUtc, info.CreationTimeUtc, info.LastAccessTimeUtc, info.Attributes);
    }

    public (long Length, DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetFileInfo(string path)
    {
        var info = _fs.GetFileInfo(path);
        return (info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, info.LastAccessTimeUtc, info.Attributes);
    }

    public Stream OpenFileRead(string path) => _fs.OpenFile(path, FileMode.Open, FileAccess.Read);
    public Stream OpenFileWrite(string path) => _fs.OpenFile(path, FileMode.Create, FileAccess.Write);

    public string GetVolumeLabel()
    {
        return _fs switch
        {
            ExtFileSystem => "Ext Volume",
            HfsPlusFileSystem => "HFS+ Volume",
            BtrfsFileSystem => "Btrfs Volume",
            XfsFileSystem => "XFS Volume",
            _ => string.Empty
        };
    }

    public void CreateDirectory(string path) => _fs.CreateDirectory(path);
    public void DeleteFile(string path) => _fs.DeleteFile(path);
    public void DeleteDirectory(string path, bool recursive) => _fs.DeleteDirectory(path, recursive);
    public void MoveFile(string source, string dest) => _fs.MoveFile(source, dest);

    public void Dispose()
    {
        if (_fs is IDisposable d)
            d.Dispose();
    }
}
