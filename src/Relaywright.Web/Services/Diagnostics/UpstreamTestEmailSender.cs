using System.Diagnostics;
using MailKit.Net.Smtp;
using MimeKit;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class UpstreamTestEmailSender(
    IUpstreamAuthenticationService upstreamAuthenticationService,
    IOperationalEventService eventService,
    ILogger<UpstreamTestEmailSender> logger) : IUpstreamTestEmailSender
{
    public async Task<TestEmailResult> SendAsync(
        TestEmailRequest request,
        RelayConfigurationSnapshot configuration,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await WriteAsync(
            EventSeverity.Information,
            "Diagnostic test email requested.",
            $"From={request.FromAddress}; To={request.ToAddress}; Subject={request.Subject}",
            sessionId,
            cancellationToken);

        logger.LogInformation(
            "Diagnostic test email requested. SessionId={SessionId}; From={FromAddress}; To={ToAddress}; SubjectLength={SubjectLength}; BodyLength={BodyLength}",
            sessionId,
            request.FromAddress,
            request.ToAddress,
            request.Subject.Length,
            request.Body.Length);

        if (string.IsNullOrWhiteSpace(configuration.UpstreamHost))
        {
            logger.LogWarning("Diagnostic test email aborted because upstream host is not configured. SessionId={SessionId}", sessionId);

            await WriteAsync(
                EventSeverity.Warning,
                "Diagnostic test email aborted.",
                "Upstream host is not configured.",
                sessionId,
                cancellationToken);

            return new TestEmailResult
            {
                Succeeded = false,
                Message = "Upstream host is not configured.",
                SessionId = sessionId
            };
        }

        using var client = new SmtpClient();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            client.Timeout = configuration.UpstreamTimeoutSeconds * 1000;

            await WriteAsync(
                EventSeverity.Information,
                "Connecting to upstream relay for diagnostic test email.",
                $"{configuration.UpstreamHost}:{configuration.UpstreamPort} using {configuration.UpstreamSecureSocketOptions}.",
                sessionId,
                cancellationToken);

            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);

            logger.LogInformation(
                "Diagnostic test email connected to upstream. SessionId={SessionId}; UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; SecureConnection={SecureConnection}",
                sessionId,
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                client.IsSecure);

            await WriteAsync(
                EventSeverity.Information,
                "Upstream relay connection established.",
                null,
                sessionId,
                cancellationToken);

            if (configuration.UseUpstreamAuthentication)
            {
                await WriteAsync(
                    EventSeverity.Information,
                    "Authenticating to upstream relay.",
                    configuration.UpstreamAuthenticationMode.ToString(),
                    sessionId,
                    cancellationToken);

                await upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);

                logger.LogInformation(
                    "Diagnostic test email authentication succeeded. SessionId={SessionId}; AuthMode={AuthMode}",
                    sessionId,
                    configuration.UpstreamAuthenticationMode);

                await WriteAsync(
                    EventSeverity.Information,
                    "Upstream relay authentication succeeded.",
                    null,
                    sessionId,
                    cancellationToken);
            }
            else
            {
                await WriteAsync(
                    EventSeverity.Information,
                    "Upstream relay authentication skipped.",
                    "No upstream authentication is configured.",
                    sessionId,
                    cancellationToken);
            }

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(request.FromAddress));
            message.To.Add(MailboxAddress.Parse(request.ToAddress));
            message.Subject = request.Subject;
            message.Body = new TextPart("plain")
            {
                Text = request.Body
            };

            await WriteAsync(
                EventSeverity.Information,
                "Submitting diagnostic test email to upstream relay.",
                null,
                sessionId,
                cancellationToken);

            var response = await client.SendAsync(message, cancellationToken);

            logger.LogInformation(
                "Diagnostic test email accepted by upstream. SessionId={SessionId}; ElapsedMs={ElapsedMs}; Response={Response}",
                sessionId,
                stopwatch.ElapsedMilliseconds,
                response);

            await WriteAsync(
                EventSeverity.Information,
                "Upstream relay accepted the diagnostic test email.",
                response,
                sessionId,
                cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);

            return new TestEmailResult
            {
                Succeeded = true,
                Message = "Test email accepted by the upstream relay.",
                SessionId = sessionId
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Diagnostic test email failed. SessionId={SessionId}; ElapsedMs={ElapsedMs}; UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}",
                sessionId,
                stopwatch.ElapsedMilliseconds,
                configuration.UpstreamHost,
                configuration.UpstreamPort);

            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
                catch (Exception disconnectException)
                {
                    // The original SMTP failure is the useful error to surface here.
                    logger.LogDebug(
                        disconnectException,
                        "Diagnostic test email disconnect cleanup failed. SessionId={SessionId}",
                        sessionId);
                }
            }

            await WriteAsync(
                EventSeverity.Error,
                "Diagnostic test email failed.",
                exception.Message,
                sessionId,
                cancellationToken);

            return new TestEmailResult
            {
                Succeeded = false,
                Message = exception.Message,
                SessionId = sessionId
            };
        }
    }

    private Task WriteAsync(
        EventSeverity severity,
        string message,
        string? detail,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return eventService.WriteAsync(new OperationalEventRequest
        {
            Severity = severity,
            Category = OperationalEventCategory.Diagnostics,
            SessionId = sessionId,
            Message = message,
            Detail = detail
        }, cancellationToken);
    }
}
