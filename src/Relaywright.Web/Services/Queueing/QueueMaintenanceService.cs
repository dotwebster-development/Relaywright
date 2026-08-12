using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Backups;

namespace Relaywright.Web.Services.Queueing;

public sealed class QueueMaintenanceService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMessageSpoolService spoolService,
    IBackupCoordinator backupCoordinator,
    DatabaseConfiguration databaseConfiguration,
    ILogger<QueueMaintenanceService> logger,
    TimeProvider? timeProvider = null) : IQueueMaintenanceService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<int> CleanupAsync(
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var now = clock.GetUtcNow();
        var deliveredCutoff = now.AddHours(-configuration.DeliveredRetentionHours);
        var failedCutoff = now.AddHours(-configuration.FailedRetentionHours);
        var eventCutoff = now.AddHours(-configuration.EventRetentionHours);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deliveredMessages = await GetDeliveredMessagesToDeleteAsync(dbContext, deliveredCutoff, cancellationToken);
        var terminalMessages = await GetTerminalMessagesToDeleteAsync(dbContext, failedCutoff, cancellationToken);
        var messagesToDelete = deliveredMessages
            .Concat(terminalMessages)
            .DistinctBy(x => x.Id)
            .ToList();
        var spoolPathsToDelete = messagesToDelete
            .Select(x => x.SpoolFileRelativePath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        dbContext.QueuedMessages.RemoveRange(messagesToDelete);

        var expirableMessages = await GetExpirableMessagesAsync(dbContext, now, cancellationToken);
        foreach (var message in expirableMessages)
        {
            QueueStateTransitionPolicy.EnsureAllowed(message.Status, QueuedMessageStatus.Expired);
            message.Status = QueuedMessageStatus.Expired;
            message.LastAttemptCompletedUtc ??= now;
            message.LastError = "Message expired before successful delivery.";
        }

        var eventsToDelete = await GetEventsToDeleteAsync(dbContext, eventCutoff, cancellationToken);
        dbContext.OperationalEvents.RemoveRange(eventsToDelete);
        await dbContext.SaveChangesAsync(cancellationToken);

        await using var backupLock = await backupCoordinator.AcquireSpoolDeletionLockAsync(cancellationToken);
        foreach (var spoolPath in spoolPathsToDelete)
        {
            await spoolService.DeleteIfExistsAsync(spoolPath, cancellationToken);
        }

        logger.LogInformation(
            "Queue cleanup completed. DeletedMessages={DeletedMessages}; DeletedEvents={DeletedEvents}; ExpiredActiveMessages={ExpiredActiveMessages}; DeletedSpoolFiles={DeletedSpoolFiles}; DeliveredCutoffUtc={DeliveredCutoffUtc}; FailedCutoffUtc={FailedCutoffUtc}; EventCutoffUtc={EventCutoffUtc}",
            messagesToDelete.Count,
            eventsToDelete.Count,
            expirableMessages.Count,
            spoolPathsToDelete.Count,
            deliveredCutoff,
            failedCutoff,
            eventCutoff);

        return messagesToDelete.Count + eventsToDelete.Count;
    }

    private async Task<List<QueuedMessage>> GetDeliveredMessagesToDeleteAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsSqlite)
        {
            return await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE "Status" = {(int)QueuedMessageStatus.Delivered}
                        AND "DeliveredUtc" IS NOT NULL
                        AND "DeliveredUtc" <= {cutoff}
                    """)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.QueuedMessages
            .Where(x =>
                x.Status == QueuedMessageStatus.Delivered
                && x.DeliveredUtc != null
                && x.DeliveredUtc <= cutoff)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<QueuedMessage>> GetTerminalMessagesToDeleteAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsSqlite)
        {
            return await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE ("Status" = {(int)QueuedMessageStatus.Failed}
                            OR "Status" = {(int)QueuedMessageStatus.Expired})
                        AND "LastAttemptCompletedUtc" IS NOT NULL
                        AND "LastAttemptCompletedUtc" <= {cutoff}
                    """)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.QueuedMessages
            .Where(x =>
                (x.Status == QueuedMessageStatus.Failed || x.Status == QueuedMessageStatus.Expired)
                && x.LastAttemptCompletedUtc != null
                && x.LastAttemptCompletedUtc <= cutoff)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<QueuedMessage>> GetExpirableMessagesAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsSqlite)
        {
            return await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE ("Status" = {(int)QueuedMessageStatus.Pending}
                            OR "Status" = {(int)QueuedMessageStatus.InProgress}
                            OR "Status" = {(int)QueuedMessageStatus.RetryScheduled})
                        AND "ExpiresUtc" <= {now}
                    """)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.QueuedMessages
            .Where(x =>
                (x.Status == QueuedMessageStatus.Pending
                    || x.Status == QueuedMessageStatus.InProgress
                    || x.Status == QueuedMessageStatus.RetryScheduled)
                && x.ExpiresUtc <= now)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<OperationalEvent>> GetEventsToDeleteAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsSqlite)
        {
            return await dbContext.OperationalEvents
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "OperationalEvents"
                    WHERE "OccurredUtc" <= {cutoff}
                    """)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.OperationalEvents
            .Where(x => x.OccurredUtc <= cutoff)
            .ToListAsync(cancellationToken);
    }
}
