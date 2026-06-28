namespace Relaywright.Web.Data.Entities;

public sealed class BackupScheduleState
{
    public int Id { get; set; } = 1;

    public bool IsEnabled { get; set; }

    public int IntervalHours { get; set; } = 24;

    public int RetentionCount { get; set; } = 7;

    public DateTimeOffset? LastRunUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
