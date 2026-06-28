using System.Text;
using MailKit.Security;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Tests.Support;

internal static class TestData
{
    public static RelayConfiguration RelayConfiguration()
    {
        return new RelayConfiguration
        {
            Id = 1,
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            MaxMessageSizeBytes = 1024 * 1024,
            UpstreamHost = "smtp.example.test",
            UpstreamPort = 587,
            UpstreamSecureSocketOptions = SecureSocketOptions.StartTls,
            UpstreamTimeoutSeconds = 30,
            DeliveryConcurrency = 1,
            MaxRetryCount = 3,
            InitialRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 300,
            MessageExpirationHours = 24,
            DeliveredRetentionHours = 24,
            FailedRetentionHours = 24,
            EventRetentionHours = 24
        };
    }

    public static RelayConfigurationSnapshot Snapshot()
    {
        return new RelayConfigurationSnapshot
        {
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            UpstreamHost = "smtp.example.test",
            UpstreamPort = 587,
            UpstreamSecureSocketOptions = SecureSocketOptions.StartTls,
            UpstreamTimeoutSeconds = 30,
            DeliveryConcurrency = 1,
            MaxRetryCount = 3,
            InitialRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 300,
            MessageExpirationHours = 24,
            DeliveredRetentionHours = 24,
            FailedRetentionHours = 24,
            EventRetentionHours = 24
        };
    }

    public static QueuedMessage QueuedMessage(
        QueuedMessageStatus status = QueuedMessageStatus.Pending,
        DateTimeOffset? acceptedUtc = null,
        string spoolPath = "message.eml")
    {
        var accepted = acceptedUtc ?? DateTimeOffset.UtcNow;
        var message = new QueuedMessage
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            EnvelopeFrom = "sender@example.test",
            SpoolFileRelativePath = spoolPath,
            Status = status,
            AcceptedUtc = accepted,
            CreatedUtc = accepted,
            NextAttemptAtUtc = accepted,
            ExpiresUtc = accepted.AddHours(24)
        };

        message.Recipients.Add(new QueuedMessageRecipient
        {
            RecipientAddress = "recipient@example.test"
        });

        return message;
    }

    public static byte[] MimeBytes(
        string from = "sender@example.test",
        string to = "recipient@example.test",
        string subject = "Relaywright test")
    {
        return Encoding.ASCII.GetBytes($"""
            From: {from}
            To: {to}
            Subject: {subject}
            Message-Id: <{Guid.NewGuid():N}@example.test>
            Date: Tue, 01 Jan 2030 00:00:00 +0000

            Test body.
            """.ReplaceLineEndings("\r\n"));
    }
}
