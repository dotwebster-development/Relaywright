namespace Relaywright.Web.Services.Backups;

public interface IBackupFileSystem
{
    void CreateDirectory(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    long GetFileSize(string path);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}

public sealed class PhysicalBackupFileSystem : IBackupFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }
}
