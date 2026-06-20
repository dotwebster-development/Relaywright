using SmtpServer.ComponentModel;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Relay;

namespace Relaywright.Web.Services.Smtp;

public sealed class SmtpRelayHostedService(
    IRelayConfigurationService relayConfigurationService,
    IRuntimeConfigurationNotifier runtimeConfigurationNotifier,
    SmtpOptionsFactory smtpOptionsFactory,
    RelayMessageStore relayMessageStore,
    TrustedNetworkMailboxFilter trustedNetworkMailboxFilter,
    IOperationalEventService eventService,
    ILogger<SmtpRelayHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var knownVersion = runtimeConfigurationNotifier.CurrentVersion;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var configuration = await relayConfigurationService.GetSnapshotAsync(stoppingToken);
                var smtpServer = CreateServer(configuration);
                using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                logger.LogInformation(
                    "Starting SMTP listener. Listener={ListenerBindAddress}:{ListenerPort}; HostName={HostName}; StartTls={StartTls}; MaxMessageSizeBytes={MaxMessageSizeBytes}; KnownConfigVersion={KnownConfigVersion}",
                    configuration.ListenerBindAddress,
                    configuration.ListenerPort,
                    configuration.ListenerHostName,
                    configuration.EnableStartTls,
                    configuration.MaxMessageSizeBytes,
                    knownVersion);

                var runTask = smtpServer.StartAsync(runCts.Token);
                var restartTask = runtimeConfigurationNotifier.WaitForSmtpSettingsChangeAsync(knownVersion, stoppingToken);

                var completedTask = await Task.WhenAny(runTask, restartTask);
                if (completedTask == restartTask)
                {
                    knownVersion = await restartTask;
                    logger.LogInformation("SMTP listener restart requested. NewConfigVersion={ConfigVersion}", knownVersion);

                    await eventService.WriteAsync(new OperationalEventRequest
                    {
                        Category = OperationalEventCategory.Configuration,
                        Message = "Restarting SMTP listener to apply configuration changes."
                    }, stoppingToken);

                    smtpServer.Shutdown();
                    runCts.Cancel();

                    await AwaitServerStopAsync(runTask);
                    logger.LogInformation("SMTP listener stopped for configuration restart. ConfigVersion={ConfigVersion}", knownVersion);
                    continue;
                }

                await runTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "SMTP listener terminated unexpectedly.");

                await eventService.WriteAsync(new OperationalEventRequest
                {
                    Severity = EventSeverity.Error,
                    Category = OperationalEventCategory.System,
                    Message = "SMTP listener terminated unexpectedly.",
                    Detail = exception.ToString()
                }, stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private static async Task AwaitServerStopAsync(Task runTask)
    {
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private SmtpServer.SmtpServer CreateServer(RelayConfigurationSnapshot configuration)
    {
        var options = smtpOptionsFactory.Create(configuration);
        var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
        serviceProvider.Add(trustedNetworkMailboxFilter);
        serviceProvider.Add(relayMessageStore);

        var server = new SmtpServer.SmtpServer(options, serviceProvider);
        server.SessionCreated += OnSessionCreated;
        server.SessionCompleted += OnSessionCompleted;
        server.SessionCancelled += OnSessionCancelled;
        server.SessionFaulted += OnSessionFaulted;
        return server;
    }

    private void OnSessionCreated(object? sender, SmtpServer.SessionEventArgs args)
    {
        logger.LogInformation(
            "SMTP session connected. SessionId={SessionId}; RemoteIp={RemoteIp}",
            args.Context.GetOrCreateSessionId(),
            args.Context.GetRemoteIpAddress()?.ToString());

        _ = WriteSessionEventAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.SmtpSession,
            SessionId = args.Context.GetOrCreateSessionId(),
            RemoteIpAddress = args.Context.GetRemoteIpAddress()?.ToString(),
            Message = "SMTP session connected."
        });
    }

    private void OnSessionCompleted(object? sender, SmtpServer.SessionEventArgs args)
    {
        logger.LogInformation(
            "SMTP session completed. SessionId={SessionId}; RemoteIp={RemoteIp}",
            args.Context.GetOrCreateSessionId(),
            args.Context.GetRemoteIpAddress()?.ToString());

        _ = WriteSessionEventAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.SmtpSession,
            SessionId = args.Context.GetOrCreateSessionId(),
            RemoteIpAddress = args.Context.GetRemoteIpAddress()?.ToString(),
            Message = "SMTP session completed."
        });
    }

    private void OnSessionCancelled(object? sender, SmtpServer.SessionEventArgs args)
    {
        logger.LogWarning(
            "SMTP session cancelled. SessionId={SessionId}; RemoteIp={RemoteIp}",
            args.Context.GetOrCreateSessionId(),
            args.Context.GetRemoteIpAddress()?.ToString());

        _ = WriteSessionEventAsync(new OperationalEventRequest
        {
            Severity = EventSeverity.Warning,
            Category = OperationalEventCategory.SmtpSession,
            SessionId = args.Context.GetOrCreateSessionId(),
            RemoteIpAddress = args.Context.GetRemoteIpAddress()?.ToString(),
            Message = "SMTP session cancelled."
        });
    }

    private void OnSessionFaulted(object? sender, SmtpServer.SessionFaultedEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "SMTP session faulted. SessionId={SessionId}; RemoteIp={RemoteIp}",
            args.Context.GetOrCreateSessionId(),
            args.Context.GetRemoteIpAddress()?.ToString());

        _ = WriteSessionEventAsync(new OperationalEventRequest
        {
            Severity = EventSeverity.Error,
            Category = OperationalEventCategory.SmtpSession,
            SessionId = args.Context.GetOrCreateSessionId(),
            RemoteIpAddress = args.Context.GetRemoteIpAddress()?.ToString(),
            Message = "SMTP session faulted.",
            Detail = args.Exception.ToString()
        });
    }

    private async Task WriteSessionEventAsync(OperationalEventRequest request)
    {
        try
        {
            await eventService.WriteAsync(request);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to write SMTP session event.");
        }
    }
}
