using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Smtp;
using Relaywright.Web.Tests.Support;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class EndToEndSmtpDeliveryIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptedSmtpDataCanBeDeliveredToLocalCaptureServer()
    {
        await using var captureServer = await CaptureSmtpServer.StartAsync();
        await using var database = await SqliteTestStore.CreateAsync(seedRelayConfiguration: true);
        using var appData = TempAppData.Create();
        var events = new RecordingOperationalEventService();
        var signal = new RecordingQueueSignal();
        var spool = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var queue = new MessageQueueService(
            database.DbContextFactory,
            new RetryDelayCalculator(),
            spool,
            new ImmediateBackupCoordinator(),
            events,
            signal,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<MessageQueueService>.Instance);
        var configuration = new RelayConfigurationSnapshot
        {
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            UpstreamHost = IPAddress.Loopback.ToString(),
            UpstreamPort = captureServer.Port,
            UpstreamSecureSocketOptions = SecureSocketOptions.None,
            UpstreamTimeoutSeconds = 10,
            UseUpstreamAuthentication = false,
            DeliveryConcurrency = 1,
            MaxRetryCount = 3,
            InitialRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 300,
            MessageExpirationHours = 24,
            DeliveredRetentionHours = 24,
            FailedRetentionHours = 24,
            EventRetentionHours = 24
        };
        var store = new RelayMessageStore(
            spool,
            queue,
            new StaticRelayConfigurationService(configuration),
            events,
            NullLogger<RelayMessageStore>.Instance);

        var response = await store.SaveAsync(
            new FakeSessionContext(IPAddress.Loopback, Guid.NewGuid()),
            new FakeMessageTransaction(recipients: ["recipient@example.test"]),
            new ReadOnlySequence<byte>(TestData.MimeBytes(subject: "Relaywright E2E delivery")),
            CancellationToken.None);

        Assert.Equal(SmtpReplyCode.Ok, response.ReplyCode);
        var workItem = await queue.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(workItem);

        var delivery = new UpstreamDeliveryService(
            spool,
            new NoopUpstreamAuthenticationService(),
            new DeliveryFailureClassifier(),
            NullLogger<UpstreamDeliveryService>.Instance);
        var result = await delivery.DeliverAsync(workItem!, configuration, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorDetail);
        await queue.MarkDeliveredAsync(workItem!, result, CancellationToken.None);

        await using var dbContext = database.CreateDbContext();
        var saved = await dbContext.QueuedMessages
            .Include(x => x.DeliveryAttempts)
            .SingleAsync();
        Assert.Equal(QueuedMessageStatus.Delivered, saved.Status);
        Assert.True(saved.DeliveryAttempts.Single().Succeeded);
        var captured = Assert.Single(captureServer.Messages);
        Assert.Contains("Relaywright E2E delivery", captured, StringComparison.Ordinal);
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.SmtpSession);
        Assert.Contains(events.Events, x => x.Category == OperationalEventCategory.Delivery);
    }

    private sealed class NoopUpstreamAuthenticationService : IUpstreamAuthenticationService
    {
        public Task AuthenticateAsync(
            MailKit.Net.Smtp.SmtpClient client,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly SmtpServer.SmtpServer _server;
        private readonly CaptureMessageStore _store;
        private readonly Task _serverTask;

        private CaptureSmtpServer(int port, SmtpServer.SmtpServer server, CaptureMessageStore store)
        {
            Port = port;
            _server = server;
            _store = store;
            _serverTask = _server.StartAsync(_cancellationTokenSource.Token);
        }

        public int Port { get; }

        public IReadOnlyList<string> Messages => _store.Messages;

        public static async Task<CaptureSmtpServer> StartAsync()
        {
            var port = GetFreeTcpPort();
            var store = new CaptureMessageStore();
            var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
            serviceProvider.Add(store);
            var options = new SmtpServerOptionsBuilder()
                .ServerName("capture.example.test")
                .Endpoint(endpoint => endpoint.Endpoint(new IPEndPoint(IPAddress.Loopback, port)))
                .Build();
            var server = new SmtpServer.SmtpServer(options, serviceProvider);
            var captureServer = new CaptureSmtpServer(port, server, store);
            await captureServer.WaitUntilListeningAsync();
            return captureServer;
        }

        public async ValueTask DisposeAsync()
        {
            _server.Shutdown();
            await _cancellationTokenSource.CancelAsync();

            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }
        }

        private async Task WaitUntilListeningAsync()
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (_serverTask.IsFaulted)
                {
                    await _serverTask;
                }

                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, Port);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(50);
                }
            }

            throw new InvalidOperationException("Local capture SMTP server did not start.");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    private sealed class CaptureMessageStore : MessageStore
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public override Task<SmtpResponse> SaveAsync(
            ISessionContext context,
            IMessageTransaction transaction,
            ReadOnlySequence<byte> buffer,
            CancellationToken cancellationToken)
        {
            _messages.Enqueue(System.Text.Encoding.ASCII.GetString(buffer.ToArray()));
            return Task.FromResult(SmtpResponse.Ok);
        }
    }
}
