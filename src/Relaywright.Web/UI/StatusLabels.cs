using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.UI;

public static class StatusLabels
{
    public static string For(QueuedMessageStatus status) =>
        status switch
        {
            QueuedMessageStatus.InProgress => "In progress",
            QueuedMessageStatus.RetryScheduled => "Retry scheduled",
            _ => status.ToString()
        };

    public static string For(DiagnosticStageStatus status) =>
        status.ToString();

    public static string For(BackupRunStatus status) =>
        status.ToString();

    public static string ForRuntime(string? status) =>
        status switch
        {
            null or "" => "Unknown",
            "Faulted" => "Failed",
            _ => status
        };
}
