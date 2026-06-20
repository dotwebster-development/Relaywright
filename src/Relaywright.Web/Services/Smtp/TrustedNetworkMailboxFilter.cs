using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Storage;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Smtp;

public sealed class TrustedNetworkMailboxFilter(
    ITrustedNetworkService trustedNetworkService,
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
        var trusted = await trustedNetworkService.IsTrustedAsync(remoteIp, cancellationToken);
        if (trusted)
        {
            logger.LogInformation(
                "SMTP MAIL FROM accepted from trusted network. SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; DeclaredSize={DeclaredSize}",
                context.GetOrCreateSessionId(),
                remoteIp?.ToString(),
                from.ToString(),
                size);

            return true;
        }

        logger.LogWarning(
            "SMTP MAIL FROM denied because remote IP is not trusted. SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; DeclaredSize={DeclaredSize}",
            context.GetOrCreateSessionId(),
            remoteIp?.ToString(),
            from.ToString(),
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

    public override Task<bool> CanDeliverToAsync(
        ISessionContext context,
        IMailbox to,
        IMailbox from,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "SMTP RCPT TO accepted. SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; Recipient={Recipient}",
            context.GetOrCreateSessionId(),
            context.GetRemoteIpAddress()?.ToString(),
            from.ToString(),
            to.ToString());

        return Task.FromResult(true);
    }
}
