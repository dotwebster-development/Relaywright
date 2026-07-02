namespace Relaywright.Web.Services.Backups;

public interface IBackupRestoreService
{
    Task<BackupRestoreResult> StageRestoreAsync(
        IFormFile backupFile,
        string? encryptionPassword,
        CancellationToken cancellationToken);
}
