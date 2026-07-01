using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.ConfigurationHistory;

namespace Relaywright.Web.Pages.Operations;

public sealed class AlertsModel(
    IAlertService alertService,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<AlertsModel> logger) : PageModel
{
    public IReadOnlyList<AlertRule> Rules { get; private set; } = Array.Empty<AlertRule>();

    public IReadOnlyList<AlertRuleSummary> RuleSummaries { get; private set; } = Array.Empty<AlertRuleSummary>();

    public IReadOnlyList<AlertResult> RecentResults { get; private set; } = Array.Empty<AlertResult>();

    [BindProperty]
    public int RuleId { get; set; }

    [BindProperty]
    public bool IsEnabled { get; set; }

    [BindProperty]
    public long Threshold { get; set; }

    [BindProperty]
    public int CooldownMinutes { get; set; }

    [BindProperty]
    public string? EmailRecipients { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        await configurationSnapshotService.CaptureAsync(
            ConfigurationSnapshotService.AlertRulesArea,
            User.Identity?.Name,
            "Snapshot before alert rule save.",
            cancellationToken);
        await alertService.SaveRuleAsync(new AlertRule
        {
            Id = RuleId,
            IsEnabled = IsEnabled,
            Threshold = Threshold,
            CooldownMinutes = CooldownMinutes,
            EmailRecipients = EmailRecipients
        }, cancellationToken);

        StatusMessage = "Alert rule saved.";
        logger.LogInformation("Alert rule saved from operations page. RuleId={RuleId}; User={UserName}", RuleId, User.Identity?.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEvaluateAsync(CancellationToken cancellationToken)
    {
        await alertService.EvaluateAsync(cancellationToken);
        StatusMessage = "Alert rules evaluated.";
        logger.LogInformation("Alert evaluation requested from operations page. User={UserName}", User.Identity?.Name);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Rules = await alertService.GetRulesAsync(cancellationToken);
        RuleSummaries = await alertService.GetRuleSummariesAsync(cancellationToken);
        RecentResults = await alertService.GetRecentResultsAsync(25, cancellationToken);
    }
}
