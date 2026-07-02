namespace Relaywright.Web.Services.Backups;

public sealed class BackupOperationResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;
}
