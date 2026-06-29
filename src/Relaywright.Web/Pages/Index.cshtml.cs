using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Pages;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRelayConfigurationService relayConfigurationService,
    IRuntimeStatusService runtimeStatusService,
    IDashboardMetricsService dashboardMetricsService,
    ILogger<IndexModel> logger) : PageModel
{
    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public RuntimeStatusSnapshot RuntimeStatus { get; private set; } = new();

    public DashboardMetricsSnapshot Metrics { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public int PendingCount { get; private set; }

    public int RetryCount { get; private set; }

    public int FailedCount { get; private set; }

    public int DeliveredTodayCount { get; private set; }

    public IReadOnlyList<OperationalEvent> RecentEvents { get; private set; } = Array.Empty<OperationalEvent>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        RuntimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        Metrics = await dashboardMetricsService.GetSnapshotAsync(Configuration, cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        PendingCount = await dbContext.QueuedMessages
            .CountAsync(x => x.Status == QueuedMessageStatus.Pending, cancellationToken);
        RetryCount = await dbContext.QueuedMessages
            .CountAsync(x =>
                x.Status == QueuedMessageStatus.RetryScheduled
                || x.Status == QueuedMessageStatus.InProgress,
                cancellationToken);
        FailedCount = await dbContext.QueuedMessages
            .CountAsync(x =>
                x.Status == QueuedMessageStatus.Failed
                || x.Status == QueuedMessageStatus.Expired,
                cancellationToken);
        DeliveredTodayCount = await dbContext.QueuedMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM "QueuedMessages"
                WHERE "Status" = {(int)QueuedMessageStatus.Delivered}
                    AND "DeliveredUtc" IS NOT NULL
                    AND "DeliveredUtc" >= {todayUtc}
                """)
            .CountAsync(cancellationToken);

        RecentEvents = await dbContext.OperationalEvents
            .FromSqlRaw("""
                SELECT *
                FROM "OperationalEvents"
                ORDER BY "OccurredUtc" DESC
                LIMIT 20
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public async Task<IActionResult> OnPostPauseAsync(CancellationToken cancellationToken)
    {
        await runtimeStatusService.PauseDeliveryAsync(null, User.Identity?.Name, cancellationToken);
        StatusMessage = "Outbound delivery paused.";
        logger.LogWarning("Outbound delivery pause requested from dashboard. User={UserName}", User.Identity?.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(CancellationToken cancellationToken)
    {
        await runtimeStatusService.ResumeDeliveryAsync(User.Identity?.Name, cancellationToken);
        StatusMessage = "Outbound delivery resumed.";
        logger.LogInformation("Outbound delivery resume requested from dashboard. User={UserName}", User.Identity?.Name);
        return RedirectToPage();
    }
}
