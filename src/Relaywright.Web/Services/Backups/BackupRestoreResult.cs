namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreResult
{
    public bool Succeeded { get; init; }

    public bool RestartRequired { get; init; }

    public string Message { get; init; } = string.Empty;

    public BackupRestoreSummary? Summary { get; init; }
}
