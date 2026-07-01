using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRuntimeStatusService runtimeStatusService,
    IRelayConfigurationService relayConfigurationService,
    IAdminHttpsCertificateService adminHttpsCertificateService,
    IAlertEmailNotifier emailNotifier,
    IOperationalEventService eventService,
    AppPaths appPaths,
    ILogger<AlertService> logger) : IAlertService
{
    public async Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.AlertRules
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlertRuleSummary>> GetRuleSummariesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var runtimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        var adminCertificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await dbContext.AlertRules
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var summaries = new List<AlertRuleSummary>(rules.Count);
        foreach (var rule in rules)
        {
            try
            {
                var evaluation = await EvaluateRuleAsync(
                    dbContext,
                    rule,
                    runtimeStatus,
                    adminCertificate,
                    now,
                    cancellationToken);

                summaries.Add(new AlertRuleSummary
                {
                    Rule = rule,
                    IsActive = evaluation.IsActive,
                    ObservedValue = evaluation.ObservedValue,
                    Threshold = rule.Threshold,
                    Message = evaluation.Message
                });
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Read-only alert summary failed. Key={AlertRuleKey}", rule.Key);
                summaries.Add(new AlertRuleSummary
                {
                    Rule = rule,
                    Threshold = rule.Threshold,
                    Message = "Current value could not be evaluated."
                });
            }
        }

        return summaries;
    }

    public async Task<IReadOnlyList<AlertResult>> GetRecentResultsAsync(int count, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var results = await dbContext.AlertResults
            .AsNoTracking()
            .Include(x => x.AlertRule)
            .ToListAsync(cancellationToken);

        return results
            .OrderByDescending(x => x.OccurredUtc)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.AlertRules.SingleOrDefaultAsync(x => x.Id == rule.Id, cancellationToken)
            ?? throw new InvalidOperationException("Alert rule not found.");

        existing.IsEnabled = rule.IsEnabled;
        existing.Threshold = Math.Max(0, rule.Threshold);
        existing.CooldownMinutes = Math.Max(1, rule.CooldownMinutes);
        existing.EmailRecipients = string.IsNullOrWhiteSpace(rule.EmailRecipients) ? null : Trim(rule.EmailRecipients, 1024);
        existing.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Alert,
            Message = $"Alert rule updated: {existing.DisplayName}."
        }, cancellationToken);
    }

    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var runtimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        var relayConfiguration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        var adminCertificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await dbContext.AlertRules
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules.Where(x => x.IsEnabled))
        {
            var evaluation = await EvaluateRuleAsync(
                dbContext,
                rule,
                runtimeStatus,
                adminCertificate,
                now,
                cancellationToken);

            await ApplyEvaluationAsync(dbContext, rule, evaluation, relayConfiguration, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AlertEvaluation> EvaluateRuleAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        RuntimeStatusSnapshot runtimeStatus,
        AdminHttpsCertificateConfiguration? adminCertificate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return rule.Key switch
        {
            "queue-depth" => await EvaluateQueueDepthAsync(dbContext, rule, cancellationToken),
            "oldest-active-message-minutes" => await EvaluateOldestActiveMessageAsync(dbContext, rule, now, cancellationToken),
            "failed-message-count" => await EvaluateFailedMessageCountAsync(dbContext, rule, cancellationToken),
            "listener-down" => EvaluateListenerDown(rule, runtimeStatus),
            "disk-free-mb" => EvaluateDiskFree(rule),
            "admin-certificate-expiry-days" => EvaluateAdminCertificateExpiry(rule, adminCertificate, now),
            "recent-upstream-failures" => await EvaluateRecentUpstreamFailuresAsync(dbContext, rule, now, cancellationToken),
            _ => new AlertEvaluation(false, 0, $"Unknown alert rule '{rule.Key}'.")
        };
    }

    private async Task<AlertEvaluation> EvaluateQueueDepthAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.QueuedMessages.CountAsync(
            x => x.Status == QueuedMessageStatus.Pending
                || x.Status == QueuedMessageStatus.RetryScheduled
                || x.Status == QueuedMessageStatus.InProgress,
            cancellationToken);
        return Above(rule, count, $"Active queue depth is {count}.");
    }

    private static async Task<AlertEvaluation> EvaluateOldestActiveMessageAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var acceptedTimes = await dbContext.QueuedMessages
            .AsNoTracking()
            .Where(x => x.Status == QueuedMessageStatus.Pending
                || x.Status == QueuedMessageStatus.RetryScheduled
                || x.Status == QueuedMessageStatus.InProgress)
            .Select(x => (DateTimeOffset?)x.AcceptedUtc)
            .ToListAsync(cancellationToken);
        var oldest = acceptedTimes
            .Where(x => x is not null)
            .Min();
        var ageMinutes = oldest is null ? 0 : Math.Max(0, (long)(now - oldest.Value).TotalMinutes);
        return Above(rule, ageMinutes, $"Oldest active message age is {ageMinutes} minute(s).");
    }

    private static async Task<AlertEvaluation> EvaluateFailedMessageCountAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.QueuedMessages.CountAsync(
            x => x.Status == QueuedMessageStatus.Failed || x.Status == QueuedMessageStatus.Expired,
            cancellationToken);
        return Above(rule, count, $"Failed or expired message count is {count}.");
    }

    private static AlertEvaluation EvaluateListenerDown(AlertRule rule, RuntimeStatusSnapshot runtimeStatus)
    {
        var down = string.IsNullOrWhiteSpace(runtimeStatus.SmtpListener.Status)
            || !string.Equals(runtimeStatus.SmtpListener.Status, "Running", StringComparison.OrdinalIgnoreCase);
        var observed = down ? 1 : 0;
        return Above(rule, observed, down ? "SMTP listener is not reporting a running state." : "SMTP listener is running.");
    }

    private AlertEvaluation EvaluateDiskFree(AlertRule rule)
    {
        var root = Path.GetPathRoot(appPaths.DataDirectory)
            ?? throw new InvalidOperationException("Unable to determine data volume.");
        var drive = new DriveInfo(root);
        var freeMb = drive.AvailableFreeSpace / 1024 / 1024;
        return new AlertEvaluation(
            freeMb < rule.Threshold,
            freeMb,
            $"Data volume free space is {freeMb} MB.");
    }

    private static AlertEvaluation EvaluateAdminCertificateExpiry(
        AlertRule rule,
        AdminHttpsCertificateConfiguration? adminCertificate,
        DateTimeOffset now)
    {
        if (adminCertificate?.NotAfterUtc is null)
        {
            return new AlertEvaluation(false, long.MaxValue, "No admin HTTPS certificate expiry is configured.");
        }

        var days = (long)Math.Ceiling((adminCertificate.NotAfterUtc.Value - now).TotalDays);
        return new AlertEvaluation(
            days <= rule.Threshold,
            days,
            $"Admin HTTPS certificate expires in {days} day(s).");
    }

    private static async Task<AlertEvaluation> EvaluateRecentUpstreamFailuresAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-1);
        var count = await dbContext.DeliveryAttempts
            .AsNoTracking()
            .Where(x => !x.Succeeded && x.CompletedUtc != null)
            .ToListAsync(cancellationToken);
        var recentCount = count.Count(x => x.CompletedUtc >= cutoff);
        return Above(rule, recentCount, $"Failed delivery attempts in the last hour: {recentCount}.");
    }

    private async Task ApplyEvaluationAsync(
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
            notification = await emailNotifier.SendAsync(rule, evaluation.Message, relayConfiguration, cancellationToken);
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

    private static AlertEvaluation Above(AlertRule rule, long observed, string message)
    {
        return new AlertEvaluation(observed >= rule.Threshold, observed, message);
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record AlertEvaluation(bool IsActive, long ObservedValue, string Message);
}
