using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class SubmissionPolicyModel(
    ITrustedDevicePolicyService trustedDevicePolicyService,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<SubmissionPolicyModel> logger) : PageModel
{
    [BindProperty]
    public SubmissionPolicy Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);

        logger.LogDebug(
            "Submission policy page loaded. Enabled={Enabled}; User={UserName}",
            Input.IsEnabled,
            User.Identity?.Name);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        await configurationSnapshotService.CaptureAsync(
            ConfigurationSnapshotService.SubmissionPolicyArea,
            User.Identity?.Name,
            "Snapshot before submission policy save.",
            cancellationToken);
        await trustedDevicePolicyService.SavePolicyAsync(Input, cancellationToken);

        StatusMessage = "Submission policy saved.";
        logger.LogInformation(
            "Submission policy saved from admin page. Enabled={Enabled}; MaxMessageSizeBytes={MaxMessageSizeBytes}; MaxRecipientsPerMessage={MaxRecipientsPerMessage}; User={UserName}",
            Input.IsEnabled,
            Input.MaxMessageSizeBytes,
            Input.MaxRecipientsPerMessage,
            User.Identity?.Name);

        return RedirectToPage();
    }
}
