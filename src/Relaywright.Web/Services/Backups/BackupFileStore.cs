using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupFileStore(
    AppPaths appPaths,
    IBackupFileSystem fileSystem,
    ILogger<BackupFileStore> logger)
{
    public void EnsureCreated()
    {
        fileSystem.CreateDirectory(appPaths.BackupDirectory);
    }

    public string GetWorkingDirectory(string purpose, Guid backupId)
    {
        return Path.Combine(appPaths.BackupDirectory, $".{purpose}-{backupId:N}");
    }

    public void CreateWorkingDirectory(string path)
    {
        fileSystem.CreateDirectory(path);
    }

    public string CreateBackupFileName(BackupRun run, bool encrypted)
    {
        return $"relaywright-backup-{run.StartedUtc:yyyyMMdd-HHmmss}-{run.Id:N}.{(encrypted ? "rwbak" : "zip")}";
    }

    public string GetPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(fileName, safeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backup file name is invalid.");
        }

        return Path.Combine(appPaths.BackupDirectory, safeName);
    }

    public string? GetPath(BackupRun run)
    {
        return string.IsNullOrWhiteSpace(run.FileName) ? null : GetPath(run.FileName);
    }

    public bool Exists(string path) => fileSystem.FileExists(path);

    public long GetFileSize(string path) => fileSystem.GetFileSize(path);

    public void DeleteFileIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && fileSystem.FileExists(path))
        {
            fileSystem.DeleteFile(path);
        }
    }

    public long GetStorageBytes()
    {
        if (!fileSystem.DirectoryExists(appPaths.BackupDirectory))
        {
            return 0;
        }

        return fileSystem
            .EnumerateFiles(appPaths.BackupDirectory, "*", SearchOption.AllDirectories)
            .Sum(fileSystem.GetFileSize);
    }

    public void DeleteWorkingDirectoryBestEffort(string directory, Guid backupId, string purpose)
    {
        try
        {
            if (fileSystem.DirectoryExists(directory))
            {
                fileSystem.DeleteDirectory(directory, recursive: true);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to remove backup working directory. BackupId={BackupId}; Purpose={Purpose}; Directory={Directory}",
                backupId,
                purpose,
                directory);
        }
    }
}
