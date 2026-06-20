using MailKit.Security;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Configuration;

public sealed class RelayConfigurationSnapshot
{
    public string ListenerBindAddress { get; init; } = "0.0.0.0";

    public int ListenerPort { get; init; } = 25;

    public string ListenerHostName { get; init; } = Environment.MachineName;

    public long MaxMessageSizeBytes { get; init; } = 10 * 1024 * 1024;

    public bool EnableStartTls { get; init; }

    public string? CertificatePath { get; init; }

    public string? CertificatePassword { get; init; }

    public string UpstreamHost { get; init; } = string.Empty;

    public int UpstreamPort { get; init; } = 587;

    public SecureSocketOptions UpstreamSecureSocketOptions { get; init; } = SecureSocketOptions.StartTlsWhenAvailable;

    public bool UseUpstreamAuthentication { get; init; }

    public UpstreamAuthenticationMode UpstreamAuthenticationMode { get; init; } = UpstreamAuthenticationMode.Basic;

    public string? UpstreamUserName { get; init; }

    public string? UpstreamPassword { get; init; }

    public string? MicrosoftTenantId { get; init; }

    public string? MicrosoftClientId { get; init; }

    public string? MicrosoftClientSecret { get; init; }

    public int UpstreamTimeoutSeconds { get; init; } = 30;

    public int DeliveryConcurrency { get; init; } = 1;

    public int MaxRetryCount { get; init; } = 5;

    public int InitialRetryDelaySeconds { get; init; } = 60;

    public int MaxRetryDelaySeconds { get; init; } = 3600;

    public int MessageExpirationHours { get; init; } = 72;

    public int DeliveredRetentionHours { get; init; } = 24;

    public int FailedRetentionHours { get; init; } = 168;

    public int EventRetentionHours { get; init; } = 720;
}
