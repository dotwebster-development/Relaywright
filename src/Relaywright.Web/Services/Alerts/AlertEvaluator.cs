using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertEvaluator(AppPaths appPaths)
{
    public async Task<AlertEvaluation> EvaluateAsync(
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
            "oldest-active-message-minutes" => await EvaluateOldestActiveMessageAsync(
                dbContext,
                rule,
                now,
                cancellationToken),
            "failed-message-count" => await EvaluateFailedMessageCountAsync(dbContext, rule, cancellationToken),
            "listener-down" => EvaluateListenerDown(rule, runtimeStatus),
            "disk-free-mb" => EvaluateDiskFree(rule),
            "admin-certificate-expiry-days" => EvaluateAdminCertificateExpiry(rule, adminCertificate, now),
            "recent-upstream-failures" => await EvaluateRecentUpstreamFailuresAsync(
                dbContext,
                rule,
                now,
                cancellationToken),
            _ => new AlertEvaluation(false, 0, $"Unknown alert rule '{rule.Key}'.")
        };
    }

    private static async Task<AlertEvaluation> EvaluateQueueDepthAsync(
        ApplicationDbContext dbContext,
        AlertRule rule,
        CancellationToken cancellationToken)
    {
        var count = await dbContext.QueuedMessages.CountAsync(
            message => message.Status == QueuedMessageStatus.Pending
                || message.Status == QueuedMessageStatus.RetryScheduled
                || message.Status == QueuedMessageStatus.InProgress,
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
            .Where(message => message.Status == QueuedMessageStatus.Pending
                || message.Status == QueuedMessageStatus.RetryScheduled
                || message.Status == QueuedMessageStatus.InProgress)
            .Select(message => (DateTimeOffset?)message.AcceptedUtc)
            .ToListAsync(cancellationToken);
        var oldest = acceptedTimes
            .Where(acceptedUtc => acceptedUtc is not null)
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
            message => message.Status == QueuedMessageStatus.Failed
                || message.Status == QueuedMessageStatus.Expired,
            cancellationToken);
        return Above(rule, count, $"Failed or expired message count is {count}.");
    }

    private static AlertEvaluation EvaluateListenerDown(
        AlertRule rule,
        RuntimeStatusSnapshot runtimeStatus)
    {
        var down = string.IsNullOrWhiteSpace(runtimeStatus.SmtpListener.Status)
            || !string.Equals(
                runtimeStatus.SmtpListener.Status,
                "Running",
                StringComparison.OrdinalIgnoreCase);
        var observed = down ? 1 : 0;
        return Above(
            rule,
            observed,
            down ? "SMTP listener is not reporting a running state." : "SMTP listener is running.");
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
            return new AlertEvaluation(
                false,
                long.MaxValue,
                "No admin HTTPS certificate expiry is configured.");
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
        var attempts = await dbContext.DeliveryAttempts
            .AsNoTracking()
            .Where(attempt => !attempt.Succeeded && attempt.CompletedUtc != null)
            .ToListAsync(cancellationToken);
        var recentCount = attempts.Count(attempt => attempt.CompletedUtc >= cutoff);
        return Above(rule, recentCount, $"Failed delivery attempts in the last hour: {recentCount}.");
    }

    private static AlertEvaluation Above(AlertRule rule, long observed, string message)
    {
        return new AlertEvaluation(observed >= rule.Threshold, observed, message);
    }
}
