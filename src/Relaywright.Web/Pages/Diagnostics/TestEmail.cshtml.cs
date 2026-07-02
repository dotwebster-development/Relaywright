using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Pages.Diagnostics;

public sealed class TestEmailModel(
    IRelayConfigurationService relayConfigurationService,
    IUpstreamTestEmailSender upstreamTestEmailSender,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IDiagnosticRunRecorder diagnosticRunRecorder,
    ILogger<TestEmailModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public RelayConfigurationSnapshot Configuration { get; private set; } = new();

    public TestEmailResult? Result { get; private set; }

    public IReadOnlyList<OperationalEvent> RunEvents { get; private set; } = Array.Empty<OperationalEvent>();

    public IReadOnlyList<DiagnosticRun> RecentRuns { get; private set; } = Array.Empty<DiagnosticRun>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.TestEmail, 10, cancellationToken);
        InitializeDefaults();

        logger.LogDebug(
            "Diagnostic test email page loaded. UpstreamConfigured={UpstreamConfigured}; User={UserName}",
            !string.IsNullOrWhiteSpace(Configuration.UpstreamHost),
            User.Identity?.Name);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.TestEmail, 10, cancellationToken);
            logger.LogWarning(
                "Diagnostic test email request rejected by validation. User={UserName}; ErrorCount={ErrorCount}",
                User.Identity?.Name,
                ModelState.ErrorCount);

            return Page();
        }

        var sessionId = Guid.NewGuid();
        logger.LogInformation(
            "Diagnostic test email requested from admin page. SessionId={SessionId}; From={FromAddress}; To={ToAddress}; SubjectLength={SubjectLength}; User={UserName}",
            sessionId,
            Input.FromAddress,
            Input.ToAddress,
            Input.Subject.Length,
            User.Identity?.Name);

        Result = await upstreamTestEmailSender.SendAsync(
            new TestEmailRequest
            {
                FromAddress = Input.FromAddress.Trim(),
                ToAddress = Input.ToAddress.Trim(),
                Subject = Input.Subject.Trim(),
                Body = Input.Body
            },
            Configuration,
            sessionId,
            cancellationToken);

        RunEvents = await LoadRunEventsAsync(sessionId, cancellationToken);
        RecentRuns = await diagnosticRunRecorder.GetRecentRunsAsync(DiagnosticRunKind.TestEmail, 10, cancellationToken);
        logger.LogInformation(
            "Diagnostic test email page completed. SessionId={SessionId}; Succeeded={Succeeded}; RunEventCount={RunEventCount}; User={UserName}",
            sessionId,
            Result.Succeeded,
            RunEvents.Count,
            User.Identity?.Name);

        return Page();
    }

    private void InitializeDefaults()
    {
        if (string.IsNullOrWhiteSpace(Input.FromAddress))
        {
            Input.FromAddress = GuessDefaultFromAddress(Configuration);
        }

        if (string.IsNullOrWhiteSpace(Input.Subject))
        {
            Input.Subject = "Relaywright test email";
        }

        if (string.IsNullOrWhiteSpace(Input.Body))
        {
            Input.Body = "This is a diagnostic test email sent from Relaywright.";
        }
    }

    private async Task<IReadOnlyList<OperationalEvent>> LoadRunEventsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var events = await dbContext.OperationalEvents
                .AsNoTracking()
                .Where(x => x.Category == OperationalEventCategory.Diagnostics && x.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        return events
            .OrderBy(x => x.OccurredUtc)
            .ToList();
    }

    private static string GuessDefaultFromAddress(RelayConfigurationSnapshot configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.UpstreamUserName)
            && configuration.UpstreamUserName.Contains('@', StringComparison.Ordinal))
        {
            return configuration.UpstreamUserName;
        }

        return "relay-test@localhost";
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string FromAddress { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string ToAddress { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [StringLength(4000)]
        public string Body { get; set; } = string.Empty;
    }
}
