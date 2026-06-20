using System.Buffers;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Services.Smtp;

public sealed class RelayMessageStore(
    IMessageSpoolService spoolService,
    IMessageQueueService queueService,
    IRelayConfigurationService relayConfigurationService,
    IOperationalEventService eventService,
    ILogger<RelayMessageStore> logger) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        string? spoolRelativePath = null;
        var queued = false;

        try
        {
            var configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
            var sessionId = context.GetOrCreateSessionId();
            var messageId = Guid.NewGuid();
            var acceptedUtc = DateTimeOffset.UtcNow;
            var remoteIp = context.GetRemoteIpAddress()?.ToString();
            var recipients = transaction.To.Select(x => x.ToString() ?? string.Empty).ToArray();

            logger.LogInformation(
                "SMTP DATA received. MessageId={MessageId}; SessionId={SessionId}; RemoteIp={RemoteIp}; EnvelopeFrom={EnvelopeFrom}; RecipientCount={RecipientCount}; Bytes={Bytes}",
                messageId,
                sessionId,
                remoteIp,
                transaction.From?.ToString() ?? string.Empty,
                recipients.Length,
                buffer.Length);

            spoolRelativePath = await spoolService.WriteAsync(messageId, acceptedUtc, buffer, cancellationToken);

            await queueService.EnqueueAsync(new NewQueuedMessageRequest
            {
                MessageId = messageId,
                SessionId = sessionId,
                RemoteIpAddress = remoteIp,
                EnvelopeFrom = transaction.From?.ToString() ?? string.Empty,
                Recipients = recipients,
                SpoolFileRelativePath = spoolRelativePath,
                MessageSizeBytes = buffer.Length,
                AcceptedUtc = acceptedUtc,
                MessageExpirationHours = configuration.MessageExpirationHours
            }, cancellationToken);
            queued = true;

            logger.LogInformation(
                "SMTP DATA accepted and queued. MessageId={MessageId}; SessionId={SessionId}; RemoteIp={RemoteIp}; SpoolPath={SpoolPath}; RecipientCount={RecipientCount}",
                messageId,
                sessionId,
                remoteIp,
                spoolRelativePath,
                recipients.Length);

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.SmtpSession,
                SessionId = sessionId,
                QueuedMessageId = messageId,
                RemoteIpAddress = remoteIp,
                Message = $"SMTP DATA accepted for {recipients.Length} recipient(s)."
            }, cancellationToken);

            return SmtpResponse.Ok;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist SMTP DATA. SessionId={SessionId}; RemoteIp={RemoteIp}; SpoolPath={SpoolPath}; Queued={Queued}",
                context.GetOrCreateSessionId(),
                context.GetRemoteIpAddress()?.ToString(),
                spoolRelativePath,
                queued);

            if (!queued && !string.IsNullOrWhiteSpace(spoolRelativePath))
            {
                await spoolService.DeleteIfExistsAsync(spoolRelativePath, cancellationToken);
            }

            await eventService.WriteAsync(new OperationalEventRequest
            {
                Severity = EventSeverity.Error,
                Category = OperationalEventCategory.Queue,
                SessionId = context.GetOrCreateSessionId(),
                RemoteIpAddress = context.GetRemoteIpAddress()?.ToString(),
                Message = "Failed to persist queued message.",
                Detail = exception.ToString()
            }, cancellationToken);

            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Unable to queue message.");
        }
    }
}
