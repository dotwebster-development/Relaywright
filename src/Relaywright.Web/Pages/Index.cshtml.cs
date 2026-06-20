using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Pages;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRelayConfigurationService relayConfigurationService,
    ILogger<IndexModel> logger) : PageModel
{
    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public int PendingCount { get; private set; }

    public int RetryCount { get; private set; }

    public int FailedCount { get; private set; }

    public int DeliveredTodayCount { get; private set; }

    public IReadOnlyList<OperationalEvent> RecentEvents { get; private set; } = Array.Empty<OperationalEvent>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        PendingCount = await dbContext.QueuedMessages.CountAsync(x => x.Status == QueuedMessageStatus.Pending, cancellationToken);
        RetryCount =
            await dbContext.QueuedMessages.CountAsync(x => x.Status == QueuedMessageStatus.RetryScheduled, cancellationToken)
            + await dbContext.QueuedMessages.CountAsync(x => x.Status == QueuedMessageStatus.InProgress, cancellationToken);
        FailedCount =
            await dbContext.QueuedMessages.CountAsync(x => x.Status == QueuedMessageStatus.Failed, cancellationToken)
            + await dbContext.QueuedMessages.CountAsync(x => x.Status == QueuedMessageStatus.Expired, cancellationToken);
        DeliveredTodayCount = (await dbContext.QueuedMessages
            .Where(x => x.Status == QueuedMessageStatus.Delivered)
            .Select(x => x.DeliveredUtc)
            .ToListAsync(cancellationToken))
            .Count(x => x != null && x >= todayUtc);

        var recentEvents = await dbContext.OperationalEvents
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        RecentEvents = recentEvents
            .OrderByDescending(x => x.OccurredUtc)
            .Take(20)
            .ToList();

        logger.LogDebug(
            "Dashboard loaded. Pending={PendingCount}; Retry={RetryCount}; Failed={FailedCount}; DeliveredToday={DeliveredTodayCount}; RecentEventCount={RecentEventCount}; Listener={ListenerBindAddress}:{ListenerPort}; User={UserName}",
            PendingCount,
            RetryCount,
            FailedCount,
            DeliveredTodayCount,
            RecentEvents.Count,
            Configuration.ListenerBindAddress,
            Configuration.ListenerPort,
            User.Identity?.Name);
    }
}
