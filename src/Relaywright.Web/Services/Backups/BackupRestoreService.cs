using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreService(
    AppPaths appPaths,
    ILogger<BackupRestoreService> logger) : IBackupRestoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<BackupRestoreResult> StageRestoreAsync(
        IFormFile backupFile,
        string? encryptionPassword,
        CancellationToken cancellationToken)
    {
        if (backupFile.Length <= 0)
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = "Select a Relaywright backup file."
            };
        }

        var tempDirectory = Path.Combine(appPaths.DataDirectory, $".restore-upload-{Guid.NewGuid():N}");
        var stagingDirectory = Path.Combine(appPaths.DataDirectory, $".restore-stage-{Guid.NewGuid():N}");
        var uploadPath = Path.Combine(tempDirectory, Path.GetFileName(backupFile.FileName));

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(stagingDirectory);

            await using (var upload = File.Create(uploadPath))
            {
                await backupFile.CopyToAsync(upload, cancellationToken);
            }

            var archivePath = uploadPath;
            if (BackupEncryption.LooksEncrypted(uploadPath))
            {
                archivePath = Path.Combine(tempDirectory, "bundle.zip");
                await BackupEncryption.DecryptFileAsync(
                    uploadPath,
                    archivePath,
                    encryptionPassword ?? string.Empty,
                    cancellationToken);
            }

            await ExtractAndValidateArchiveAsync(archivePath, stagingDirectory, cancellationToken);

            if (Directory.Exists(appPaths.RestorePendingDirectory))
            {
                Directory.Delete(appPaths.RestorePendingDirectory, recursive: true);
            }

            Directory.Move(stagingDirectory, appPaths.RestorePendingDirectory);

            logger.LogWarning(
                "Restore staged from first-run setup. FileName={FileName}; Encrypted={Encrypted}",
                backupFile.FileName,
                BackupEncryption.LooksEncrypted(uploadPath));

            return new BackupRestoreResult
            {
                Succeeded = true,
                RestartRequired = true,
                Message = "Restore staged. Restart Relaywright to apply the restored database, spool, keys, certificates, and listener settings."
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Restore staging failed from first-run setup.");
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = exception.Message
            };
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
            DeleteDirectoryIfExists(stagingDirectory);
        }
    }

    public static void ApplyPendingRestore(AppPaths paths)
    {
        if (!Directory.Exists(paths.RestorePendingDirectory))
        {
            return;
        }

        var markerPath = Path.Combine(paths.RestorePendingDirectory, "restore.json");
        if (!File.Exists(markerPath))
        {
            return;
        }

        var safetyDirectory = Path.Combine(
            paths.DataDirectory,
            $".restore-safety-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(safetyDirectory);

        MoveFileIfExists(paths.DatabasePath, Path.Combine(safetyDirectory, Path.GetFileName(paths.DatabasePath)));
        MoveDirectoryIfExists(paths.SpoolRootDirectory, Path.Combine(safetyDirectory, "spool"));
        MoveDirectoryIfExists(paths.KeyRingDirectory, Path.Combine(safetyDirectory, "keys"));
        MoveDirectoryIfExists(paths.CertificateDirectory, Path.Combine(safetyDirectory, "certs"));
        MoveFileIfExists(paths.AdminHttpsCertificateConfigurationPath, Path.Combine(safetyDirectory, "admin-https-certificate.json"));
        MoveFileIfExists(paths.AdminWebListenerConfigurationPath, Path.Combine(safetyDirectory, "admin-web-listener.json"));

        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "relay.db"), paths.DatabasePath);
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "spool"), paths.SpoolRootDirectory);
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "keys"), paths.KeyRingDirectory);
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "certs"), paths.CertificateDirectory);
        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "admin-https-certificate.json"), paths.AdminHttpsCertificateConfigurationPath);
        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "admin-web-listener.json"), paths.AdminWebListenerConfigurationPath);

        Directory.CreateDirectory(paths.SpoolRootDirectory);
        Directory.CreateDirectory(paths.KeyRingDirectory);
        Directory.CreateDirectory(paths.CertificateDirectory);
        Directory.Delete(paths.RestorePendingDirectory, recursive: true);
    }

    private static async Task ExtractAndValidateArchiveAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Backup manifest is missing.");
        var databaseEntry = archive.GetEntry("relay.db")
            ?? throw new InvalidOperationException("Database snapshot is missing.");

        await using (var manifestStream = manifestEntry.Open())
        {
            _ = await JsonSerializer.DeserializeAsync<RestoreManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Backup manifest could not be read.");
        }

        ExtractRequiredEntry(databaseEntry, stagingDirectory, "relay.db");
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (entry.FullName.StartsWith("spool/", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("keys/", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("certs/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FullName, "admin-https-certificate.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FullName, "admin-web-listener.json", StringComparison.OrdinalIgnoreCase))
            {
                ExtractEntry(entry, stagingDirectory, entry.FullName);
            }
        }

        if (!Directory.Exists(Path.Combine(stagingDirectory, "keys")))
        {
            throw new InvalidOperationException("Data Protection key entries are missing.");
        }

        await ValidateDatabaseAsync(Path.Combine(stagingDirectory, "relay.db"), cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(stagingDirectory, "restore.json"),
            JsonSerializer.Serialize(new
            {
                stagedUtc = DateTimeOffset.UtcNow
            }, JsonOptions),
            cancellationToken);
    }

    private static void ExtractRequiredEntry(ZipArchiveEntry entry, string root, string relativePath)
    {
        ExtractEntry(entry, root, relativePath);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string root, string relativePath)
    {
        var destination = ResolveUnder(root, relativePath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        entry.ExtractToFile(destination, overwrite: true);
    }

    private static string ResolveUnder(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("Backup entry resolves outside the restore directory.");
        }

        return candidate;
    }

    private static async Task ValidateDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"AspNetUsers\";";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void MoveFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
    }

    private static void MoveDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(source, destination);
    }

    private static void CopyFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RestoreManifest
    {
        public Guid BackupId { get; set; }

        public string Application { get; set; } = string.Empty;
    }
}
