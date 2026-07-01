using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class SubmissionPolicyModel(
    ITrustedDevicePolicyService trustedDevicePolicyService,
    IConfigurationSnapshotService configurationSnapshotService,
    ISubmissionFlowEvaluator submissionFlowEvaluator,
    ILogger<SubmissionPolicyModel> logger) : PageModel
{
    [BindProperty]
    public SubmissionPolicy Input { get; set; } = new();

    [BindProperty]
    public PreviewInputModel Preview { get; set; } = new();

    public SubmissionFlowEvaluation? PreviewResult { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        InitializePreviewDefaults();

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

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        var validationMessage = ValidatePreview();
        if (validationMessage is not null)
        {
            PreviewResult = new SubmissionFlowEvaluation
            {
                Succeeded = false,
                Message = validationMessage,
                Recommendation = "Complete the preview fields and run the check again."
            };
            return Page();
        }

        PreviewResult = await submissionFlowEvaluator.EvaluateAsync(
            new SubmissionFlowCheckRequest
            {
                SourceIpAddress = Preview.SourceIpAddress,
                EnvelopeFrom = Preview.EnvelopeFrom,
                Recipients = Preview.Recipients,
                DeclaredSizeBytes = Math.Max(0, Preview.DeclaredSizeBytes)
            },
            cancellationToken,
            NormalizePreviewPolicy(Input));

        logger.LogInformation(
            "Submission policy preview requested. Succeeded={Succeeded}; User={UserName}",
            PreviewResult.Succeeded,
            User.Identity?.Name);

        return Page();
    }

    private void InitializePreviewDefaults()
    {
        if (Preview.DeclaredSizeBytes <= 0)
        {
            Preview.DeclaredSizeBytes = 1024;
        }
    }

    private string? ValidatePreview()
    {
        if (string.IsNullOrWhiteSpace(Preview.SourceIpAddress))
        {
            return "Source IP is required.";
        }

        if (string.IsNullOrWhiteSpace(Preview.EnvelopeFrom))
        {
            return "Envelope sender is required.";
        }

        if (string.IsNullOrWhiteSpace(Preview.Recipients))
        {
            return "At least one recipient is required.";
        }

        return null;
    }

    private static SubmissionPolicy NormalizePreviewPolicy(SubmissionPolicy policy)
    {
        return new SubmissionPolicy
        {
            Id = policy.Id,
            IsEnabled = policy.IsEnabled,
            AllowedSenderAddresses = policy.AllowedSenderAddresses,
            BlockedSenderAddresses = policy.BlockedSenderAddresses,
            AllowedRecipientDomains = policy.AllowedRecipientDomains,
            BlockedRecipientDomains = policy.BlockedRecipientDomains,
            MaxMessageSizeBytes = policy.MaxMessageSizeBytes is > 0 ? policy.MaxMessageSizeBytes : null,
            MaxRecipientsPerMessage = policy.MaxRecipientsPerMessage is > 0 ? policy.MaxRecipientsPerMessage : null,
            UpdatedUtc = policy.UpdatedUtc
        };
    }

    public sealed class PreviewInputModel
    {
        public string SourceIpAddress { get; set; } = string.Empty;

        public string EnvelopeFrom { get; set; } = string.Empty;

        public string Recipients { get; set; } = string.Empty;

        public long DeclaredSizeBytes { get; set; } = 1024;
    }
}
