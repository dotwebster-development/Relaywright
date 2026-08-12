using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Tests.Support;

internal sealed class ConfigurationSnapshotTestContext : IAsyncDisposable
{
    private ConfigurationSnapshotTestContext(
        SqliteTestStore database,
        TempAppData appData,
        AdminWebListenerConfigurationService adminWebListenerConfigurationService,
        RuntimeConfigurationNotifier notifier,
        RecordingQueueSignal queueSignal,
        RecordingApplicationRestartService restartService,
        RecordingOperationalEventService events,
        ConfigurationSnapshotService service,
        ConfigurationSnapshotRestorer restorer)
    {
        Database = database;
        AppData = appData;
        AdminWebListenerConfigurationService = adminWebListenerConfigurationService;
        Notifier = notifier;
        QueueSignal = queueSignal;
        RestartService = restartService;
        Events = events;
        Service = service;
        Restorer = restorer;
    }

    public SqliteTestStore Database { get; }

    public TempAppData AppData { get; }

    public AdminWebListenerConfigurationService AdminWebListenerConfigurationService { get; }

    public RuntimeConfigurationNotifier Notifier { get; }

    public RecordingQueueSignal QueueSignal { get; }

    public RecordingApplicationRestartService RestartService { get; }

    public RecordingOperationalEventService Events { get; }

    public ConfigurationSnapshotService Service { get; }

    public ConfigurationSnapshotRestorer Restorer { get; }

    public static async Task<ConfigurationSnapshotTestContext> CreateAsync(bool seedRelayConfiguration = false)
    {
        var database = await SqliteTestStore.CreateAsync(seedRelayConfiguration);
        var appData = TempAppData.Create();
        var adminWebListenerConfigurationService = new AdminWebListenerConfigurationService(
            appData.Paths,
            NullLogger<AdminWebListenerConfigurationService>.Instance);
        var notifier = new RuntimeConfigurationNotifier();
        var queueSignal = new RecordingQueueSignal();
        var restartService = new RecordingApplicationRestartService();
        var events = new RecordingOperationalEventService();
        var serializer = new ConfigurationSnapshotSerializer();
        var payloadFactory = new ConfigurationSnapshotPayloadFactory(
            database.DbContextFactory,
            adminWebListenerConfigurationService,
            serializer);
        var restorer = new ConfigurationSnapshotRestorer(
            database.DbContextFactory,
            adminWebListenerConfigurationService,
            appData.Paths,
            notifier,
            queueSignal,
            restartService,
            serializer);
        var service = new ConfigurationSnapshotService(
            database.DbContextFactory,
            payloadFactory,
            restorer,
            events,
            NullLogger<ConfigurationSnapshotService>.Instance);

        return new ConfigurationSnapshotTestContext(
            database,
            appData,
            adminWebListenerConfigurationService,
            notifier,
            queueSignal,
            restartService,
            events,
            service,
            restorer);
    }

    public async ValueTask DisposeAsync()
    {
        AppData.Dispose();
        await Database.DisposeAsync();
    }
}

internal sealed class RecordingApplicationRestartService : IApplicationRestartService
{
    public List<(string Reason, string? UserName)> Requests { get; } = [];

    public Task<ApplicationRestartRequestResult> RequestRestartAsync(
        string reason,
        string? userName,
        CancellationToken cancellationToken)
    {
        Requests.Add((reason, userName));
        return Task.FromResult(new ApplicationRestartRequestResult
        {
            RestartScheduled = false,
            RestartSupported = false,
            Message = "Restart not scheduled."
        });
    }

    public Task ClearAppliedRestartIfNeededAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
