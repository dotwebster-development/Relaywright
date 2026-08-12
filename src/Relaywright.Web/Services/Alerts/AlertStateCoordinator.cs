using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertStateCoordinator(
    IAlertEmailNotifier emailNotifier,
    IOperationalEventService eventService,
    ILogger<AlertStateCoordinator> logger)
{
    public async Task ApplyAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        AlertEvaluation evaluation,
        RelayConfigurationSnapshot relayConfiguration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var wasActive = rule.IsActive;
        var shouldNotify = evaluation.IsActive
            && (!wasActive
                || rule.LastNotificationUtc is null
                || rule.LastNotificationUtc.Value.AddMinutes(rule.CooldownMinutes) <= now);

        AlertNotificationResult? notification = null;
        if (shouldNotify)
        {
            notification = await emailNotifier.SendAsync(
                rule,
                evaluation.Message,
                relayConfiguration,
                cancellationToken);
            rule.LastNotificationUtc = now;
            rule.LastNotificationSucceeded = notification.Succeeded;
            rule.LastNotificationMessage = Trim(notification.Message, 2048);
        }

        if (evaluation.IsActive && !wasActive)
        {
            rule.LastTriggeredUtc = now;
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Warning,
                Category = OperationalEventCategory.Alert,
                Message = $"Alert triggered: {rule.DisplayName}.",
                Detail = evaluation.Message
            }, cancellationToken);
        }
        else if (!evaluation.IsActive && wasActive)
        {
            rule.LastResolvedUtc = now;
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.Alert,
                Message = $"Alert resolved: {rule.DisplayName}.",
                Detail = evaluation.Message
            }, cancellationToken);
        }

        rule.IsActive = evaluation.IsActive;
        rule.UpdatedUtc = now;

        if (evaluation.IsActive != wasActive || notification is not null)
        {
            dbContext.AlertResults.Add(new AlertResult
            {
                AlertRuleId = rule.Id,
                OccurredUtc = now,
                IsActive = evaluation.IsActive,
                ObservedValue = evaluation.ObservedValue,
                Threshold = rule.Threshold,
                Message = evaluation.Message,
                NotificationSucceeded = notification?.Succeeded,
                NotificationMessage = notification is null ? null : Trim(notification.Message, 2048)
            });
        }

        logger.LogDebug(
            "Alert evaluated. Key={AlertRuleKey}; Active={Active}; ObservedValue={ObservedValue}; Threshold={Threshold}; NotificationAttempted={NotificationAttempted}",
            rule.Key,
            evaluation.IsActive,
            evaluation.ObservedValue,
            rule.Threshold,
            notification is not null);
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
