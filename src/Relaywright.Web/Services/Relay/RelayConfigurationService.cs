using System.Net;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Relay;

public sealed class RelayConfigurationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ISecretProtector secretProtector,
    IOperationalEventService eventService,
    IRuntimeConfigurationNotifier runtimeConfigurationNotifier,
    IQueueSignal queueSignal,
    ILogger<RelayConfigurationService> logger) : IRelayConfigurationService
{
    public async Task<RelayConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading relay configuration snapshot.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RelayConfigurations
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var snapshot = MapToSnapshot(entity);
        logger.LogDebug(
            "Loaded relay configuration snapshot. Listener={ListenerBindAddress}:{ListenerPort}; UpstreamConfigured={UpstreamConfigured}; AuthEnabled={AuthEnabled}; AuthMode={AuthMode}; DeliveryConcurrency={DeliveryConcurrency}",
            snapshot.ListenerBindAddress,
            snapshot.ListenerPort,
            !string.IsNullOrWhiteSpace(snapshot.UpstreamHost),
            snapshot.UseUpstreamAuthentication,
            snapshot.UpstreamAuthenticationMode,
            snapshot.DeliveryConcurrency);

        return snapshot;
    }

    public async Task<RelayConfigurationEditModel> GetEditModelAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading relay configuration edit model.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RelayConfigurations
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        return new RelayConfigurationEditModel
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

    public async Task SaveAsync(RelayConfigurationEditModel model, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RelayConfigurations.SingleAsync(cancellationToken);

        Validate(
            model,
            hasExistingUpstreamPassword: !string.IsNullOrWhiteSpace(entity.ProtectedUpstreamPassword),
            hasExistingMicrosoftClientSecret: !string.IsNullOrWhiteSpace(entity.ProtectedMicrosoftClientSecret));

        entity.ListenerBindAddress = model.ListenerBindAddress.Trim();
        entity.ListenerPort = model.ListenerPort;
        entity.ListenerHostName = model.ListenerHostName.Trim();
        entity.MaxMessageSizeBytes = model.MaxMessageSizeBytes;
        entity.EnableStartTls = model.EnableStartTls;
        entity.CertificatePath = string.IsNullOrWhiteSpace(model.CertificatePath) ? null : model.CertificatePath.Trim();
        entity.UpstreamHost = model.UpstreamHost.Trim();
        entity.UpstreamPort = model.UpstreamPort;
        entity.UpstreamSecureSocketOptions = model.UpstreamSecureSocketOptions;
        entity.UseUpstreamAuthentication = model.UseUpstreamAuthentication;
        if (model.UpstreamAuthenticationMode is not null)
        {
            entity.UpstreamAuthenticationMode = model.UpstreamAuthenticationMode.Value;
        }
        entity.UpstreamUserName = string.IsNullOrWhiteSpace(model.UpstreamUserName) ? null : model.UpstreamUserName.Trim();
        entity.MicrosoftTenantId = string.IsNullOrWhiteSpace(model.MicrosoftTenantId) ? null : model.MicrosoftTenantId.Trim();
        entity.MicrosoftClientId = string.IsNullOrWhiteSpace(model.MicrosoftClientId) ? null : model.MicrosoftClientId.Trim();
        entity.UpstreamTimeoutSeconds = model.UpstreamTimeoutSeconds;
        entity.DeliveryConcurrency = model.DeliveryConcurrency;
        entity.MaxRetryCount = model.MaxRetryCount;
        entity.InitialRetryDelaySeconds = model.InitialRetryDelaySeconds;
        entity.MaxRetryDelaySeconds = model.MaxRetryDelaySeconds;
        entity.MessageExpirationHours = model.MessageExpirationHours;
        entity.DeliveredRetentionHours = model.DeliveredRetentionHours;
        entity.FailedRetentionHours = model.FailedRetentionHours;
        entity.EventRetentionHours = model.EventRetentionHours;
        entity.UpdatedUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(model.CertificatePassword))
        {
            entity.ProtectedCertificatePassword = secretProtector.Protect(model.CertificatePassword);
        }

        if (!string.IsNullOrWhiteSpace(model.UpstreamPassword))
        {
            entity.ProtectedUpstreamPassword = secretProtector.Protect(model.UpstreamPassword);
        }

        if (!string.IsNullOrWhiteSpace(model.MicrosoftClientSecret))
        {
            entity.ProtectedMicrosoftClientSecret = secretProtector.Protect(model.MicrosoftClientSecret);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var configurationVersion = runtimeConfigurationNotifier.NotifySmtpSettingsChanged();
        queueSignal.Pulse();

        logger.LogInformation(
            "Relay configuration saved. Listener={ListenerBindAddress}:{ListenerPort}; StartTls={StartTls}; CertificateConfigured={CertificateConfigured}; UpstreamConfigured={UpstreamConfigured}; Upstream={UpstreamHost}:{UpstreamPort}; TlsMode={TlsMode}; AuthEnabled={AuthEnabled}; AuthMode={AuthMode}; DeliveryConcurrency={DeliveryConcurrency}; MaxRetryCount={MaxRetryCount}; ConfigVersion={ConfigVersion}; CertificateSecretUpdated={CertificateSecretUpdated}; UpstreamPasswordUpdated={UpstreamPasswordUpdated}; MicrosoftSecretUpdated={MicrosoftSecretUpdated}",
            entity.ListenerBindAddress,
            entity.ListenerPort,
            entity.EnableStartTls,
            !string.IsNullOrWhiteSpace(entity.CertificatePath),
            !string.IsNullOrWhiteSpace(entity.UpstreamHost),
            entity.UpstreamHost,
            entity.UpstreamPort,
            entity.UpstreamSecureSocketOptions,
            entity.UseUpstreamAuthentication,
            entity.UpstreamAuthenticationMode,
            entity.DeliveryConcurrency,
            entity.MaxRetryCount,
            configurationVersion,
            !string.IsNullOrWhiteSpace(model.CertificatePassword),
            !string.IsNullOrWhiteSpace(model.UpstreamPassword),
            !string.IsNullOrWhiteSpace(model.MicrosoftClientSecret));

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Configuration,
            Message = OperationalEventMessages.RelayConfigurationUpdated
        }, cancellationToken);
    }

    private RelayConfigurationSnapshot MapToSnapshot(RelayConfiguration entity)
    {
        return new RelayConfigurationSnapshot
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
    }

    private static void Validate(
        RelayConfigurationEditModel model,
        bool hasExistingUpstreamPassword,
        bool hasExistingMicrosoftClientSecret)
    {
        if (string.IsNullOrWhiteSpace(model.ListenerBindAddress))
        {
            throw new InvalidOperationException("Listener bind address is required.");
        }

        if (!IPAddress.TryParse(model.ListenerBindAddress, out _))
        {
            throw new InvalidOperationException("Listener bind address must be a valid IP address.");
        }

        if (model.ListenerPort is < ValidationLimits.MinimumPort or > ValidationLimits.MaximumPort)
        {
            throw new InvalidOperationException(ValidationMessages.PortRange("Listener port"));
        }

        if (string.IsNullOrWhiteSpace(model.ListenerHostName))
        {
            throw new InvalidOperationException("Listener host name is required.");
        }

        ValidateHostOrIp(model.ListenerHostName, "Listener host name");

        if (model.MaxMessageSizeBytes < ValidationLimits.MinimumMessageSizeBytes)
        {
            throw new InvalidOperationException(
                ValidationMessages.AtLeast(
                    "Maximum message size",
                    ValidationLimits.MinimumMessageSizeBytes,
                    "bytes"));
        }

        if (model.EnableStartTls)
        {
            if (string.IsNullOrWhiteSpace(model.CertificatePath))
            {
                throw new InvalidOperationException("Certificate path is required when STARTTLS is enabled.");
            }

            ValidateFileExtension(
                model.CertificatePath,
                "Certificate path",
                ValidationLimits.CertificateFileExtensions);
        }
        else if (!string.IsNullOrWhiteSpace(model.CertificatePath))
        {
            ValidateFileExtension(
                model.CertificatePath,
                "Certificate path",
                ValidationLimits.CertificateFileExtensions);
        }

        ValidateOptionalSecret(model.CertificatePassword, "Certificate password");

        if (!string.IsNullOrWhiteSpace(model.UpstreamHost))
        {
            ValidateHostOrIp(model.UpstreamHost, "Upstream host");
        }

        if (model.UpstreamPort is < ValidationLimits.MinimumPort or > ValidationLimits.MaximumPort)
        {
            throw new InvalidOperationException(ValidationMessages.PortRange("Upstream port"));
        }

        if (model.DeliveryConcurrency < 1)
        {
            throw new InvalidOperationException("Delivery concurrency must be at least 1.");
        }

        if (model.MaxRetryCount < 1)
        {
            throw new InvalidOperationException("Maximum retry count must be at least 1.");
        }

        if (model.InitialRetryDelaySeconds < 1)
        {
            throw new InvalidOperationException("Initial retry delay must be at least 1 second.");
        }

        if (model.MaxRetryDelaySeconds < model.InitialRetryDelaySeconds)
        {
            throw new InvalidOperationException("Maximum retry delay must be greater than or equal to the initial retry delay.");
        }

        if (model.MessageExpirationHours < 1)
        {
            throw new InvalidOperationException("Message expiration must be at least 1 hour.");
        }

        if (model.DeliveredRetentionHours < 1)
        {
            throw new InvalidOperationException("Delivered retention must be at least 1 hour.");
        }

        if (model.FailedRetentionHours < 1)
        {
            throw new InvalidOperationException("Failed retention must be at least 1 hour.");
        }

        if (model.EventRetentionHours < 1)
        {
            throw new InvalidOperationException("Event retention must be at least 1 hour.");
        }

        if (model.UpstreamTimeoutSeconds < 5)
        {
            throw new InvalidOperationException("Upstream timeout must be at least 5 seconds.");
        }

        ValidateOptionalSecret(model.UpstreamPassword, "Upstream password");
        ValidateOptionalSecret(model.MicrosoftClientSecret, "Microsoft client secret");

        if (!model.UseUpstreamAuthentication)
        {
            return;
        }

        if (model.UpstreamSecureSocketOptions is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect))
        {
            throw new InvalidOperationException("Upstream authentication requires a guaranteed TLS mode: StartTls or SslOnConnect.");
        }

        if (string.IsNullOrWhiteSpace(model.UpstreamUserName))
        {
            throw new InvalidOperationException("Authentication requires a user name or mailbox.");
        }

        ValidateSingleLineText(model.UpstreamUserName, "Authentication user name");

        if (model.UpstreamAuthenticationMode is null)
        {
            throw new InvalidOperationException("Select an upstream authentication type.");
        }

        switch (model.UpstreamAuthenticationMode.Value)
        {
            case UpstreamAuthenticationMode.Basic:
                if (string.IsNullOrWhiteSpace(model.UpstreamPassword) && !hasExistingUpstreamPassword)
                {
                    throw new InvalidOperationException("Basic authentication requires a password.");
                }
                break;

            case UpstreamAuthenticationMode.Microsoft365OAuth:
                if (string.IsNullOrWhiteSpace(model.MicrosoftTenantId))
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth requires a tenant ID.");
                }

                if (!ValidationRules.IsMicrosoftTenantId(model.MicrosoftTenantId.Trim()))
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth tenant ID must be a tenant GUID or tenant domain.");
                }

                if (string.IsNullOrWhiteSpace(model.MicrosoftClientId))
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth requires a client ID.");
                }

                if (!ValidationRules.IsGuidText(model.MicrosoftClientId.Trim()))
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth client ID must be a GUID.");
                }

                if (!ValidationRules.IsMailboxAddress(model.UpstreamUserName.Trim()))
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth requires a mailbox-style upstream user name.");
                }

                if (string.IsNullOrWhiteSpace(model.MicrosoftClientSecret) && !hasExistingMicrosoftClientSecret)
                {
                    throw new InvalidOperationException("Microsoft 365 OAuth requires a client secret.");
                }
                break;

            default:
                throw new InvalidOperationException("The selected upstream authentication mode is not supported.");
        }
    }

    private static void ValidateHostOrIp(string value, string label)
    {
        if (!ValidationRules.IsHostNameOrIpAddress(value.Trim()))
        {
            throw new InvalidOperationException(ValidationMessages.HostNameOrIpAddress(label));
        }
    }

    private static void ValidateFileExtension(string value, string label, IReadOnlyCollection<string> extensions)
    {
        if (!ValidationRules.HasAllowedExtension(value, extensions))
        {
            throw new InvalidOperationException(ValidationMessages.FileExtension(label, extensions));
        }
    }

    private static void ValidateSingleLineText(string? value, string label)
    {
        if (ValidationRules.ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _))
        {
            throw new InvalidOperationException(ValidationMessages.UnsupportedControlCharacter(label));
        }
    }

    private static void ValidateOptionalSecret(string? value, string label)
    {
        if (value is not null && value.Length > ValidationLimits.MaximumSecretLength)
        {
            throw new InvalidOperationException(ValidationMessages.MaximumLength(label, ValidationLimits.MaximumSecretLength));
        }

        ValidateSingleLineText(value, label);
    }
}
