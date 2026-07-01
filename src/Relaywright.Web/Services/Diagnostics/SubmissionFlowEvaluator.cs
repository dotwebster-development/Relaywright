using System.Net;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class SubmissionFlowEvaluator(
    ITrustedNetworkService trustedNetworkService,
    ITrustedDevicePolicyService trustedDevicePolicyService,
    ITrustedDeviceRateLimiter trustedDeviceRateLimiter) : ISubmissionFlowEvaluator
{
    public async Task<SubmissionFlowEvaluation> EvaluateAsync(
        SubmissionFlowCheckRequest request,
        CancellationToken cancellationToken,
        SubmissionPolicy? policyOverride = null)
    {
        var stages = new List<SubmissionFlowCheckStage>();

        SubmissionFlowEvaluation Result(bool succeeded, string message, string? recommendation = null)
        {
            return new SubmissionFlowEvaluation
            {
                Succeeded = succeeded,
                Message = message,
                Recommendation = recommendation,
                Stages = stages.ToArray()
            };
        }

        bool Stage(string name, bool succeeded, string message)
        {
            stages.Add(new SubmissionFlowCheckStage
            {
                Name = name,
                Succeeded = succeeded,
                Message = message
            });
            return succeeded;
        }

        if (!IPAddress.TryParse(request.SourceIpAddress.Trim(), out var remoteIp))
        {
            const string message = "Source IP address is not valid.";
            Stage("Source IP", false, message);
            return Result(false, message, "Enter the IPv4 or IPv6 address that the device uses to reach Relaywright.");
        }

        var profile = await trustedNetworkService.FindMatchingAsync(remoteIp, cancellationToken);
        if (profile is null)
        {
            const string message = "Source IP does not match an enabled trusted network.";
            Stage("Trusted Network", false, message);
            return Result(false, message, "Add the source IP/CIDR under Trusted IPs or run the check from a trusted device address.");
        }

        Stage("Trusted Network", true, $"Matched {profile.Cidr} ({profile.Description}).");

        var policy = policyOverride ?? await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        var senderDecision = trustedDevicePolicyService.CanAcceptFrom(
            profile,
            policy,
            request.EnvelopeFrom,
            Math.Max(0, request.DeclaredSizeBytes));
        if (!Stage("Sender And Size", senderDecision.Allowed, senderDecision.Message))
        {
            return Result(false, senderDecision.Message, RecommendForSenderAndSize(senderDecision.Message));
        }

        var rateDecision = trustedDeviceRateLimiter.PreviewAcceptMessage(profile, remoteIp.ToString());
        if (!Stage("Rate Limit", rateDecision.Allowed, rateDecision.Message))
        {
            return Result(false, rateDecision.Message, "Wait for the device rate-limit window to clear or raise the trusted-device hourly limit.");
        }

        var recipients = ParseRecipients(request.Recipients);
        if (recipients.Count == 0)
        {
            const string message = "At least one recipient is required.";
            Stage("Recipients", false, message);
            return Result(false, message, "Add one or more envelope recipients to the test.");
        }

        for (var index = 0; index < recipients.Count; index++)
        {
            var recipient = recipients[index];
            var recipientDecision = trustedDevicePolicyService.CanDeliverTo(
                profile,
                policy,
                recipient,
                index + 1);
            if (!Stage($"Recipient {index + 1}", recipientDecision.Allowed, $"{recipient}: {recipientDecision.Message}"))
            {
                return Result(false, recipientDecision.Message, RecommendForRecipient(recipientDecision.Message));
            }
        }

        const string success = "Submission would be accepted before SMTP DATA.";
        return Result(true, success, "No change is needed for this submission path.");
    }

    private static string RecommendForSenderAndSize(string message)
    {
        if (message.Contains("size", StringComparison.OrdinalIgnoreCase))
        {
            return "Lower the message size or raise the stricter global/trusted-device size limit.";
        }

        if (message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Remove the sender from the matching block list or use a different envelope sender.";
        }

        return "Add the sender to the applicable allowed-senders list or leave that allow list empty.";
    }

    private static string RecommendForRecipient(string message)
    {
        if (message.Contains("recipient limit", StringComparison.OrdinalIgnoreCase))
        {
            return "Reduce the recipient count or raise the stricter global/trusted-device recipient limit.";
        }

        if (message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Remove the recipient domain from the matching block list or use a permitted recipient domain.";
        }

        if (message.Contains("domain", StringComparison.OrdinalIgnoreCase))
        {
            return "Add the recipient domain to the applicable allowed-domain list or leave that allow list empty.";
        }

        return "Correct the recipient address and run the flow check again.";
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
