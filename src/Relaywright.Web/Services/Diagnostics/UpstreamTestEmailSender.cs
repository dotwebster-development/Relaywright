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
    IDiagnosticRunRecorder diagnosticRunRecorder,
    IOperationalEventService eventService,
    ILogger<UpstreamTestEmailSender> logger) : IUpstreamTestEmailSender
{
    public async Task<TestEmailResult> SendAsync(
        TestEmailRequest request,
        RelayConfigurationSnapshot configuration,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var run = await diagnosticRunRecorder.StartRunAsync(
            DiagnosticRunKind.TestEmail,
            sessionId,
            requestedBy: null,
            cancellationToken);
        DiagnosticStage? currentStage = null;
        var stageSequence = 0;

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
            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Configuration",
                "Checking upstream configuration.",
                cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Failed,
                "Upstream host is not configured.",
                detail: null,
                cancellationToken);
            await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, "Upstream host is not configured.", cancellationToken);

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
                SessionId = sessionId,
                DiagnosticRunId = run.Id
            };
        }

        using var client = new SmtpClient();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            client.Timeout = configuration.UpstreamTimeoutSeconds * 1000;

            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Configuration",
                "Checking test email request and upstream configuration.",
                cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Succeeded,
                "Configuration is present.",
                detail: null,
                cancellationToken);

            await WriteAsync(
                EventSeverity.Information,
                "Connecting to upstream relay for diagnostic test email.",
                $"{configuration.UpstreamHost}:{configuration.UpstreamPort} using {configuration.UpstreamSecureSocketOptions}.",
                sessionId,
                cancellationToken);

            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Connect/TLS",
                "Connecting to upstream relay.",
                cancellationToken);
            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Succeeded,
                client.IsSecure ? "Connected with TLS." : "Connected without TLS.",
                detail: null,
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
                currentStage = await diagnosticRunRecorder.StartStageAsync(
                    run.Id,
                    ++stageSequence,
                    "Authentication",
                    "Authenticating to upstream relay.",
                    cancellationToken);

                await WriteAsync(
                    EventSeverity.Information,
                    "Authenticating to upstream relay.",
                    configuration.UpstreamAuthenticationMode.ToString(),
                    sessionId,
                    cancellationToken);

                await upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);
                await diagnosticRunRecorder.CompleteStageAsync(
                    currentStage.Id,
                    DiagnosticStageStatus.Succeeded,
                    "Authentication succeeded.",
                    detail: null,
                    cancellationToken);

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
                currentStage = await diagnosticRunRecorder.StartStageAsync(
                    run.Id,
                    ++stageSequence,
                    "Authentication",
                    "Authentication is disabled.",
                    cancellationToken);
                await diagnosticRunRecorder.CompleteStageAsync(
                    currentStage.Id,
                    DiagnosticStageStatus.Skipped,
                    "Authentication skipped.",
                    detail: null,
                    cancellationToken);

                await WriteAsync(
                    EventSeverity.Information,
                    "Upstream relay authentication skipped.",
                    "No upstream authentication is configured.",
                    sessionId,
                    cancellationToken);
            }

            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Compose",
                "Composing diagnostic message.",
                cancellationToken);
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(request.FromAddress));
            message.To.Add(MailboxAddress.Parse(request.ToAddress));
            message.Subject = request.Subject;
            message.Body = new TextPart("plain")
            {
                Text = request.Body
            };
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Succeeded,
                "Diagnostic message composed.",
                detail: null,
                cancellationToken);

            await WriteAsync(
                EventSeverity.Information,
                "Submitting diagnostic test email to upstream relay.",
                null,
                sessionId,
                cancellationToken);

            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Send",
                "Submitting diagnostic message.",
                cancellationToken);
            var response = await client.SendAsync(message, cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Succeeded,
                "Upstream relay accepted the diagnostic message.",
                response,
                cancellationToken);

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

            currentStage = await diagnosticRunRecorder.StartStageAsync(
                run.Id,
                ++stageSequence,
                "Disconnect",
                "Disconnecting from upstream relay.",
                cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            await diagnosticRunRecorder.CompleteStageAsync(
                currentStage.Id,
                DiagnosticStageStatus.Succeeded,
                "Disconnected cleanly.",
                detail: null,
                cancellationToken);

            await diagnosticRunRecorder.CompleteRunAsync(run.Id, true, "Test email accepted by the upstream relay.", cancellationToken);

            return new TestEmailResult
            {
                Succeeded = true,
                Message = "Test email accepted by the upstream relay.",
                SessionId = sessionId,
                DiagnosticRunId = run.Id
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

            if (currentStage is not null)
            {
                await diagnosticRunRecorder.CompleteStageAsync(
                    currentStage.Id,
                    DiagnosticStageStatus.Failed,
                    exception.Message,
                    detail: null,
                    cancellationToken);
            }

            await diagnosticRunRecorder.CompleteRunAsync(run.Id, false, exception.Message, cancellationToken);

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
                SessionId = sessionId,
                DiagnosticRunId = run.Id
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
