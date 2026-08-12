using System.Text.Json;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreService(
    AppPaths appPaths,
    DatabaseConfiguration databaseConfiguration,
    ILogger<BackupRestoreService> logger,
    BackupRestoreArchiveExtractor? archiveExtractor = null,
    RestoredBackupValidator? restoredBackupValidator = null) : IBackupRestoreService
{
    private const long MaxRestoreUploadBytes = 5L * 1024 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BackupRestoreArchiveExtractor archiveExtractor =
        archiveExtractor ?? new BackupRestoreArchiveExtractor();
    private readonly RestoredBackupValidator restoredBackupValidator =
        restoredBackupValidator ?? new RestoredBackupValidator();

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

        if (!ValidationRules.HasAllowedExtension(backupFile.FileName, [".zip", ".rwbak"]))
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = "Backup file must use a .zip or .rwbak extension."
            };
        }

        if (!IsValidPassword(encryptionPassword, out var passwordMessage))
        {
            return new BackupRestoreResult
            {
                Succeeded = false,
                Message = passwordMessage
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

            await archiveExtractor.ExtractAsync(archivePath, stagingDirectory, cancellationToken);

            var stagedDatabasePath = Path.Combine(stagingDirectory, "relay.db");
            await restoredBackupValidator.ValidateDatabaseAsync(stagedDatabasePath, cancellationToken);
            await BackupCredentialSanitizer.SanitizeAsync(stagedDatabasePath, cancellationToken);
            await restoredBackupValidator.ValidateTrustedNetworksAsync(
                stagedDatabasePath,
                cancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, "restore.json"),
                JsonSerializer.Serialize(new
                {
                    stagedUtc = DateTimeOffset.UtcNow
                }, JsonOptions),
                cancellationToken);

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
                Message = "Restore staged. Restart Relaywright to apply the restored database, spool, certificates, and listener settings. Admin accounts, protected relay secrets, Data Protection keys, and admin HTTPS certificate passwords are not restored."
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

    private static bool IsValidPassword(string? value, out string message)
    {
        message = string.Empty;
        if (value is null)
        {
            return true;
        }

        if (value.Length > ValidationLimits.MaximumSecretLength)
        {
            message = ValidationMessages.MaximumLength(
                "Restore encryption password",
                ValidationLimits.MaximumSecretLength);
            return false;
        }

        if (ValidationRules.ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _))
        {
            message = ValidationMessages.UnsupportedControlCharacter("Restore encryption password");
            return false;
        }

        return true;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
