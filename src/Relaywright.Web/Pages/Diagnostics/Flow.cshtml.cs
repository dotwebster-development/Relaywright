using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;

namespace Relaywright.Web.Pages.Diagnostics;

public sealed class FlowModel(
    ISubmissionFlowChecker submissionFlowChecker,
    IDiagnosticRunRecorder diagnosticRunRecorder,
    ILogger<FlowModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SubmissionFlowCheckResult? Result { get; private set; }

    public IReadOnlyList<DiagnosticRun> RecentRuns { get; private set; } = Array.Empty<DiagnosticRun>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.SubmissionFlow, 10, cancellationToken);
        Input.DeclaredSizeBytes = 1024;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.SubmissionFlow, 10, cancellationToken);
            return Page();
        }

        Result = await submissionFlowChecker.CheckAsync(
            new SubmissionFlowCheckRequest
            {
                SourceIpAddress = Input.SourceIpAddress,
                EnvelopeFrom = Input.EnvelopeFrom,
                Recipients = Input.Recipients,
                DeclaredSizeBytes = Input.DeclaredSizeBytes
            },
            User.Identity?.Name,
            cancellationToken);
        RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.SubmissionFlow, 10, cancellationToken);

        logger.LogInformation(
            "Submission flow check requested from diagnostics page. Succeeded={Succeeded}; User={UserName}",
            Result.Succeeded,
            User.Identity?.Name);

        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        public string SourceIpAddress { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string EnvelopeFrom { get; set; } = string.Empty;

        [Required]
        public string Recipients { get; set; } = string.Empty;

        [Range(0, long.MaxValue)]
        public long DeclaredSizeBytes { get; set; } = 1024;
    }
}
