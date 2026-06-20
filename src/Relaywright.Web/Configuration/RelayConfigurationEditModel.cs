using MailKit.Security;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Configuration;

public sealed class RelayConfigurationEditModel
{
    public string ListenerBindAddress { get; set; } = "0.0.0.0";

    public int ListenerPort { get; set; } = 25;

    public string ListenerHostName { get; set; } = Environment.MachineName;

    public long MaxMessageSizeBytes { get; set; } = 10 * 1024 * 1024;

    public bool EnableStartTls { get; set; }

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public string UpstreamHost { get; set; } = string.Empty;

    public int UpstreamPort { get; set; } = 587;

    public SecureSocketOptions UpstreamSecureSocketOptions { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;

    public bool UseUpstreamAuthentication { get; set; }

    public UpstreamAuthenticationMode? UpstreamAuthenticationMode { get; set; }

    public string? UpstreamUserName { get; set; }

    public string? UpstreamPassword { get; set; }

    public string? MicrosoftTenantId { get; set; }

    public string? MicrosoftClientId { get; set; }

    public string? MicrosoftClientSecret { get; set; }

    public int UpstreamTimeoutSeconds { get; set; } = 30;

    public int DeliveryConcurrency { get; set; } = 1;

    public int MaxRetryCount { get; set; } = 5;

    public int InitialRetryDelaySeconds { get; set; } = 60;

    public int MaxRetryDelaySeconds { get; set; } = 3600;

    public int MessageExpirationHours { get; set; } = 72;

    public int DeliveredRetentionHours { get; set; } = 24;

    public int FailedRetentionHours { get; set; } = 168;

    public int EventRetentionHours { get; set; } = 720;
}
