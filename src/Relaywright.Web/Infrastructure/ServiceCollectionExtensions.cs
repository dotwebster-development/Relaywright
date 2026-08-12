using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Smtp;
using Relaywright.Web.Services.Updates;

namespace Relaywright.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRelaywrightApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        return services
            .AddRelaywrightEventAndRuntimeServices()
            .AddRelaywrightSecurityAndConfigurationServices()
            .AddRelaywrightQueueAndDeliveryServices()
            .AddRelaywrightBackupAndAlertServices()
            .AddRelaywrightDiagnosticAndUpdateServices()
            .AddRelaywrightSmtpAndPersistenceServices();
    }

    public static IServiceCollection AddRelaywrightEventAndRuntimeServices(this IServiceCollection services)
    {
        services.AddSingleton<IOperationalEventService, OperationalEventService>();
        services.AddSingleton<OperationalLogQueryService>();
        services.AddSingleton<IRuntimeStatusService, RuntimeStatusService>();
        services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
        services.AddSingleton<IOutboundRouteProbe, OutboundRouteProbe>();
        services.AddSingleton<IDashboardMetricsService, DashboardMetricsService>();
        services.AddSingleton<IDashboardReadinessService, DashboardReadinessService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<DetailedHealthService>();
        return services;
    }

    public static IServiceCollection AddRelaywrightSecurityAndConfigurationServices(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<AdminHttpsCertificateConfigurationStore>();
        services.AddSingleton<AdminHttpsCertificateFileStore>();
        services.AddSingleton<AdminHttpsCertificateMaterialService>();
        services.AddSingleton<IAdminHttpsCertificateService, AdminHttpsCertificateService>();
        services.AddSingleton<CertificateFormValidator>();
        services.AddSingleton<IAdminWebListenerConfigurationService, AdminWebListenerConfigurationService>();
        services.AddSingleton<IAdminSecurityActivityService, AdminSecurityActivityService>();
        services.AddScoped<AdminAccountService>();
        services.AddScoped<FirstRunSetupService>();
        services.AddSingleton<IRuntimeConfigurationNotifier, RuntimeConfigurationNotifier>();
        services.AddSingleton<RelayConfigurationMapper>();
        services.AddSingleton<IRelayConfigurationService, RelayConfigurationService>();
        services.AddSingleton<SubmissionPolicyEvaluator>();
        services.AddSingleton<SubmissionPolicyValidator>();
        services.AddSingleton<ITrustedNetworkService, TrustedNetworkService>();
        services.AddSingleton<ITrustedDevicePolicyService, TrustedDevicePolicyService>();
        services.AddSingleton<ITrustedDeviceRateLimiter, TrustedDeviceRateLimiter>();
        services.AddSingleton<ConfigurationSnapshotSerializer>();
        services.AddSingleton<ConfigurationSnapshotPayloadFactory>();
        services.AddSingleton<ConfigurationSnapshotRestorer>();
        services.AddSingleton<IConfigurationSnapshotService, ConfigurationSnapshotService>();
        return services;
    }

    public static IServiceCollection AddRelaywrightQueueAndDeliveryServices(this IServiceCollection services)
    {
        services.AddSingleton<IQueueSignal, QueueSignal>();
        services.AddSingleton<ISpoolFileSystem, PhysicalSpoolFileSystem>();
        services.AddSingleton<IMessageSpoolService, MessageSpoolService>();
        services.AddSingleton<IMessageMetadataService, MessageMetadataService>();
        services.AddSingleton<RetryDelayCalculator>();
        services.AddSingleton<QueueClaimService>();
        services.AddSingleton<QueueDeliveryStateService>();
        services.AddSingleton<MessageQueueService>();
        services.AddSingleton<IMessageQueueService>(provider => provider.GetRequiredService<MessageQueueService>());
        services.AddSingleton<IQueueOperatorService, QueueOperatorService>();
        services.AddSingleton<IQueueMaintenanceService, QueueMaintenanceService>();
        services.AddSingleton<QueueQueryService>();
        services.AddSingleton<DeliveryFailureClassifier>();
        services.AddSingleton<MicrosoftOAuthTokenProvider>();
        services.AddSingleton<IUpstreamAuthenticationService, UpstreamAuthenticationService>();
        services.AddSingleton<IUpstreamDiagnosticSmtpSessionFactory, UpstreamDiagnosticSmtpSessionFactory>();
        services.AddSingleton<IUpstreamDeliveryService, UpstreamDeliveryService>();
        return services;
    }

    public static IServiceCollection AddRelaywrightBackupAndAlertServices(this IServiceCollection services)
    {
        services.AddSingleton<IBackupCoordinator, BackupCoordinator>();
        services.AddSingleton<IBackupFileSystem, PhysicalBackupFileSystem>();
        services.AddSingleton<BackupFileStore>();
        services.AddSingleton<BackupArchiveService>();
        services.AddSingleton<BackupRunRepository>();
        services.AddSingleton<BackupScheduleRepository>();
        services.AddSingleton<BackupRestoreArchiveExtractor>();
        services.AddSingleton<RestoredBackupValidator>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<IAlertEmailNotifier, AlertEmailNotifier>();
        services.AddSingleton<AlertRuleRepository>();
        services.AddSingleton<AlertEvaluator>();
        services.AddSingleton<AlertStateCoordinator>();
        services.AddSingleton<IAlertService, AlertService>();
        return services;
    }

    public static IServiceCollection AddRelaywrightDiagnosticAndUpdateServices(this IServiceCollection services)
    {
        services.AddSingleton<IDiagnosticRunRecorder, DiagnosticRunRecorder>();
        services.AddSingleton<IUpstreamConnectivityTester, UpstreamConnectivityTester>();
        services.AddSingleton<IUpstreamTestEmailSender, UpstreamTestEmailSender>();
        services.AddSingleton<ISubmissionFlowChecker, SubmissionFlowChecker>();
        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        services.AddHttpClient();
        services.AddHttpClient(UpdateCheckService.HttpClientName);
        return services;
    }

    public static IServiceCollection AddRelaywrightSmtpAndPersistenceServices(this IServiceCollection services)
    {
        services.AddSingleton<SmtpOptionsFactory>();
        services.AddSingleton<RelayMessageStore>();
        services.AddSingleton<TrustedNetworkMailboxFilter>();
        services.AddSingleton<ILegacySqliteSchemaStep, RelayPolicySchemaStep>();
        services.AddSingleton<ILegacySqliteSchemaStep, QueueRuntimeSchemaStep>();
        services.AddSingleton<ILegacySqliteSchemaStep, AlertBackupSchemaStep>();
        services.AddSingleton<ILegacySqliteSchemaStep, DiagnosticsHistorySchemaStep>();
        services.AddSingleton<ISqliteSchemaUpgrade, LegacyBaselineSqliteSchemaUpgrade>();
        services.AddSingleton<DatabaseSchemaInitializer>();
        services.AddSingleton<DataSeeder>();
        return services;
    }

    public static IServiceCollection AddRelaywrightHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<SmtpRelayHostedService>();
        services.AddHostedService<QueueDeliveryWorker>();
        services.AddHostedService<MaintenanceWorker>();
        services.AddHostedService<AlertWorker>();
        services.AddHostedService<BackupWorker>();
        services.AddHostedService<UpdateCheckWorker>();
        return services;
    }
}
