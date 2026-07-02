namespace Relaywright.Web.Data.Entities;

public sealed class AlertRule
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public long Threshold { get; set; }

    public int CooldownMinutes { get; set; } = 60;

    public string? EmailRecipients { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastTriggeredUtc { get; set; }

    public DateTimeOffset? LastResolvedUtc { get; set; }

    public DateTimeOffset? LastNotificationUtc { get; set; }

    public bool? LastNotificationSucceeded { get; set; }

    public string? LastNotificationMessage { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AlertResult> Results { get; set; } = new List<AlertResult>();
}
