using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Pages;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IRelayConfigurationService relayConfigurationService,
    IRuntimeStatusService runtimeStatusService,
    IDashboardMetricsService dashboardMetricsService,
    DatabaseConfiguration databaseConfiguration,
    IAlertService alertService,
    ILogger<IndexModel> logger) : PageModel
{
    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public RuntimeStatusSnapshot RuntimeStatus { get; private set; } = new();

    public DashboardMetricsSnapshot Metrics { get; private set; } = new();

    public DateTimeOffset LoadedUtc { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public int PendingCount { get; private set; }

    public int RetryCount { get; private set; }

    public int FailedCount { get; private set; }

    public int DeliveredTodayCount { get; private set; }

    public IReadOnlyList<AlertRuleSummary> AlertSummaries { get; private set; } = Array.Empty<AlertRuleSummary>();

    public IReadOnlyList<DashboardAttentionItem> AttentionItems { get; private set; } = Array.Empty<DashboardAttentionItem>();

    public IReadOnlyList<OperationalEvent> RecentEvents { get; private set; } = Array.Empty<OperationalEvent>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LoadedUtc = DateTimeOffset.UtcNow;
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        RuntimeStatus = await runtimeStatusService.GetSnapshotAsync(cancellationToken);
        Metrics = await dashboardMetricsService.GetSnapshotAsync(Configuration, cancellationToken);
        AlertSummaries = await alertService.GetRuleSummariesAsync(cancellationToken);

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
        DeliveredTodayCount = databaseConfiguration.IsSqlite
            ? await dbContext.QueuedMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "QueuedMessages"
                    WHERE "Status" = {(int)QueuedMessageStatus.Delivered}
                        AND "DeliveredUtc" IS NOT NULL
                        AND "DeliveredUtc" >= {todayUtc}
                    """)
                .CountAsync(cancellationToken)
            : await dbContext.QueuedMessages
                .CountAsync(x =>
                    x.Status == QueuedMessageStatus.Delivered
                    && x.DeliveredUtc != null
                    && x.DeliveredUtc >= todayUtc,
                    cancellationToken);

        RecentEvents = databaseConfiguration.IsSqlite
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

        AttentionItems = BuildAttentionItems();

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

    private IReadOnlyList<DashboardAttentionItem> BuildAttentionItems()
    {
        var items = new List<DashboardAttentionItem>();

        if (RuntimeStatus.RestartRequired)
        {
            items.Add(new DashboardAttentionItem(
                "Restart required",
                RuntimeStatus.RestartSupported
                    ? "A graceful restart has been requested."
                    : "Restart Relaywright from the host service manager to apply the change.",
                "severity-warning"));
        }

        if (RuntimeStatus.IsDeliveryPaused)
        {
            items.Add(new DashboardAttentionItem(
                "Outbound delivery paused",
                string.IsNullOrWhiteSpace(RuntimeStatus.DeliveryPausedBy)
                    ? "SMTP intake continues, but outbound queue claiming is stopped."
                    : $"Paused by {RuntimeStatus.DeliveryPausedBy}.",
                "status-disabled"));
        }

        if (!string.Equals(RuntimeStatus.SmtpListener.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new DashboardAttentionItem(
                "SMTP listener not running",
                RuntimeStatus.SmtpListener.Detail ?? RuntimeStatus.SmtpListener.LastError ?? "Listener state needs review.",
                "status-failed"));
        }

        if (string.Equals(RuntimeStatus.DeliveryWorker.Status, "Faulted", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new DashboardAttentionItem(
                "Delivery worker faulted",
                RuntimeStatus.DeliveryWorker.LastError ?? RuntimeStatus.DeliveryWorker.Detail ?? "Delivery worker state needs review.",
                "status-failed"));
        }

        if (FailedCount > 0)
        {
            items.Add(new DashboardAttentionItem(
                "Failed queue items",
                $"{FailedCount:N0} failed or expired message(s) need review.",
                "status-failed"));
        }

        if (!Metrics.BackupReadiness.IsReady)
        {
            items.Add(new DashboardAttentionItem(
                "Backup readiness",
                Metrics.BackupReadiness.Message,
                "severity-warning"));
        }

        items.AddRange(AlertSummaries
            .Where(x => x.Rule.IsEnabled && x.IsActive)
            .Select(x => new DashboardAttentionItem(
                $"Alert: {x.Rule.DisplayName}",
                $"{x.ObservedValue:N0} / {x.Threshold:N0}. {x.Message}",
                "severity-warning")));

        return items;
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

public sealed record DashboardAttentionItem(string Title, string Detail, string BadgeClass);
