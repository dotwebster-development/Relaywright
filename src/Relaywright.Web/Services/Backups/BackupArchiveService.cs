using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupArchiveService(AppPaths appPaths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<int> CreateAsync(
        Guid backupId,
        DateTimeOffset createdUtc,
        string tempDirectory,
        string destinationPath,
        string? encryptionPassword,
        CancellationToken cancellationToken)
    {
        var snapshotPath = Path.Combine(tempDirectory, "relay.db");
        var zipPath = Path.Combine(tempDirectory, "bundle.zip");
        await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
        await BackupCredentialSanitizer.SanitizeAsync(snapshotPath, cancellationToken);
        var spoolPaths = await ReadSpoolPathsFromSnapshotAsync(snapshotPath, cancellationToken);
        var manifest = new BackupManifest
        {
            BackupId = backupId,
            CreatedUtc = createdUtc,
            Application = "Relaywright",
            DatabaseFile = "relay.db",
            SpoolFileCount = spoolPaths.Count,
            SpoolFiles = spoolPaths.ToList()
        };

        await CreateZipAsync(zipPath, snapshotPath, manifest, cancellationToken);
        if (!string.IsNullOrWhiteSpace(encryptionPassword))
        {
            await BackupEncryption.EncryptFileAsync(
                zipPath,
                destinationPath,
                encryptionPassword,
                cancellationToken);
        }
        else
        {
            File.Move(zipPath, destinationPath, overwrite: true);
        }

        return spoolPaths.Count;
    }

    public async Task ValidateAsync(
        string path,
        string tempDirectory,
        string? encryptionPassword,
        CancellationToken cancellationToken)
    {
        var readableArchivePath = await PrepareReadableArchiveAsync(
            path,
            tempDirectory,
            encryptionPassword,
            cancellationToken);
        using var archive = ZipFile.OpenRead(readableArchivePath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Backup manifest is missing.");
        var databaseEntry = archive.GetEntry("relay.db")
            ?? throw new InvalidOperationException("Database snapshot is missing.");

        BackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidOperationException("Backup manifest could not be read.");
        }

        var extractedDatabase = Path.Combine(tempDirectory, "relay.db");
        databaseEntry.ExtractToFile(extractedDatabase, overwrite: true);
        await ValidateDatabaseAsync(extractedDatabase, cancellationToken);

        var missingSpoolEntry = manifest.SpoolFiles
            .Select(ToZipSpoolEntry)
            .FirstOrDefault(entryName => archive.GetEntry(entryName) is null);
        if (missingSpoolEntry is not null)
        {
            throw new InvalidOperationException($"Spool entry is missing from backup: {missingSpoolEntry}");
        }
    }

    private async Task CreateDatabaseSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection($"Data Source={appPaths.DatabasePath};Pooling=False");
        await using var destination = new SqliteConnection($"Data Source={snapshotPath};Pooling=False");
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task<IReadOnlyList<string>> ReadSpoolPathsFromSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"SpoolFileRelativePath\" FROM \"QueuedMessages\" WHERE \"SpoolFileRelativePath\" IS NOT NULL;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                paths.Add(reader.GetString(0));
            }
        }

        return paths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task CreateZipAsync(
        string backupPath,
        string snapshotPath,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(snapshotPath, "relay.db", CompressionLevel.Optimal);

        foreach (var spoolPath in manifest.SpoolFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolutePath = appPaths.GetSpoolAbsolutePath(spoolPath);
            if (!File.Exists(absolutePath))
            {
                manifest.MissingSpoolFiles.Add(spoolPath);
                continue;
            }

            archive.CreateEntryFromFile(absolutePath, ToZipSpoolEntry(spoolPath), CompressionLevel.Optimal);
        }

        AddDirectoryEntries(archive, appPaths.CertificateDirectory, "certs");
        AddFileIfExists(archive, appPaths.AdminWebListenerConfigurationPath, "admin-web-listener.json");

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
    }

    private static async Task<string> PrepareReadableArchiveAsync(
        string path,
        string tempDirectory,
        string? encryptionPassword,
        CancellationToken cancellationToken)
    {
        if (!BackupEncryption.LooksEncrypted(path))
        {
            return path;
        }

        var decryptedPath = Path.Combine(tempDirectory, "bundle.zip");
        await BackupEncryption.DecryptFileAsync(path, decryptedPath, encryptionPassword ?? string.Empty, cancellationToken);
        return decryptedPath;
    }

    private static async Task ValidateDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"QueuedMessages\";";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string ToZipSpoolEntry(string relativePath) =>
        $"spool/{relativePath.Replace('\\', '/')}";

    private static void AddDirectoryEntries(ZipArchive archive, string directory, string prefix)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{prefix}/{relative}", CompressionLevel.Optimal);
        }
    }

    private static void AddFileIfExists(ZipArchive archive, string path, string entryName)
    {
        if (File.Exists(path))
        {
            archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        }
    }

    private sealed class BackupManifest
    {
        public Guid BackupId { get; set; }

        public string Application { get; set; } = string.Empty;

        public DateTimeOffset CreatedUtc { get; set; }

        public string DatabaseFile { get; set; } = string.Empty;

        public int SpoolFileCount { get; set; }

        public List<string> SpoolFiles { get; set; } = [];

        public List<string> MissingSpoolFiles { get; } = [];
    }
}
