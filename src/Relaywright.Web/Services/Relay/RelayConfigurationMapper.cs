using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Relay;

public sealed class RelayConfigurationMapper(ISecretProtector secretProtector)
{
    public RelayConfigurationSnapshot ToSnapshot(RelayConfiguration entity) => new()
    {
        UpdatedUtc = entity.UpdatedUtc,
        ListenerBindAddress = entity.ListenerBindAddress,
        ListenerPort = entity.ListenerPort,
        ListenerHostName = entity.ListenerHostName,
        MaxMessageSizeBytes = entity.MaxMessageSizeBytes,
        EnableStartTls = entity.EnableStartTls,
        CertificatePath = entity.CertificatePath,
        CertificatePassword = secretProtector.Unprotect(entity.ProtectedCertificatePassword),
        UpstreamHost = entity.UpstreamHost,
        UpstreamPort = entity.UpstreamPort,
        UpstreamSecureSocketOptions = entity.UpstreamSecureSocketOptions,
        UseUpstreamAuthentication = entity.UseUpstreamAuthentication,
        UpstreamAuthenticationMode = entity.UpstreamAuthenticationMode,
        UpstreamUserName = entity.UpstreamUserName,
        UpstreamPassword = secretProtector.Unprotect(entity.ProtectedUpstreamPassword),
        MicrosoftTenantId = entity.MicrosoftTenantId,
        MicrosoftClientId = entity.MicrosoftClientId,
        MicrosoftClientSecret = secretProtector.Unprotect(entity.ProtectedMicrosoftClientSecret),
        UpstreamTimeoutSeconds = Math.Max(5, entity.UpstreamTimeoutSeconds),
        DeliveryConcurrency = Math.Max(1, entity.DeliveryConcurrency),
        MaxRetryCount = Math.Max(1, entity.MaxRetryCount),
        InitialRetryDelaySeconds = Math.Max(5, entity.InitialRetryDelaySeconds),
        MaxRetryDelaySeconds = Math.Max(entity.InitialRetryDelaySeconds, entity.MaxRetryDelaySeconds),
        MessageExpirationHours = Math.Max(1, entity.MessageExpirationHours),
        DeliveredRetentionHours = Math.Max(1, entity.DeliveredRetentionHours),
        FailedRetentionHours = Math.Max(1, entity.FailedRetentionHours),
        EventRetentionHours = Math.Max(1, entity.EventRetentionHours)
    };

    public static RelayConfigurationEditModel ToEditModel(RelayConfiguration entity) => new()
    {
        ListenerBindAddress = entity.ListenerBindAddress,
        ListenerPort = entity.ListenerPort,
        ListenerHostName = entity.ListenerHostName,
        MaxMessageSizeBytes = entity.MaxMessageSizeBytes,
        EnableStartTls = entity.EnableStartTls,
        CertificatePath = entity.CertificatePath,
        UpstreamHost = entity.UpstreamHost,
        UpstreamPort = entity.UpstreamPort,
        UpstreamSecureSocketOptions = entity.UpstreamSecureSocketOptions,
        UseUpstreamAuthentication = entity.UseUpstreamAuthentication,
        UpstreamAuthenticationMode = entity.UseUpstreamAuthentication ? entity.UpstreamAuthenticationMode : null,
        UpstreamUserName = entity.UpstreamUserName,
        MicrosoftTenantId = entity.MicrosoftTenantId,
        MicrosoftClientId = entity.MicrosoftClientId,
        UpstreamTimeoutSeconds = entity.UpstreamTimeoutSeconds,
        DeliveryConcurrency = entity.DeliveryConcurrency,
        MaxRetryCount = entity.MaxRetryCount,
        InitialRetryDelaySeconds = entity.InitialRetryDelaySeconds,
        MaxRetryDelaySeconds = entity.MaxRetryDelaySeconds,
        MessageExpirationHours = entity.MessageExpirationHours,
        DeliveredRetentionHours = entity.DeliveredRetentionHours,
        FailedRetentionHours = entity.FailedRetentionHours,
        EventRetentionHours = entity.EventRetentionHours
    };
}
