using System.Net;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowChecker(
    ITrustedNetworkService trustedNetworkService,
    ITrustedDevicePolicyService trustedDevicePolicyService,
    ITrustedDeviceRateLimiter trustedDeviceRateLimiter,
    IDiagnosticRunRecorder diagnosticRunRecorder,
    ILogger<SubmissionFlowChecker> logger) : ISubmissionFlowChecker
{
    public async Task<SubmissionFlowCheckResult> CheckAsync(
        SubmissionFlowCheckRequest request,
        string? requestedBy,
        CancellationToken cancellationToken)
    {
        var run = await diagnosticRunRecorder.StartRunAsync(
            DiagnosticRunKind.SubmissionFlow,
            sessionId: null,
            requestedBy,
            cancellationToken);
        var stages = new List<SubmissionFlowCheckStage>();
        var sequence = 0;

        async Task<bool> StageAsync(string name, bool succeeded, string message)
        {
            var stage = await diagnosticRunRecorder.StartStageAsync(run.Id, ++sequence, name, message, cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                stage.Id,
                succeeded ? DiagnosticStageStatus.Succeeded : DiagnosticStageStatus.Failed,
                message,
                detail: null,
                cancellationToken);
            stages.Add(new SubmissionFlowCheckStage
            {
                Name = name,
                Succeeded = succeeded,
                Message = message
            });
            return succeeded;
        }

        try
        {
            if (!IPAddress.TryParse(request.SourceIpAddress.Trim(), out var remoteIp))
            {
                await StageAsync("Source IP", false, "Source IP address is not valid.");
                await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, "Source IP address is not valid.", cancellationToken);
                return Result(false, run.Id, "Source IP address is not valid.", stages);
            }

            var profile = await trustedNetworkService.FindMatchingAsync(remoteIp, cancellationToken);
            if (profile is null)
            {
                await StageAsync("Trusted Network", false, "Source IP does not match an enabled trusted network.");
                await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, "Source IP does not match an enabled trusted network.", cancellationToken);
                return Result(false, run.Id, "Source IP does not match an enabled trusted network.", stages);
            }

            await StageAsync(
                "Trusted Network",
                true,
                $"Matched {profile.Cidr} ({profile.Description}).");

            var policy = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
            var senderDecision = trustedDevicePolicyService.CanAcceptFrom(
                profile,
                policy,
                request.EnvelopeFrom,
                Math.Max(0, request.DeclaredSizeBytes));
            if (!await StageAsync("Sender And Size", senderDecision.Allowed, senderDecision.Message))
            {
                await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, senderDecision.Message, cancellationToken);
                return Result(false, run.Id, senderDecision.Message, stages);
            }

            var rateDecision = trustedDeviceRateLimiter.PreviewAcceptMessage(profile, remoteIp.ToString());
            if (!await StageAsync("Rate Limit", rateDecision.Allowed, rateDecision.Message))
            {
                await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, rateDecision.Message, cancellationToken);
                return Result(false, run.Id, rateDecision.Message, stages);
            }

            var recipients = ParseRecipients(request.Recipients);
            if (recipients.Count == 0)
            {
                await StageAsync("Recipients", false, "At least one recipient is required.");
                await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, "At least one recipient is required.", cancellationToken);
                return Result(false, run.Id, "At least one recipient is required.", stages);
            }

            for (var index = 0; index < recipients.Count; index++)
            {
                var recipient = recipients[index];
                var recipientDecision = trustedDevicePolicyService.CanDeliverTo(
                    profile,
                    policy,
                    recipient,
                    index + 1);
                if (!await StageAsync($"Recipient {index + 1}", recipientDecision.Allowed, $"{recipient}: {recipientDecision.Message}"))
                {
                    await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, recipientDecision.Message, cancellationToken);
                    return Result(false, run.Id, recipientDecision.Message, stages);
                }
            }

            var message = "Submission would be accepted before SMTP DATA.";
            await diagnosticRunRecorder.CompleteRunAsync(run.Id, true, message, cancellationToken);
            logger.LogInformation(
                "Submission flow check succeeded. RemoteIp={RemoteIp}; RecipientCount={RecipientCount}; RequestedBy={RequestedBy}",
                remoteIp,
                recipients.Count,
                requestedBy);

            return Result(true, run.Id, message, stages);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Submission flow check failed unexpectedly. SourceIp={SourceIp}; RequestedBy={RequestedBy}",
                request.SourceIpAddress,
                requestedBy);
            await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, exception.Message, cancellationToken);
            return Result(false, run.Id, exception.Message, stages);
        }
    }

    private static SubmissionFlowCheckResult Result(
        bool succeeded,
        Guid runId,
        string message,
        IReadOnlyList<SubmissionFlowCheckStage> stages)
    {
        return new SubmissionFlowCheckResult
        {
            Succeeded = succeeded,
            DiagnosticRunId = runId,
            Message = message,
            Stages = stages.ToArray()
        };
    }

    private static IReadOnlyList<string> ParseRecipients(string value)
    {
        return value
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
