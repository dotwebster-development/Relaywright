using MailKit.Net.Smtp;
using MimeKit;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;

namespace Relaywright.Web.Services.Alerts;

public sealed class AlertEmailNotifier(
    IUpstreamAuthenticationService upstreamAuthenticationService,
    ILogger<AlertEmailNotifier> logger) : IAlertEmailNotifier
{
    public async Task<AlertNotificationResult> SendAsync(
        AlertRule rule,
        string message,
        RelayConfigurationSnapshot configuration,
        CancellationToken cancellationToken)
    {
        var recipients = ParseRecipients(rule.EmailRecipients);
        if (recipients.Count == 0)
        {
            return new AlertNotificationResult
            {
                Succeeded = false,
                Message = "No alert email recipients are configured."
            };
        }

        if (string.IsNullOrWhiteSpace(configuration.UpstreamHost))
        {
            return new AlertNotificationResult
            {
                Succeeded = false,
                Message = "Upstream host is not configured."
            };
        }

        using var client = new SmtpClient();
        try
        {
            var fromAddress = !string.IsNullOrWhiteSpace(configuration.UpstreamUserName)
                && configuration.UpstreamUserName.Contains('@', StringComparison.Ordinal)
                    ? configuration.UpstreamUserName
                    : "relaywright-alert@localhost";

            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(MailboxAddress.Parse(fromAddress));
            foreach (var recipient in recipients)
            {
                mimeMessage.To.Add(MailboxAddress.Parse(recipient));
            }

            mimeMessage.Subject = $"Relaywright alert: {rule.DisplayName}";
            mimeMessage.Body = new TextPart("plain")
            {
                Text = message
            };

            client.Timeout = configuration.UpstreamTimeoutSeconds * 1000;
            await client.ConnectAsync(
                configuration.UpstreamHost,
                configuration.UpstreamPort,
                configuration.UpstreamSecureSocketOptions,
                cancellationToken);

            await upstreamAuthenticationService.AuthenticateAsync(client, configuration, cancellationToken);
            var response = await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation(
                "Alert email sent. AlertRuleKey={AlertRuleKey}; RecipientCount={RecipientCount}; Response={Response}",
                rule.Key,
                recipients.Count,
                response);

            return new AlertNotificationResult
            {
                Succeeded = true,
                Message = response
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Alert email failed. AlertRuleKey={AlertRuleKey}; RecipientCount={RecipientCount}",
                rule.Key,
                recipients.Count);

            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
                catch (Exception disconnectException)
                {
                    logger.LogDebug(disconnectException, "Alert email disconnect cleanup failed.");
                }
            }

            return new AlertNotificationResult
            {
                Succeeded = false,
                Message = exception.Message
            };
        }
    }

    private static IReadOnlyList<string> ParseRecipients(string? recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return [];
        }

        return recipients
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
