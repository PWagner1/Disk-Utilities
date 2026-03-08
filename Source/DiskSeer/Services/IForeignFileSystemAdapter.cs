#nullable enable

namespace DiskSeer.Services;

/// <summary>
/// Minimal adapter for file system access. Implemented by DiscUtils wrappers and ApfsFileSystem.
/// </summary>
internal interface IForeignFileSystemAdapter : IDisposable
{
    bool DirectoryExists(string path);
    bool Exists(string path);
    IEnumerable<string> GetDirectories(string path);
    IEnumerable<string> GetFiles(string path);
    (DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetDirectoryInfo(string path);
    (long Length, DateTime LastWriteUtc, DateTime CreationUtc, DateTime LastAccessUtc, FileAttributes Attributes) GetFileInfo(string path);
    Stream OpenFileRead(string path);
    Stream OpenFileWrite(string path);
    string GetVolumeLabel();
    void CreateDirectory(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    void MoveFile(string source, string dest);
}
