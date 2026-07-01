using System.Diagnostics;
using MailKit.Net.Smtp;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class UpstreamConnectivityTester : IUpstreamConnectivityTester
{
    private readonly IUpstreamAuthenticationService _upstreamAuthenticationService;
    private readonly IDiagnosticRunRecorder _diagnosticRunRecorder;
    private readonly ILogger<UpstreamConnectivityTester> _logger;

    public UpstreamConnectivityTester(
        IUpstreamAuthenticationService upstreamAuthenticationService,
        IDiagnosticRunRecorder diagnosticRunRecorder,
        ILogger<UpstreamConnectivityTester> logger)
    {
        _upstreamAuthenticationService = upstreamAuthenticationService;
        _diagnosticRunRecorder = diagnosticRunRecorder;
        _logger = logger;
    }

    public async Task<ConnectivityTestResult> TestAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        var run = await _diagnosticRunRecorder.StartRunAsync(
            DiagnosticRunKind.Connectivity,
            sessionId: null,
            requestedBy: null,
            cancellationToken);

        DiagnosticStage? currentStage = null;
        var stageSequence = 0;

        if (string.IsNullOrWhiteSpace(configuration.UpstreamHost))
        {
            _logger.LogWarning("Upstream connectivity test rejected because upstream host is not configured.");
            currentStage = await StartStageAsync(run.Id, ++stageSequence, "Configuration", "Checking upstream configuration.", cancellationToken);
            await FailStageAsync(currentStage, "Upstream host is not configured.", cancellationToken);
            await _diagnosticRunRecorder.CompleteRunAsync(run.Id, false, "Upstream host is not configured.", cancellationToken);

            return new ConnectivityTestResult
            {
                Succeeded = false,
                Message = "Upstream host is not configured.",
                Recommendation = "Configure the upstream relay host and port under Relay Settings.",
                DiagnosticRunId = run.Id
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Starting upstream connectivity test. UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; TlsMode={TlsMode}; AuthEnabled={AuthEnabled}; TimeoutSeconds={TimeoutSeconds}",
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                configuration.UseUpstreamAuthentication,
                configuration.UpstreamTimeoutSeconds);

            using var client = new SmtpClient();
            client.Timeout = configuration.UpstreamTimeoutSeconds * 1000;

            currentStage = await StartStageAsync(run.Id, ++stageSequence, "Configuration", "Checking upstream configuration.", cancellationToken);
            await SucceedStageAsync(currentStage, "Upstream configuration is present.", cancellationToken);

            currentStage = await StartStageAsync(run.Id, ++stageSequence, "Connect/TLS", "Connecting to upstream relay.", cancellationToken);
            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);
            await SucceedStageAsync(
                currentStage,
                client.IsSecure ? "Connected with TLS." : "Connected without TLS.",
                cancellationToken);

            currentStage = await StartStageAsync(run.Id, ++stageSequence, "Authentication", "Checking upstream authentication.", cancellationToken);
            await _upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);
            await SucceedStageAsync(
                currentStage,
                configuration.UseUpstreamAuthentication ? "Authentication succeeded." : "Authentication skipped.",
                cancellationToken);

            currentStage = await StartStageAsync(run.Id, ++stageSequence, "Disconnect", "Disconnecting from upstream relay.", cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            await SucceedStageAsync(currentStage, "Disconnected cleanly.", cancellationToken);

            _logger.LogInformation(
                "Upstream connectivity test succeeded. UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; ElapsedMs={ElapsedMs}",
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                stopwatch.ElapsedMilliseconds);

            await _diagnosticRunRecorder.CompleteRunAsync(run.Id, true, "Connected to the upstream relay successfully.", cancellationToken);

            return new ConnectivityTestResult
            {
                Succeeded = true,
                Message = "Connected to the upstream relay successfully.",
                Recommendation = "No change is needed for the upstream connection.",
                DiagnosticRunId = run.Id
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Upstream connectivity test failed. UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; ElapsedMs={ElapsedMs}",
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                stopwatch.ElapsedMilliseconds);

            if (currentStage is not null)
            {
                await FailStageAsync(currentStage, exception.Message, cancellationToken);
            }

            await _diagnosticRunRecorder.CompleteRunAsync(run.Id, false, exception.Message, cancellationToken);

            return new ConnectivityTestResult
            {
                Succeeded = false,
                Message = exception.Message,
                Recommendation = Recommend(exception),
                DiagnosticRunId = run.Id
            };
        }
    }

    private static string Recommend(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credentials", StringComparison.OrdinalIgnoreCase)
            || message.Contains("oauth", StringComparison.OrdinalIgnoreCase))
        {
            return "Review the upstream authentication mode, mailbox, credentials, and Microsoft 365 OAuth consent.";
        }

        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ssl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("tls", StringComparison.OrdinalIgnoreCase))
        {
            return "Review the upstream TLS mode and certificate trust from this host.";
        }

        if (exception is TimeoutException
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No such host", StringComparison.OrdinalIgnoreCase))
        {
            return "Check DNS, firewall rules, routing, and whether the upstream host accepts connections on the configured port.";
        }

        return "Check the upstream relay settings and the application logs for the failed stage.";
    }

    private Task<DiagnosticStage> StartStageAsync(
        Guid runId,
        int sequence,
        string name,
        string message,
        CancellationToken cancellationToken)
    {
        return _diagnosticRunRecorder.StartStageAsync(runId, sequence, name, message, cancellationToken);
    }

    private Task SucceedStageAsync(DiagnosticStage stage, string message, CancellationToken cancellationToken)
    {
        return _diagnosticRunRecorder.CompleteStageAsync(
            stage.Id,
            DiagnosticStageStatus.Succeeded,
            message,
            detail: null,
            cancellationToken);
    }

    private Task FailStageAsync(DiagnosticStage stage, string message, CancellationToken cancellationToken)
    {
        return _diagnosticRunRecorder.CompleteStageAsync(
            stage.Id,
            DiagnosticStageStatus.Failed,
            message,
            detail: null,
            cancellationToken);
    }
}
