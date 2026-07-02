using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RelayConfigurationIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveProtectsSecretsInDatabaseAndUnprotectsSnapshots()
    {
        await using var database = await SqliteTestStore.CreateAsync(seedRelayConfiguration: true);
        using var appData = TempAppData.Create();
        var service = CreateService(database, appData, out _, out _);

        await service.SaveAsync(CreateModel(
            certificatePassword: "cert-secret",
            upstreamPassword: "smtp-secret",
            microsoftSecret: "oauth-secret"), CancellationToken.None);

        await using (var dbContext = database.CreateDbContext())
        {
            var stored = await dbContext.RelayConfigurations.AsNoTracking().SingleAsync();
            Assert.DoesNotContain("cert-secret", stored.ProtectedCertificatePassword, StringComparison.Ordinal);
            Assert.DoesNotContain("smtp-secret", stored.ProtectedUpstreamPassword, StringComparison.Ordinal);
            Assert.DoesNotContain("oauth-secret", stored.ProtectedMicrosoftClientSecret, StringComparison.Ordinal);
        }

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal("cert-secret", snapshot.CertificatePassword);
        Assert.Equal("smtp-secret", snapshot.UpstreamPassword);
        Assert.Equal("oauth-secret", snapshot.MicrosoftClientSecret);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BlankSecretFieldsPreserveExistingProtectedValuesAndNotifyRuntime()
    {
        await using var database = await SqliteTestStore.CreateAsync();
        using var appData = TempAppData.Create();
        var service = CreateService(database, appData, out var notifier, out var signal);

        await using (var dbContext = database.CreateDbContext())
        {
            var protector = CreateSecretProtector(appData);
            var configuration = TestData.RelayConfiguration();
            configuration.ProtectedCertificatePassword = protector.Protect("existing-cert");
            configuration.ProtectedUpstreamPassword = protector.Protect("existing-smtp");
            configuration.ProtectedMicrosoftClientSecret = protector.Protect("existing-oauth");
            configuration.UseUpstreamAuthentication = true;
            configuration.UpstreamAuthenticationMode = UpstreamAuthenticationMode.Basic;
            configuration.UpstreamUserName = "relay@example.test";
            dbContext.RelayConfigurations.Add(configuration);
            await dbContext.SaveChangesAsync();
        }

        var beforeVersion = notifier.CurrentVersion;
        await service.SaveAsync(CreateModel(), CancellationToken.None);

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal("existing-cert", snapshot.CertificatePassword);
        Assert.Equal("existing-smtp", snapshot.UpstreamPassword);
        Assert.Equal("existing-oauth", snapshot.MicrosoftClientSecret);
        Assert.True(notifier.CurrentVersion > beforeVersion);
        Assert.Equal(1, signal.PulseCount);
    }

    private static RelayConfigurationService CreateService(
        SqliteTestStore database,
        TempAppData appData,
        out RuntimeConfigurationNotifier notifier,
        out RecordingQueueSignal signal)
    {
        notifier = new RuntimeConfigurationNotifier();
        signal = new RecordingQueueSignal();
        return new RelayConfigurationService(
            database.DbContextFactory,
            CreateSecretProtector(appData),
            new RecordingOperationalEventService(),
            notifier,
            signal,
            NullLogger<RelayConfigurationService>.Instance);
    }

    private static DataProtectionSecretProtector CreateSecretProtector(TempAppData appData)
    {
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(appData.Paths.KeyRingDirectory),
            builder => builder.SetApplicationName("Relaywright"));
        return new DataProtectionSecretProtector(
            provider,
            NullLogger<DataProtectionSecretProtector>.Instance);
    }

    private static RelayConfigurationEditModel CreateModel(
        string? certificatePassword = null,
        string? upstreamPassword = null,
        string? microsoftSecret = null)
    {
        return new RelayConfigurationEditModel
        {
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            MaxMessageSizeBytes = 1024 * 1024,
            EnableStartTls = true,
            CertificatePath = "relaywright.pfx",
            CertificatePassword = certificatePassword,
            UpstreamHost = "smtp.example.test",
            UpstreamPort = 587,
            UpstreamSecureSocketOptions = SecureSocketOptions.StartTls,
            UseUpstreamAuthentication = true,
            UpstreamAuthenticationMode = UpstreamAuthenticationMode.Microsoft365OAuth,
            UpstreamUserName = "relay@example.test",
            UpstreamPassword = upstreamPassword,
            MicrosoftTenantId = "tenant-id",
            MicrosoftClientId = "client-id",
            MicrosoftClientSecret = microsoftSecret,
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
}
