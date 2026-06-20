using System.Diagnostics;
using MailKit.Net.Smtp;
using MimeKit;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Services.Delivery;

public sealed class UpstreamDeliveryService(
    IMessageSpoolService spoolService,
    IUpstreamAuthenticationService upstreamAuthenticationService,
    DeliveryFailureClassifier failureClassifier,
    ILogger<UpstreamDeliveryService> logger) : IUpstreamDeliveryService
{
    public async Task<DeliveryResult> DeliverAsync(
        DeliveryWorkItem workItem,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.UpstreamHost))
        {
            logger.LogError(
                "Delivery cannot start because upstream host is not configured. MessageId={MessageId}; AttemptNumber={AttemptNumber}",
                workItem.MessageId,
                workItem.AttemptNumber);

            return new DeliveryResult
            {
                IsPermanentFailure = true,
                FailureCategory = DeliveryFailureCategory.Configuration,
                ErrorDetail = "Upstream host is not configured.",
                ExceptionType = nameof(InvalidOperationException)
            };
        }

        if (!spoolService.Exists(workItem.SpoolFileRelativePath))
        {
            logger.LogError(
                "Delivery cannot start because spool file is missing. MessageId={MessageId}; AttemptNumber={AttemptNumber}; SpoolPath={SpoolPath}",
                workItem.MessageId,
                workItem.AttemptNumber,
                workItem.SpoolFileRelativePath);

            return new DeliveryResult
            {
                IsPermanentFailure = true,
                FailureCategory = DeliveryFailureCategory.Configuration,
                ErrorDetail = "Spool file was not found.",
                ExceptionType = nameof(FileNotFoundException)
            };
        }

        using var client = new SmtpClient();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            logger.LogInformation(
                "Starting upstream delivery. MessageId={MessageId}; AttemptNumber={AttemptNumber}; UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; TlsMode={TlsMode}; RecipientCount={RecipientCount}; SpoolPath={SpoolPath}",
                workItem.MessageId,
                workItem.AttemptNumber,
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                workItem.Recipients.Count,
                workItem.SpoolFileRelativePath);

            await using var stream = spoolService.OpenRead(workItem.SpoolFileRelativePath);
            var message = await MimeMessage.LoadAsync(stream, cancellationToken);

            logger.LogDebug(
                "Loaded MIME message from spool. MessageId={MessageId}; AttemptNumber={AttemptNumber}; HeaderMessageId={HeaderMessageId}; SubjectLength={SubjectLength}",
                workItem.MessageId,
                workItem.AttemptNumber,
                message.MessageId,
                message.Subject?.Length ?? 0);

            client.Timeout = configuration.UpstreamTimeoutSeconds * 1000;
            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);

            logger.LogInformation(
                "Connected to upstream SMTP server. MessageId={MessageId}; AttemptNumber={AttemptNumber}; UpstreamHost={UpstreamHost}; SecureConnection={SecureConnection}",
                workItem.MessageId,
                workItem.AttemptNumber,
                configuration.UpstreamHost,
                client.IsSecure);

            await upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);

            var recipients = workItem.Recipients
                .Select(address => MailboxAddress.Parse(address))
                .ToList();

            logger.LogDebug(
                "Parsed delivery envelope. MessageId={MessageId}; AttemptNumber={AttemptNumber}; EnvelopeFromPresent={EnvelopeFromPresent}; RecipientCount={RecipientCount}",
                workItem.MessageId,
                workItem.AttemptNumber,
                !string.IsNullOrWhiteSpace(workItem.EnvelopeFrom),
                recipients.Count);

            string response;
            if (string.IsNullOrWhiteSpace(workItem.EnvelopeFrom))
            {
                response = await client.SendAsync(message, cancellationToken);
            }
            else
            {
                response = await client.SendAsync(
                    FormatOptions.Default,
                    message,
                    MailboxAddress.Parse(workItem.EnvelopeFrom),
                recipients,
                cancellationToken);
            }

            logger.LogInformation(
                "Upstream SMTP send completed. MessageId={MessageId}; AttemptNumber={AttemptNumber}; ElapsedMs={ElapsedMs}; Response={Response}",
                workItem.MessageId,
                workItem.AttemptNumber,
                stopwatch.ElapsedMilliseconds,
                response);

            return new DeliveryResult
            {
                Succeeded = true,
                ResponseText = response
            };
        }
        catch (Exception exception)
        {
            var result = failureClassifier.Classify(exception);

            logger.LogWarning(
                exception,
                "Upstream delivery failed. MessageId={MessageId}; AttemptNumber={AttemptNumber}; ElapsedMs={ElapsedMs}; FailureCategory={FailureCategory}; Permanent={Permanent}; ExceptionType={ExceptionType}; ResponseCode={ResponseCode}; ErrorDetail={ErrorDetail}",
                workItem.MessageId,
                workItem.AttemptNumber,
                stopwatch.ElapsedMilliseconds,
                result.FailureCategory,
                result.IsPermanentFailure,
                result.ExceptionType,
                result.ResponseCode,
                result.ErrorDetail ?? result.ResponseText);

            return result;
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
                catch
                {
                    // Delivery outcome has already been determined; disconnect cleanup should not trigger duplicates.
                    logger.LogDebug(
                        "Upstream SMTP disconnect cleanup failed. MessageId={MessageId}; AttemptNumber={AttemptNumber}",
                        workItem.MessageId,
                        workItem.AttemptNumber);
                }
            }
        }
    }
}
