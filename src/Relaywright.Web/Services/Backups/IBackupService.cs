using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Backups;

public interface IBackupService
{
    Task<IReadOnlyList<BackupRun>> GetRunsAsync(CancellationToken cancellationToken);

    Task<BackupScheduleState> GetScheduleAsync(CancellationToken cancellationToken);

    Task SaveScheduleAsync(BackupScheduleState schedule, CancellationToken cancellationToken);

    Task<BackupRun> CreateBackupAsync(
        string? createdBy,
        bool scheduled,
        CancellationToken cancellationToken,
        string? encryptionPassword = null);

    Task<BackupOperationResult> ValidateAsync(
        Guid id,
        CancellationToken cancellationToken,
        string? encryptionPassword = null);

    Task<BackupOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<BackupReadiness> GetReadinessAsync(CancellationToken cancellationToken);

    Task<string?> GetBackupPathAsync(Guid id, CancellationToken cancellationToken);

    Task PruneByRetentionAsync(int retentionCount, CancellationToken cancellationToken);
}
