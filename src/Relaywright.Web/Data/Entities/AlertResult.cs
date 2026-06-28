namespace Relaywright.Web.Data.Entities;

public sealed class AlertResult
{
    public long Id { get; set; }

    public int AlertRuleId { get; set; }

    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; }

    public long ObservedValue { get; set; }

    public long Threshold { get; set; }

    public string Message { get; set; } = string.Empty;

    public bool? NotificationSucceeded { get; set; }

    public string? NotificationMessage { get; set; }

    public AlertRule AlertRule { get; set; } = null!;
}
