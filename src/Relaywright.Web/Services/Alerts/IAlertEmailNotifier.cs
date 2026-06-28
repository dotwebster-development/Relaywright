using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Alerts;

public interface IAlertEmailNotifier
{
    Task<AlertNotificationResult> SendAsync(
        AlertRule rule,
        string message,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken);
}
