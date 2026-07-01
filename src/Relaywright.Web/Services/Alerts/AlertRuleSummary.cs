using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertRuleSummary
{
    public AlertRule Rule { get; init; } = new();

    public bool IsActive { get; init; }

    public long ObservedValue { get; init; }

    public long Threshold { get; init; }

    public string Message { get; init; } = string.Empty;
}
