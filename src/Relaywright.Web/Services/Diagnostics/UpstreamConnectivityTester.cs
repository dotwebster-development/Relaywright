using System.Diagnostics;
using MailKit.Net.Smtp;
using Relaywright.Web.Configuration;
using Relaywright.Web.Services.Delivery;

namespace Relaywright.Web.Services.Diagnostics;

public sealed class UpstreamConnectivityTester : IUpstreamConnectivityTester
{
    private readonly IUpstreamAuthenticationService _upstreamAuthenticationService;
    private readonly ILogger<UpstreamConnectivityTester> _logger;

    public UpstreamConnectivityTester(
        IUpstreamAuthenticationService upstreamAuthenticationService,
        ILogger<UpstreamConnectivityTester> logger)
    {
        _upstreamAuthenticationService = upstreamAuthenticationService;
        _logger = logger;
    }

    public async Task<ConnectivityTestResult> TestAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.UpstreamHost))
        {
            _logger.LogWarning("Upstream connectivity test rejected because upstream host is not configured.");

            return new ConnectivityTestResult
            {
                Succeeded = false,
                Message = "Upstream host is not configured."
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
            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);

            await _upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "Upstream connectivity test succeeded. UpstreamHost={UpstreamHost}; UpstreamPort={UpstreamPort}; ElapsedMs={ElapsedMs}",
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                stopwatch.ElapsedMilliseconds);

            return new ConnectivityTestResult
            {
                Succeeded = true,
                Message = "Connected to the upstream relay successfully."
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

            return new ConnectivityTestResult
            {
                Succeeded = false,
                Message = exception.Message
            };
        }
    }
}
