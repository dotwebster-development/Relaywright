using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Backups;

namespace Relaywright.Web.Services.Runtime;

public sealed class DashboardMetricsService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AppPaths appPaths,
    DatabaseConfiguration databaseConfiguration,
    IBackupService backupService,
    IOutboundRouteProbe outboundRouteProbe,
    ILogger<DashboardMetricsService> logger) : IDashboardMetricsService
{
    public async Task<DashboardMetricsSnapshot> GetSnapshotAsync(
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var messageTimes = await dbContext.QueuedMessages
            .AsNoTracking()
            .Select(x => new { x.AcceptedUtc, x.DeliveredUtc })
            .ToListAsync(cancellationToken);
        var attemptTimes = await dbContext.DeliveryAttempts
            .AsNoTracking()
            .Where(x => !x.Succeeded && x.CompletedUtc != null)
            .Select(x => x.CompletedUtc)
            .ToListAsync(cancellationToken);
        var acceptedLast24Hours = messageTimes.Count(x => x.AcceptedUtc >= cutoff);
        var deliveredLast24Hours = messageTimes.Count(x => x.DeliveredUtc is not null && x.DeliveredUtc >= cutoff);
        var failedLast24Hours = attemptTimes.Count(x => x is not null && x >= cutoff);
        var retryingCount = await dbContext.QueuedMessages
            .CountAsync(x => x.Status == QueuedMessageStatus.RetryScheduled || x.Status == QueuedMessageStatus.InProgress, cancellationToken);

        var activeAcceptedTimes = await dbContext.QueuedMessages
            .AsNoTracking()
            .Where(x => x.Status == QueuedMessageStatus.Pending
                || x.Status == QueuedMessageStatus.RetryScheduled
                || x.Status == QueuedMessageStatus.InProgress)
            .Select(x => (DateTimeOffset?)x.AcceptedUtc)
            .ToListAsync(cancellationToken);
        var oldestAccepted = activeAcceptedTimes.Min();

        var failures = await dbContext.DeliveryAttempts
            .AsNoTracking()
            .Where(x => !x.Succeeded && x.CompletedUtc != null)
            .ToListAsync(cancellationToken);
        var lastFailure = failures
            .OrderByDescending(x => x.CompletedUtc)
            .FirstOrDefault();

        var route = await outboundRouteProbe.ProbeAsync(
            configuration.UpstreamHost,
            configuration.UpstreamPort,
            cancellationToken);
        var backupReadiness = await backupService.GetReadinessAsync(cancellationToken);

        return new DashboardMetricsSnapshot
        {
            AcceptedLast24Hours = acceptedLast24Hours,
            DeliveredLast24Hours = deliveredLast24Hours,
            FailedLast24Hours = failedLast24Hours,
            RetryingCount = retryingCount,
            OldestActiveAgeMinutes = oldestAccepted is null
                ? null
                : Math.Max(0, (long)(now - oldestAccepted.Value).TotalMinutes),
            LastUpstreamFailureUtc = lastFailure?.CompletedUtc,
            LastUpstreamFailureMessage = lastFailure is null
                ? null
                : FirstNonEmpty(lastFailure.ResponseText, lastFailure.ExceptionMessage, lastFailure.FailureCategory.ToString()),
            DatabaseSizeBytes = databaseConfiguration.IsSqlite ? FileSize(appPaths.DatabasePath) : 0,
            IsDatabaseExternallyManaged = databaseConfiguration.IsExternalServer,
            DatabaseDescription = databaseConfiguration.IsSqlite
                ? "SQLite database file"
                : $"{databaseConfiguration.Provider} managed externally",
            SpoolSizeBytes = DirectorySize(appPaths.SpoolRootDirectory),
            BackupSizeBytes = DirectorySize(appPaths.BackupDirectory),
            OutboundRoute = route,
            BackupReadiness = backupReadiness
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static long FileSize(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private long DirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Directory size calculation failed. Path={Path}", path);
            return 0;
        }
    }
}
