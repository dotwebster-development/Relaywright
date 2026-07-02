namespace Relaywright.Web.Services.Backups;

public sealed class BackupReadiness
{
    public bool IsReady { get; init; }

    public Guid? BackupId { get; init; }

    public DateTimeOffset? LastGoodBackupUtc { get; init; }

    public long? LastGoodBackupAgeHours { get; init; }

    public int StaleAfterHours { get; init; }

    public long BackupStorageBytes { get; init; }

    public string Message { get; init; } = "No validated backup is available.";
}
