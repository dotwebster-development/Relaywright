namespace Relaywright.Web.Data.Entities;

public sealed class BackupRun
{
    public Guid Id { get; set; }

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedUtc { get; set; }

    public BackupRunStatus Status { get; set; } = BackupRunStatus.Running;

    public string? FileName { get; set; }

    public bool IsEncrypted { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? CreatedBy { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset? LastValidatedUtc { get; set; }

    public bool? LastValidationSucceeded { get; set; }

    public string? LastValidationMessage { get; set; }
}
