using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IBackupCoordinator backupCoordinator,
    IOperationalEventService eventService,
    AppPaths appPaths,
    DatabaseConfiguration databaseConfiguration,
    ILogger<BackupService> logger,
    BackupArchiveService? backupArchiveService = null,
    BackupFileStore? backupFileStore = null,
    BackupRunRepository? backupRunRepository = null,
    BackupScheduleRepository? backupScheduleRepository = null,
    TimeProvider? timeProvider = null) : IBackupService
{
    private readonly BackupArchiveService archiveService = backupArchiveService ?? new BackupArchiveService(appPaths);
    private readonly BackupFileStore fileStore = backupFileStore ?? new BackupFileStore(
        appPaths,
        new PhysicalBackupFileSystem(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupFileStore>.Instance);
    private readonly BackupRunRepository runRepository = backupRunRepository ?? new BackupRunRepository(dbContextFactory);
    private readonly BackupScheduleRepository scheduleRepository =
        backupScheduleRepository ?? new BackupScheduleRepository(dbContextFactory);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<BackupRun>> GetRunsAsync(CancellationToken cancellationToken)
    {
        return await runRepository.GetRecentAsync(100, cancellationToken);
    }

    public async Task<BackupScheduleState> GetScheduleAsync(CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsExternalServer)
        {
            return new BackupScheduleState
            {
                IsEnabled = false
            };
        }

        return await scheduleRepository.GetOrCreateAsync(cancellationToken);
    }

    public async Task SaveScheduleAsync(BackupScheduleState schedule, CancellationToken cancellationToken)
    {
        ValidateSchedule(schedule);

        if (databaseConfiguration.IsExternalServer)
        {
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.System,
                Message = "Relaywright scheduled backups are disabled because the database is managed externally.",
                Detail = $"{databaseConfiguration.Provider} database backups must be handled with database platform tooling."
            }, cancellationToken);
            return;
        }

        schedule.UpdatedUtc = clock.GetUtcNow();
        await scheduleRepository.SaveAsync(schedule, cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = schedule.IsEnabled
                ? $"Scheduled backups enabled every {schedule.IntervalHours} hour(s)."
                : "Scheduled backups disabled."
        }, cancellationToken);
    }

    public async Task<BackupRun> CreateBackupAsync(
        string? createdBy,
        bool scheduled,
        CancellationToken cancellationToken,
        string? encryptionPassword = null)
    {
        ValidatePassword(encryptionPassword, "Backup encryption password");

        var encrypt = !string.IsNullOrWhiteSpace(encryptionPassword);

        var run = new BackupRun
        {
            Id = Guid.NewGuid(),
            StartedUtc = clock.GetUtcNow(),
            Status = BackupRunStatus.Running,
            IsEncrypted = encrypt,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim()
        };

        if (databaseConfiguration.IsExternalServer)
        {
            run.Status = BackupRunStatus.Failed;
            run.CompletedUtc = clock.GetUtcNow();
            run.Message = $"Built-in backup is only available for SQLite. Back up the {databaseConfiguration.Provider} database with database platform tooling.";
            await SaveRunAsync(run, cancellationToken);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Warning,
                Category = OperationalEventCategory.System,
                Message = "Backup was not created because the database is managed externally.",
                Detail = run.Message
            }, cancellationToken);

            return run;
        }

        fileStore.EnsureCreated();
        await SaveRunAsync(run, cancellationToken);

        var tempDirectory = fileStore.GetWorkingDirectory("tmp", run.Id);
        var fileName = fileStore.CreateBackupFileName(run, encrypt);
        var backupPath = fileStore.GetPath(fileName);

        try
        {
            fileStore.CreateWorkingDirectory(tempDirectory);
            await using var backupLock = await backupCoordinator.AcquireSpoolDeletionLockAsync(cancellationToken);

            var spoolFileCount = await archiveService.CreateAsync(
                run.Id,
                run.StartedUtc,
                tempDirectory,
                backupPath,
                encryptionPassword,
                cancellationToken);

            run.Status = BackupRunStatus.Succeeded;
            run.CompletedUtc = clock.GetUtcNow();
            run.FileName = fileName;
            run.IsEncrypted = encrypt;
            run.FileSizeBytes = fileStore.GetFileSize(backupPath);
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
                spoolFileCount);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.System,
                Message = run.Message,
                Detail = run.FileName
            }, cancellationToken);

            var validation = await ValidateAsync(run.Id, cancellationToken, encryptionPassword);
            run.LastValidatedUtc = clock.GetUtcNow();
            run.LastValidationSucceeded = validation.Succeeded;
            run.LastValidationMessage = validation.Message;

            return run;
        }
        catch (Exception exception)
        {
            run.Status = BackupRunStatus.Failed;
            run.CompletedUtc = clock.GetUtcNow();
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
            fileStore.DeleteWorkingDirectoryBestEffort(tempDirectory, run.Id, "create");
        }
    }

    public async Task<BackupOperationResult> ValidateAsync(
        Guid id,
        CancellationToken cancellationToken,
        string? encryptionPassword = null)
    {
        ValidatePassword(encryptionPassword, "Backup validation password");

        var run = await runRepository.FindAsync(id, cancellationToken);
        if (run is null)
        {
            return new BackupOperationResult { Succeeded = false, Message = "Backup run not found." };
        }

        var path = fileStore.GetPath(run);
        var tempDirectory = fileStore.GetWorkingDirectory("validate", run.Id);

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !fileStore.Exists(path))
            {
                throw new InvalidOperationException("Backup file was not found.");
            }

            fileStore.CreateWorkingDirectory(tempDirectory);
            await archiveService.ValidateAsync(path, tempDirectory, encryptionPassword, cancellationToken);

            run.LastValidatedUtc = clock.GetUtcNow();
            run.LastValidationSucceeded = true;
            run.LastValidationMessage = "Backup validation succeeded.";
            await runRepository.UpdateAsync(run, cancellationToken);

            return new BackupOperationResult { Succeeded = true, Message = run.LastValidationMessage };
        }
        catch (Exception exception)
        {
            run.LastValidatedUtc = clock.GetUtcNow();
            run.LastValidationSucceeded = false;
            run.LastValidationMessage = exception.Message;
            await runRepository.UpdateAsync(run, cancellationToken);

            return new BackupOperationResult { Succeeded = false, Message = exception.Message };
        }
        finally
        {
            fileStore.DeleteWorkingDirectoryBestEffort(tempDirectory, run.Id, "validate");
        }
    }

    public async Task<BackupOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await runRepository.FindAsync(id, cancellationToken);
        if (run is null)
        {
            return new BackupOperationResult { Succeeded = false, Message = "Backup run not found." };
        }

        var path = fileStore.GetPath(run);
        fileStore.DeleteFileIfExists(path);

        run.Status = BackupRunStatus.Deleted;
        run.Message = "Backup file deleted.";
        await runRepository.UpdateAsync(run, cancellationToken);

        return new BackupOperationResult { Succeeded = true, Message = "Backup deleted." };
    }

    public async Task<string?> GetBackupPathAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await runRepository.FindAsync(id, cancellationToken);
        return run is null ? null : fileStore.GetPath(run);
    }

    public async Task<BackupReadiness> GetReadinessAsync(CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsExternalServer)
        {
            return new BackupReadiness
            {
                IsReady = true,
                BackupStorageBytes = fileStore.GetStorageBytes(),
                Message = $"{databaseConfiguration.Provider} database backup is managed outside Relaywright."
            };
        }

        var now = clock.GetUtcNow();
        var schedule = await scheduleRepository.GetOrCreateAsync(cancellationToken);
        var latest = await runRepository.FindLatestValidatedAsync(cancellationToken);
        var staleAfterHours = schedule.IsEnabled
            ? Math.Max(24, schedule.IntervalHours * 2)
            : 168;
        var backupStorageBytes = fileStore.GetStorageBytes();

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
        if (databaseConfiguration.IsExternalServer)
        {
            return;
        }

        var runs = await runRepository.GetRetentionCandidatesAsync(retentionCount, cancellationToken);

        foreach (var run in runs)
        {
            var path = fileStore.GetPath(run);
            fileStore.DeleteFileIfExists(path);

            run.Status = BackupRunStatus.Deleted;
            run.Message = "Backup pruned by retention policy.";
        }

        await runRepository.UpdateRangeAsync(runs, cancellationToken);
    }

    private async Task SaveRunAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await runRepository.AddAsync(run, cancellationToken);
    }

    private async Task UpdateRunAsync(BackupRun run, CancellationToken cancellationToken)
    {
        await runRepository.UpdateAsync(run, cancellationToken);
    }

    private static void ValidateSchedule(BackupScheduleState schedule)
    {
        if (schedule.IntervalHours is < 1 or > 720)
        {
            throw new InvalidOperationException("Backup interval must be between 1 and 720 hours.");
        }

        if (schedule.RetentionCount is < 1 or > 100)
        {
            throw new InvalidOperationException("Backup retention count must be between 1 and 100.");
        }
    }

    private static void ValidatePassword(string? value, string label)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > ValidationLimits.MaximumSecretLength)
        {
            throw new InvalidOperationException(ValidationMessages.MaximumLength(label, ValidationLimits.MaximumSecretLength));
        }

        if (ValidationRules.ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _))
        {
            throw new InvalidOperationException(ValidationMessages.UnsupportedControlCharacter(label));
        }
    }

}
