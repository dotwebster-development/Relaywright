using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreService(
    AppPaths appPaths,
    DatabaseConfiguration databaseConfiguration,
    ILogger<BackupRestoreService> logger) : IBackupRestoreService
{
    private const long MaxRestoreUploadBytes = 5L * 1024 * 1024 * 1024;
    private const long MaxExtractedArchiveBytes = 50L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntryCount = 200_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<BackupRestoreResult> StageRestoreAsync(
        IFormFile backupFile,
        string? encryptionPassword,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsExternalServer)
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = $"{databaseConfiguration.Provider} database restore is managed outside Relaywright. Restore the database with platform tooling and restore spool/certificate files from host backups."
            };
        }

        if (backupFile.Length <= 0)
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = "Select a Relaywright backup file."
            };
        }

        if (backupFile.Length > MaxRestoreUploadBytes)
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = $"Backup file is too large. The restore upload limit is {MaxRestoreUploadBytes / 1024 / 1024 / 1024} GB."
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

            var summary = await ExtractAndValidateArchiveAsync(archivePath, stagingDirectory, cancellationToken);

            if (Directory.Exists(appPaths.RestorePendingDirectory))
            {
                Directory.Delete(appPaths.RestorePendingDirectory, recursive: true);
            }

            Directory.Move(stagingDirectory, appPaths.RestorePendingDirectory);

            logger.LogWarning(
                "Restore staged from authenticated backup restore. FileName={FileName}; Encrypted={Encrypted}",
                backupFile.FileName,
                BackupEncryption.LooksEncrypted(uploadPath));

            return new BackupRestoreResult
            {
                Succeeded = true,
                RestartRequired = true,
                Message = "Restore staged. Restart Relaywright to apply the restored database, spool, certificates, and listener settings. Admin accounts, protected relay secrets, Data Protection keys, and admin HTTPS certificate passwords are not restored.",
                Summary = summary
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Restore staging failed.");
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
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "certs"), paths.CertificateDirectory);
        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "admin-web-listener.json"), paths.AdminWebListenerConfigurationPath);

        Directory.CreateDirectory(paths.SpoolRootDirectory);
        Directory.CreateDirectory(paths.KeyRingDirectory);
        Directory.CreateDirectory(paths.CertificateDirectory);
        Directory.Delete(paths.RestorePendingDirectory, recursive: true);
    }

    private static async Task<BackupRestoreSummary> ExtractAndValidateArchiveAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Backup manifest is missing.");
        var databaseEntry = archive.GetEntry("relay.db")
            ?? throw new InvalidOperationException("Database snapshot is missing.");

        RestoreManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<RestoreManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Backup manifest could not be read.");
            if (!string.Equals(manifest.Application, "Relaywright", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backup manifest is not for Relaywright.");
            }
        }

        ValidateArchiveShape(archive);

        ExtractRequiredEntry(databaseEntry, stagingDirectory, "relay.db");
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var restorePath = GetAllowedRestorePath(entry.FullName);
            if (restorePath is not null)
            {
                ExtractEntry(entry, stagingDirectory, restorePath);
            }
        }

        var stagedDatabasePath = Path.Combine(stagingDirectory, "relay.db");
        var databaseSummary = await ReadDatabaseSummaryAsync(stagedDatabasePath, cancellationToken);
        await BackupCredentialSanitizer.SanitizeAsync(stagedDatabasePath, cancellationToken);
        await ValidateRestoredTrustedNetworksAsync(stagedDatabasePath, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(stagingDirectory, "restore.json"),
            JsonSerializer.Serialize(new
            {
                stagedUtc = DateTimeOffset.UtcNow
            }, JsonOptions),
            cancellationToken);

        return new BackupRestoreSummary
        {
            BackupId = manifest.BackupId == Guid.Empty ? null : manifest.BackupId,
            CreatedUtc = manifest.CreatedUtc == default ? null : manifest.CreatedUtc,
            ManifestSpoolFileCount = manifest.SpoolFileCount,
            ArchiveSpoolFileCount = archive.Entries.Count(x =>
                !string.IsNullOrWhiteSpace(x.Name)
                && NormalizeArchivePath(x.FullName).StartsWith("spool/", StringComparison.OrdinalIgnoreCase)),
            QueuedMessageCount = databaseSummary.QueuedMessageCount,
            IncludesCertificateFiles = archive.Entries.Any(x =>
                !string.IsNullOrWhiteSpace(x.Name)
                && NormalizeArchivePath(x.FullName).StartsWith("certs/", StringComparison.OrdinalIgnoreCase)),
            IncludesAdminWebListener = archive.Entries.Any(x =>
                !string.IsNullOrWhiteSpace(x.Name)
                && string.Equals(NormalizeArchivePath(x.FullName), "admin-web-listener.json", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static void ExtractRequiredEntry(ZipArchiveEntry entry, string root, string relativePath)
    {
        ExtractEntry(entry, root, relativePath);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string root, string relativePath)
    {
        if (entry.Length < 0 || entry.Length > MaxExtractedArchiveBytes)
        {
            throw new InvalidOperationException($"Backup entry is too large: {entry.FullName}");
        }

        var destination = ResolveUnder(root, relativePath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        entry.ExtractToFile(destination, overwrite: true);
    }

    private static void ValidateArchiveShape(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxArchiveEntryCount)
        {
            throw new InvalidOperationException("Backup archive contains too many entries.");
        }

        long totalSize = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalizedName = NormalizeArchivePath(entry.FullName);
            if (!names.Add(normalizedName))
            {
                throw new InvalidOperationException($"Backup archive contains a duplicate entry: {entry.FullName}");
            }

            if (entry.Length < 0)
            {
                throw new InvalidOperationException($"Backup entry has an invalid length: {entry.FullName}");
            }

            totalSize += entry.Length;
            if (totalSize > MaxExtractedArchiveBytes)
            {
                throw new InvalidOperationException($"Backup archive expands beyond the restore limit of {MaxExtractedArchiveBytes / 1024 / 1024 / 1024} GB.");
            }

            _ = GetAllowedRestorePath(entry.FullName);
        }
    }

    private static string? GetAllowedRestorePath(string entryName)
    {
        var normalized = NormalizeArchivePath(entryName);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "relay.db", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (normalized.StartsWith("spool/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("certs/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "admin-web-listener.json", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("keys/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "admin-https-certificate.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        throw new InvalidOperationException($"Backup archive contains an unsupported entry: {entryName}");
    }

    private static string NormalizeArchivePath(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Backup entry uses an absolute path: {entryName}");
        }

        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Contains(':', StringComparison.Ordinal)
                || segment.IndexOfAny(invalidFileNameCharacters) >= 0))
        {
            throw new InvalidOperationException($"Backup entry contains an invalid path segment: {entryName}");
        }

        return string.Join('/', segments);
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

    private static async Task<RestoreDatabaseSummary> ReadDatabaseSummaryAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"AspNetUsers\";";
        _ = await command.ExecuteScalarAsync(cancellationToken);

        long queuedMessages = 0;
        if (await TableExistsAsync(connection, "QueuedMessages", cancellationToken))
        {
            await using var queuedCommand = connection.CreateCommand();
            queuedCommand.CommandText = "SELECT COUNT(*) FROM \"QueuedMessages\";";
            queuedMessages = Convert.ToInt64(await queuedCommand.ExecuteScalarAsync(cancellationToken));
        }

        return new RestoreDatabaseSummary(queuedMessages);
    }

    private static async Task ValidateRestoredTrustedNetworksAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "TrustedNetworks", cancellationToken))
        {
            return;
        }

        var networks = new List<TrustedNetworkCidr>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"Id\", \"Cidr\" FROM \"TrustedNetworks\";";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32(0);
                var cidr = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!CidrRange.TryParse(cidr, out var range))
                {
                    throw new InvalidOperationException($"Restored backup contains an invalid trusted network CIDR: {cidr}");
                }

                networks.Add(new TrustedNetworkCidr(id, cidr, range!));
            }
        }

        for (var i = 0; i < networks.Count; i++)
        {
            for (var j = i + 1; j < networks.Count; j++)
            {
                if (networks[i].Range.Overlaps(networks[j].Range))
                {
                    throw new InvalidOperationException(
                        $"Restored backup contains overlapping trusted networks: {networks[i].Cidr} and {networks[j].Cidr}.");
                }
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
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

        public DateTimeOffset CreatedUtc { get; set; }

        public int SpoolFileCount { get; set; }
    }

    private sealed record RestoreDatabaseSummary(long QueuedMessageCount);

    private sealed record TrustedNetworkCidr(int Id, string Cidr, CidrRange Range);
}
