using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Queueing;

public sealed class QueueClaimService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DatabaseConfiguration databaseConfiguration,
    IOptions<QueueProcessingOptions> options,
    ILogger<QueueClaimService> logger,
    TimeProvider? timeProvider = null)
{
    private const int ClaimRetryLimit = 3;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan staleClaimThreshold = options.Value.GetStaleClaimThreshold();

    private sealed record QueueCandidate(Guid Id, QueuedMessageStatus Status, int AttemptCount);

    public async Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        for (var claimAttempt = 1; claimAttempt <= ClaimRetryLimit; claimAttempt++)
        {
            var now = clock.GetUtcNow();
            var staleThreshold = now.Subtract(staleClaimThreshold);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var candidate = await SelectClaimCandidateAsync(dbContext, now, staleThreshold, cancellationToken);

            if (candidate is null)
            {
                logger.LogDebug("No eligible queue work found.");
                return null;
            }

            var attemptNumber = candidate.AttemptCount + 1;
            QueueStateTransitionPolicy.EnsureAllowed(candidate.Status, QueuedMessageStatus.InProgress);
            var updated = await dbContext.QueuedMessages
                .Where(x =>
                    x.Id == candidate.Id
                    && x.Status == candidate.Status
                    && x.AttemptCount == candidate.AttemptCount)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, QueuedMessageStatus.InProgress)
                    .SetProperty(x => x.AttemptCount, attemptNumber)
                    .SetProperty(x => x.LastAttemptStartedUtc, (DateTimeOffset?)now),
                    cancellationToken);

            if (updated == 0)
            {
                logger.LogDebug(
                    "Queue claim candidate was already claimed by another worker. MessageId={MessageId}; ClaimAttempt={ClaimAttempt}",
                    candidate.Id,
                    claimAttempt);
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var deliveryAttempt = new DeliveryAttempt
            {
                QueuedMessageId = candidate.Id,
                AttemptNumber = attemptNumber,
                StartedUtc = now
            };

            dbContext.DeliveryAttempts.Add(deliveryAttempt);
            await dbContext.SaveChangesAsync(cancellationToken);

            var queuedMessage = await dbContext.QueuedMessages
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Recipients)
                .SingleAsync(x => x.Id == candidate.Id, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Claimed queued message for delivery. MessageId={MessageId}; PreviousStatus={PreviousStatus}; AttemptNumber={AttemptNumber}; RecipientCount={RecipientCount}; RemoteIp={RemoteIp}; ExpiresUtc={ExpiresUtc}; SpoolPath={SpoolPath}",
                queuedMessage.Id,
                candidate.Status,
                attemptNumber,
                queuedMessage.Recipients.Count,
                queuedMessage.RemoteIpAddress,
                queuedMessage.ExpiresUtc,
                queuedMessage.SpoolFileRelativePath);

            return new DeliveryWorkItem
            {
                MessageId = queuedMessage.Id,
                DeliveryAttemptId = deliveryAttempt.Id,
                AttemptNumber = attemptNumber,
                CorrelationId = queuedMessage.CorrelationId,
                EnvelopeFrom = queuedMessage.EnvelopeFrom,
                Recipients = queuedMessage.Recipients.Select(x => x.RecipientAddress).ToArray(),
                SpoolFileRelativePath = queuedMessage.SpoolFileRelativePath,
                RemoteIpAddress = queuedMessage.RemoteIpAddress,
                ExpiresUtc = queuedMessage.ExpiresUtc
            };
        }

        logger.LogDebug("No queue work was claimed after retrying contended candidates.");
        return null;
    }

    private async Task<QueueCandidate?> SelectClaimCandidateAsync(
        ApplicationDbContext dbContext,
        DateTimeOffset now,
        DateTimeOffset staleThreshold,
        CancellationToken cancellationToken)
    {
        if (databaseConfiguration.IsSqlite)
        {
            return await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE (("Status" = {(int)QueuedMessageStatus.Pending} OR "Status" = {(int)QueuedMessageStatus.RetryScheduled})
                        AND "NextAttemptAtUtc" <= {now})
                        OR ("Status" = {(int)QueuedMessageStatus.InProgress}
                            AND "LastAttemptStartedUtc" IS NOT NULL
                            AND "LastAttemptStartedUtc" <= {staleThreshold})
                    ORDER BY CASE
                        WHEN "Status" = {(int)QueuedMessageStatus.InProgress} THEN "LastAttemptStartedUtc"
                        ELSE "NextAttemptAtUtc"
                    END ASC, "CreatedUtc" ASC
                    LIMIT 1
                    """)
                .AsNoTracking()
                .Select(x => new QueueCandidate(x.Id, x.Status, x.AttemptCount))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await dbContext.QueuedMessages
            .AsNoTracking()
            .Where(x =>
                ((x.Status == QueuedMessageStatus.Pending || x.Status == QueuedMessageStatus.RetryScheduled)
                    && x.NextAttemptAtUtc <= now)
                || (x.Status == QueuedMessageStatus.InProgress
                    && x.LastAttemptStartedUtc != null
                    && x.LastAttemptStartedUtc <= staleThreshold))
            .OrderBy(x => x.Status == QueuedMessageStatus.InProgress
                ? x.LastAttemptStartedUtc
                : (DateTimeOffset?)x.NextAttemptAtUtc)
            .ThenBy(x => x.CreatedUtc)
            .Select(x => new QueueCandidate(x.Id, x.Status, x.AttemptCount))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
