using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IBackupCoordinator backupCoordinator,
    IOperationalEventService eventService,
    AppPaths appPaths,
    ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<BackupRun>> GetRunsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return runs
            .OrderByDescending(x => x.StartedUtc)
            .Take(100)
            .ToList();
    }

    public async Task<BackupScheduleState> GetScheduleAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var schedule = await dbContext.BackupScheduleStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (schedule is not null)
        {
            return schedule;
        }

        schedule = new BackupScheduleState();
        dbContext.BackupScheduleStates.Add(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return schedule;
    }

    public async Task SaveScheduleAsync(BackupScheduleState schedule, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.BackupScheduleStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new BackupScheduleState();
            dbContext.BackupScheduleStates.Add(existing);
        }

        existing.IsEnabled = schedule.IsEnabled;
        existing.IntervalHours = Math.Clamp(schedule.IntervalHours, 1, 720);
        existing.RetentionCount = Math.Clamp(schedule.RetentionCount, 1, 100);
        existing.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = existing.IsEnabled
                ? $"Scheduled backups enabled every {existing.IntervalHours} hour(s)."
                : "Scheduled backups disabled."
        }, cancellationToken);
    }

    public async Task<BackupRun> CreateBackupAsync(
        string? createdBy,
        bool scheduled,
        CancellationToken cancellationToken,
        string? encryptionPassword = null)
    {
        Directory.CreateDirectory(appPaths.BackupDirectory);
        var encrypt = !string.IsNullOrWhiteSpace(encryptionPassword);

        var run = new BackupRun
        {
            Id = Guid.NewGuid(),
            StartedUtc = DateTimeOffset.UtcNow,
            Status = BackupRunStatus.Running,
            IsEncrypted = encrypt,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim()
        };

        await SaveRunAsync(run, cancellationToken);

        var tempDirectory = Path.Combine(appPaths.BackupDirectory, $".tmp-{run.Id:N}");
        var snapshotPath = Path.Combine(tempDirectory, "relay.db");
        var zipPath = Path.Combine(tempDirectory, "bundle.zip");
        var fileName = $"relaywright-backup-{run.StartedUtc:yyyyMMdd-HHmmss}-{run.Id:N}.{(encrypt ? "rwbak" : "zip")}";
        var backupPath = Path.Combine(appPaths.BackupDirectory, fileName);

        try
        {
            Directory.CreateDirectory(tempDirectory);
            await using var backupLock = await backupCoordinator.AcquireSpoolDeletionLockAsync(cancellationToken);

            await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
            var spoolPaths = await ReadSpoolPathsFromSnapshotAsync(snapshotPath, cancellationToken);
            var manifest = new BackupManifest
            {
                BackupId = run.Id,
                CreatedUtc = run.StartedUtc,
                Application = "Relaywright",
                DatabaseFile = "relay.db",
                SpoolFileCount = spoolPaths.Count,
                SpoolFiles = spoolPaths.ToList()
            };

            await CreateZipAsync(zipPath, snapshotPath, manifest, cancellationToken);
            if (encrypt)
            {
                await BackupEncryption.EncryptFileAsync(
                    zipPath,
                    backupPath,
                    encryptionPassword!,
                    cancellationToken);
            }
            else
            {
                File.Move(zipPath, backupPath, overwrite: true);
            }

            run.Status = BackupRunStatus.Succeeded;
            run.CompletedUtc = DateTimeOffset.UtcNow;
            run.FileName = fileName;
            run.IsEncrypted = encrypt;
            run.FileSizeBytes = new FileInfo(backupPath).Length;
            run.Message = scheduled
                ? "Scheduled backup completed."
                : encrypt
                    ? "Encrypted manual backup completed."
                    : "Manual backup completed.";
            await UpdateRunAsync(run, cancellationToken);

            logger.LogInformation(
                "Backup completed. BackupId={BackupId}; FileName={FileName}; SizeBytes={SizeBytes}; SpoolFileCount={SpoolFileCount}",
                run.Id,
                run.FileName,
                run.FileSizeBytes,
                spoolPaths.Count);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.System,
                Message = run.Message,
                Detail = run.FileName
            }, cancellationToken);

            var validation = await ValidateAsync(run.Id, cancellationToken, encryptionPassword);
            run.LastValidatedUtc = DateTimeOffset.UtcNow;
            run.LastValidationSucceeded = validation.Succeeded;
            run.LastValidationMessage = validation.Message;

            return run;
        }
        catch (Exception exception)
        {
            run.Status = BackupRunStatus.Failed;
            run.CompletedUtc = DateTimeOffset.UtcNow;
            run.Message = exception.Message;
            await UpdateRunAsync(run, cancellationToken);

            logger.LogError(exception, "Backup failed. BackupId={BackupId}", run.Id);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Error,
                Category = OperationalEventCategory.System,
                Message = "Backup failed.",
                Detail = exception.Message
            }, cancellationToken);

            return run;
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    public async Task<BackupOperationResult> ValidateAsync(
        Guid id,
        CancellationToken cancellationToken,
        string? encryptionPassword = null)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.BackupRuns.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null)
        {
            return new BackupOperationResult { Succeeded = false, Message = "Backup run not found." };
        }

        var path = GetBackupPath(run);
        var tempDirectory = Path.Combine(appPaths.BackupDirectory, $".validate-{run.Id:N}");

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new InvalidOperationException("Backup file was not found.");
            }

            Directory.CreateDirectory(tempDirectory);
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
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken)
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

            if (!archive.Entries.Any(x => x.FullName.StartsWith("keys/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Data Protection key entries are missing.");
            }

            run.LastValidatedUtc = DateTimeOffset.UtcNow;
            run.LastValidationSucceeded = true;
            run.LastValidationMessage = "Backup validation succeeded.";
            await dbContext.SaveChangesAsync(cancellationToken);

            return new BackupOperationResult { Succeeded = true, Message = run.LastValidationMessage };
        }
        catch (Exception exception)
        {
            run.LastValidatedUtc = DateTimeOffset.UtcNow;
            run.LastValidationSucceeded = false;
            run.LastValidationMessage = exception.Message;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new BackupOperationResult { Succeeded = false, Message = exception.Message };
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    public async Task<BackupOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.BackupRuns.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null)
        {
            return new BackupOperationResult { Succeeded = false, Message = "Backup run not found." };
        }

        var path = GetBackupPath(run);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }

        run.Status = BackupRunStatus.Deleted;
        run.Message = "Backup file deleted.";
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BackupOperationResult { Succeeded = true, Message = "Backup deleted." };
    }

    public async Task<string?> GetBackupPathAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.BackupRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return run is null ? null : GetBackupPath(run);
    }

    public async Task<BackupReadiness> GetReadinessAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var schedule = await dbContext.BackupScheduleStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? new BackupScheduleState();
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .Where(x => x.Status == BackupRunStatus.Succeeded && x.LastValidationSucceeded == true)
            .ToListAsync(cancellationToken);
        var latest = runs
            .OrderByDescending(x => x.LastValidatedUtc ?? x.CompletedUtc ?? x.StartedUtc)
            .FirstOrDefault();
        var staleAfterHours = schedule.IsEnabled
            ? Math.Max(24, schedule.IntervalHours * 2)
            : 168;
        var backupStorageBytes = DirectorySize(appPaths.BackupDirectory);

        if (latest is null)
        {
            return new BackupReadiness
            {
                IsReady = false,
                StaleAfterHours = staleAfterHours,
                BackupStorageBytes = backupStorageBytes,
                Message = "No validated backup is available."
            };
        }

        var goodUtc = latest.LastValidatedUtc ?? latest.CompletedUtc ?? latest.StartedUtc;
        var ageHours = Math.Max(0, (long)(now - goodUtc).TotalHours);
        var ready = ageHours <= staleAfterHours;

        return new BackupReadiness
        {
            IsReady = ready,
            BackupId = latest.Id,
            LastGoodBackupUtc = goodUtc,
            LastGoodBackupAgeHours = ageHours,
            StaleAfterHours = staleAfterHours,
            BackupStorageBytes = backupStorageBytes,
            Message = ready
                ? $"Last validated backup is {ageHours} hour(s) old."
                : $"Last validated backup is stale at {ageHours} hour(s) old."
        };
    }

    public async Task PruneByRetentionAsync(int retentionCount, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var retention = Math.Clamp(retentionCount, 1, 100);
        var succeededRuns = await dbContext.BackupRuns
            .Where(x => x.Status == BackupRunStatus.Succeeded)
            .ToListAsync(cancellationToken);
        var runs = succeededRuns
            .OrderByDescending(x => x.StartedUtc)
            .Skip(retention)
            .ToList();

        foreach (var run in runs)
        {
            var path = GetBackupPath(run);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }

            run.Status = BackupRunStatus.Deleted;
            run.Message = "Backup pruned by retention policy.";
        }

        if (runs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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

        AddDirectoryEntries(archive, appPaths.KeyRingDirectory, "keys");
        AddDirectoryEntries(archive, appPaths.CertificateDirectory, "certs");
        AddFileIfExists(archive, appPaths.AdminHttpsCertificateConfigurationPath, "admin-https-certificate.json");
        AddFileIfExists(archive, appPaths.AdminWebListenerConfigurationPath, "admin-web-listener.json");

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
    }

    private static async Task ValidateDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"QueuedMessages\";";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task SaveRunAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.BackupRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateRunAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.BackupRuns.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string? GetBackupPath(BackupRun run)
    {
        return string.IsNullOrWhiteSpace(run.FileName)
            ? null
            : Path.Combine(appPaths.BackupDirectory, Path.GetFileName(run.FileName));
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private static string ToZipSpoolEntry(string relativePath)
    {
        return $"spool/{relativePath.Replace('\\', '/')}";
    }

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

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
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
