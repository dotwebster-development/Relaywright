using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Updates;

namespace Relaywright.Web.Pages;

public sealed class IndexModel(
    IDashboardService dashboardService,
    IRuntimeStatusService runtimeStatusService,
    IUpdateCheckService updateCheckService,
    ILogger<IndexModel> logger) : PageModel
{
    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public RuntimeStatusSnapshot RuntimeStatus { get; private set; } = new();

    public DashboardMetricsSnapshot Metrics { get; private set; } = new();

    public DashboardReadinessSnapshot Readiness { get; private set; } = DashboardReadinessSnapshot.Empty;

    public SuspiciousLoginSummary SuspiciousLogins { get; private set; } = SuspiciousLoginSummary.Empty;

    public UpdateCheckStatus UpdateStatus { get; private set; } = UpdateCheckStatus.Initial();

    [TempData]
    public string? StatusMessage { get; set; }

    public int PendingCount { get; private set; }

    public int RetryCount { get; private set; }

    public int FailedCount { get; private set; }

    public int DeliveredTodayCount { get; private set; }

    public IReadOnlyList<OperationalEvent> RecentEvents { get; private set; } = Array.Empty<OperationalEvent>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);
        Configuration = snapshot.Configuration;
        RuntimeStatus = snapshot.RuntimeStatus;
        Metrics = snapshot.Metrics;
        Readiness = snapshot.Readiness;
        UpdateStatus = snapshot.UpdateStatus;
        SuspiciousLogins = snapshot.SuspiciousLogins;
        PendingCount = snapshot.PendingCount;
        RetryCount = snapshot.RetryCount;
        FailedCount = snapshot.FailedCount;
        DeliveredTodayCount = snapshot.DeliveredTodayCount;
        RecentEvents = snapshot.RecentEvents;

        logger.LogDebug(
            "Dashboard loaded. Pending={PendingCount}; Retry={RetryCount}; Failed={FailedCount}; DeliveredToday={DeliveredTodayCount}; RecentEventCount={RecentEventCount}; Readiness={ReadinessComplete}/{ReadinessTotal}; Listener={ListenerBindAddress}:{ListenerPort}; User={UserName}",
            PendingCount,
            RetryCount,
            FailedCount,
            DeliveredTodayCount,
            RecentEvents.Count,
            Readiness.CompletedRequiredCount,
            Readiness.RequiredCount,
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

    public async Task<IActionResult> OnPostCheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var status = await updateCheckService.RefreshAsync(cancellationToken);
        StatusMessage = status.Message ?? "Version check completed.";
        logger.LogInformation(
            "Manual version check requested from dashboard. User={UserName}; State={State}; CurrentVersion={CurrentVersion}; LatestVersion={LatestVersion}",
            User.Identity?.Name,
            status.State,
            status.CurrentVersion,
            status.LatestVersion);
        return RedirectToPage();
    }
}
