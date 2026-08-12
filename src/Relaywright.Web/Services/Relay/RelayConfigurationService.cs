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
    ILogger<RelayConfigurationService> logger,
    RelayConfigurationMapper? configurationMapper = null) : IRelayConfigurationService
{
    private readonly RelayConfigurationMapper mapper = configurationMapper ?? new RelayConfigurationMapper(secretProtector);

    public async Task<RelayConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Loading relay configuration snapshot.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RelayConfigurations
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var snapshot = mapper.ToSnapshot(entity);
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

        return RelayConfigurationMapper.ToEditModel(entity);
    }

    public async Task SaveAsync(RelayConfigurationEditModel model, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RelayConfigurations.SingleAsync(cancellationToken);

        RelayConfigurationValidator.Validate(
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

}
