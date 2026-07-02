using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Storage;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Smtp;

public sealed class TrustedNetworkMailboxFilter(
    ITrustedNetworkService trustedNetworkService,
    ITrustedDevicePolicyService trustedDevicePolicyService,
    ITrustedDeviceRateLimiter trustedDeviceRateLimiter,
    IOperationalEventService eventService,
    ILogger<TrustedNetworkMailboxFilter> logger) : MailboxFilter
{
    public override async Task<bool> CanAcceptFromAsync(
        ISessionContext context,
        IMailbox from,
        int size,
        CancellationToken cancellationToken)
    {
        var remoteIp = context.GetRemoteIpAddress();
        var envelopeFrom = SmtpMailboxFormatter.Format(from);
        var profile = await trustedNetworkService.FindMatchingAsync(remoteIp, cancellationToken);
        if (profile is null)
        {
            logger.LogWarning(
                "SMTP MAIL FROM denied because remote IP is not trusted. SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; DeclaredSize={DeclaredSize}",
                context.GetOrCreateSessionId(),
                remoteIp?.ToString(),
                envelopeFrom,
                size);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Warning,
                Category = OperationalEventCategory.Security,
                SessionId = context.GetOrCreateSessionId(),
                RemoteIpAddress = remoteIp?.ToString(),
                Message = "SMTP submission denied because the client IP is not trusted."
            }, cancellationToken);

            return false;
        }

        var policy = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        var policyDecision = trustedDevicePolicyService.CanAcceptFrom(profile, policy, envelopeFrom, size);
        if (!policyDecision.Allowed)
        {
            await WriteDeniedAsync(context, remoteIp?.ToString(), policyDecision.Message, cancellationToken);
            logger.LogWarning(
                "SMTP MAIL FROM denied by submission policy. SessionId={SessionId}; RemoteIp={RemoteIp}; TrustedNetworkId={TrustedNetworkId}; EnvelopeFrom={EnvelopeFrom}; Reason={Reason}",
                context.GetOrCreateSessionId(),
                remoteIp?.ToString(),
                profile.Id,
                envelopeFrom,
                policyDecision.Message);
            return false;
        }

        var rateLimitDecision = trustedDeviceRateLimiter.CanAcceptMessage(profile, remoteIp?.ToString());
        if (!rateLimitDecision.Allowed)
        {
            await WriteDeniedAsync(context, remoteIp?.ToString(), rateLimitDecision.Message, cancellationToken);
            logger.LogWarning(
                "SMTP MAIL FROM denied by device rate limit. SessionId={SessionId}; RemoteIp={RemoteIp}; TrustedNetworkId={TrustedNetworkId}; Reason={Reason}",
                context.GetOrCreateSessionId(),
                remoteIp?.ToString(),
                profile.Id,
                rateLimitDecision.Message);
            return false;
        }

        context.SetTrustedNetworkId(profile.Id);

        logger.LogInformation(
            "SMTP MAIL FROM accepted from trusted device profile. SessionId={SessionId}; RemoteIp={RemoteIp}; TrustedNetworkId={TrustedNetworkId}; EnvelopeFrom={EnvelopeFrom}; DeclaredSize={DeclaredSize}",
            context.GetOrCreateSessionId(),
            remoteIp?.ToString(),
            profile.Id,
            envelopeFrom,
            size);

        return true;
    }

    public override async Task<bool> CanDeliverToAsync(
        ISessionContext context,
        IMailbox to,
        IMailbox from,
        CancellationToken cancellationToken)
    {
        var remoteIp = context.GetRemoteIpAddress();
        var envelopeFrom = SmtpMailboxFormatter.Format(from);
        var recipient = SmtpMailboxFormatter.Format(to);
        var profile = await trustedNetworkService.FindMatchingAsync(remoteIp, cancellationToken);
        if (profile is null)
        {
            await WriteDeniedAsync(context, remoteIp?.ToString(), "Recipient denied because the client IP is not trusted.", cancellationToken);
            return false;
        }

        var policy = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        var recipientNumber = context.GetNextRecipientNumber();
        var decision = trustedDevicePolicyService.CanDeliverTo(profile, policy, recipient, recipientNumber);
        if (!decision.Allowed)
        {
            await WriteDeniedAsync(context, remoteIp?.ToString(), decision.Message, cancellationToken);
            logger.LogWarning(
                "SMTP RCPT TO denied by submission policy. SessionId={SessionId}; RemoteIp={RemoteIp}; TrustedNetworkId={TrustedNetworkId}; EnvelopeFrom={EnvelopeFrom}; Recipient={Recipient}; RecipientNumber={RecipientNumber}; Reason={Reason}",
                context.GetOrCreateSessionId(),
                remoteIp?.ToString(),
                profile.Id,
                envelopeFrom,
                recipient,
                recipientNumber,
                decision.Message);
            return false;
        }

        context.CommitRecipientAccepted();

        logger.LogDebug(
            "SMTP RCPT TO accepted. SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; Recipient={Recipient}",
            context.GetOrCreateSessionId(),
            remoteIp?.ToString(),
            envelopeFrom,
            recipient);

        return true;
    }

    private Task WriteDeniedAsync(
        ISessionContext context,
        string? remoteIp,
        string message,
        CancellationToken cancellationToken)
    {
        return eventService.WriteAsync(new OperationalEventRequest
        {
            Severity = EventSeverity.Warning,
            Category = OperationalEventCategory.Security,
            SessionId = context.GetOrCreateSessionId(),
            RemoteIpAddress = remoteIp,
            Message = message
        }, cancellationToken);
    }
}
