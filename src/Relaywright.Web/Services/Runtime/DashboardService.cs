using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Updates;

namespace Relaywright.Web.Services.Runtime;

public sealed class DashboardService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRelayConfigurationService relayConfigurationService,
    IRuntimeStatusService runtimeStatusService,
    IDashboardMetricsService dashboardMetricsService,
    IDashboardReadinessService dashboardReadinessService,
    IAdminSecurityActivityService adminSecurityActivityService,
    IUpdateCheckService updateCheckService,
    DatabaseConfiguration databaseConfiguration) : IDashboardService
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        var runtimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        var metrics = await dashboardMetricsService.GetSnapshotAsync(configuration, cancellationToken);
        var readiness = await dashboardReadinessService.GetSnapshotAsync(
            configuration,
            metrics.BackupReadiness,
            cancellationToken);
        var updateStatus = await updateCheckService.GetStatusAsync(cancellationToken);
        var loadedUtc = DateTimeOffset.UtcNow;
        var suspiciousLogins = await adminSecurityActivityService.GetSuspiciousLoginSummaryAsync(
            loadedUtc,
            cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var todayUtc = new DateTimeOffset(loadedUtc.UtcDateTime.Date, TimeSpan.Zero);
        var pendingCount = await dbContext.QueuedMessages
            .CountAsync(x => x.Status == QueuedMessageStatus.Pending, cancellationToken);
        var retryCount = await dbContext.QueuedMessages
            .CountAsync(
                x => x.Status == QueuedMessageStatus.RetryScheduled
                    || x.Status == QueuedMessageStatus.InProgress,
                cancellationToken);
        var failedCount = await dbContext.QueuedMessages
            .CountAsync(
                x => x.Status == QueuedMessageStatus.Failed
                    || x.Status == QueuedMessageStatus.Expired,
                cancellationToken);
        var deliveredTodayCount = databaseConfiguration.IsSqlite
            ? await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE "Status" = {(int)QueuedMessageStatus.Delivered}
                        AND "DeliveredUtc" IS NOT NULL
                        AND "DeliveredUtc" >= {todayUtc}
                    """)
                .CountAsync(cancellationToken)
            : await dbContext.QueuedMessages.CountAsync(
                x => x.Status == QueuedMessageStatus.Delivered
                    && x.DeliveredUtc != null
                    && x.DeliveredUtc >= todayUtc,
                cancellationToken);
        var recentEvents = databaseConfiguration.IsSqlite
            ? await dbContext.OperationalEvents
                .FromSqlRaw("""
                    SELECT *
                    FROM "OperationalEvents"
                    ORDER BY "OccurredUtc" DESC
                    LIMIT 20
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
            : await dbContext.OperationalEvents
                .AsNoTracking()
                .OrderByDescending(x => x.OccurredUtc)
                .Take(20)
                .ToListAsync(cancellationToken);

        return new DashboardSnapshot
        {
            Configuration = configuration,
            RuntimeStatus = runtimeStatus,
            Metrics = metrics,
            Readiness = readiness,
            SuspiciousLogins = suspiciousLogins,
            UpdateStatus = updateStatus,
            PendingCount = pendingCount,
            RetryCount = retryCount,
            FailedCount = failedCount,
            DeliveredTodayCount = deliveredTodayCount,
            RecentEvents = recentEvents
        };
    }
}
