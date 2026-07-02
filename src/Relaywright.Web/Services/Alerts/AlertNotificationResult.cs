namespace Relaywright.Web.Services.Alerts;

public sealed class AlertNotificationResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;
}
