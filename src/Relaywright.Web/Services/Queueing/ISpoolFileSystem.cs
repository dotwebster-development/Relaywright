namespace Relaywright.Web.Services.Queueing;

public interface ISpoolFileSystem
{
    void CreateDirectory(string path);

    Stream CreateWriteThrough(string path);

    void FlushToDisk(Stream stream);

    void MoveFile(string sourcePath, string destinationPath);

    Stream OpenRead(string path);

    bool FileExists(string path);

    void DeleteFile(string path);
}

public sealed class PhysicalSpoolFileSystem : ISpoolFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Stream CreateWriteThrough(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is not FileStream fileStream)
        {
            throw new InvalidOperationException("A physical spool stream is required for a durable flush.");
        }

        fileStream.Flush(flushToDisk: true);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, overwrite: false);
    }

    public Stream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous);
    }

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);
}
