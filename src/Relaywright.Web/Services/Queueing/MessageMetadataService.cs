using MimeKit;

namespace Relaywright.Web.Services.Queueing;

public sealed class MessageMetadataService(
    IMessageSpoolService spoolService,
    ILogger<MessageMetadataService> logger) : IMessageMetadataService
{
    public async Task<MessageMetadataSummary?> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!spoolService.Exists(relativePath))
        {
            logger.LogWarning("Message metadata read skipped because spool file was missing. RelativePath={RelativePath}", relativePath);
            return null;
        }

        await using var stream = spoolService.OpenRead(relativePath);
        var message = await MimeMessage.LoadAsync(stream, cancellationToken);

        logger.LogInformation(
            "Read message metadata from spool. RelativePath={RelativePath}; HeaderMessageId={HeaderMessageId}; SubjectLength={SubjectLength}; ContentType={ContentType}",
            relativePath,
            message.MessageId,
            message.Subject?.Length ?? 0,
            message.Body?.ContentType?.MimeType);

        return new MessageMetadataSummary
        {
            Subject = message.Subject,
            MessageId = message.MessageId,
            Date = message.Date.ToUniversalTime(),
            HeaderFrom = string.Join(", ", message.From.Select(x => x.ToString())),
            HeaderTo = string.Join(", ", message.To.Select(x => x.ToString())),
            ContentType = message.Body?.ContentType?.MimeType
        };
    }
}
