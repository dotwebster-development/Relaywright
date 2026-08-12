using MimeKit;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Events;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class UpstreamTestEmailSenderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendAsyncRunsExpectedStagesWithoutAuthentication()
    {
        var fixture = new SenderFixture();

        var result = await fixture.Sender.SendAsync(
            CreateRequest(),
            CreateConfiguration(useAuthentication: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["Configuration", "Connect/TLS", "Authentication", "Compose", "Send", "Disconnect"], fixture.Recorder.StageNames);
        Assert.Equal(DiagnosticStageStatus.Skipped, fixture.Recorder.StageStatuses["Authentication"]);
        Assert.False(fixture.Session.AuthenticateCalled);
        Assert.True(fixture.Session.DisconnectCalled);
        Assert.Equal("relaywright.test", fixture.Session.SentMessage?.Subject);
        Assert.Equal(30_000, fixture.Factory.TimeoutMilliseconds);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendAsyncAuthenticatesWhenConfigured()
    {
        var fixture = new SenderFixture();

        var result = await fixture.Sender.SendAsync(
            CreateRequest(),
            CreateConfiguration(useAuthentication: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(fixture.Session.AuthenticateCalled);
        Assert.Equal(DiagnosticStageStatus.Succeeded, fixture.Recorder.StageStatuses["Authentication"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendAsyncRecordsActiveStageFailureAndDisconnects()
    {
        var fixture = new SenderFixture();
        fixture.Session.SendException = new InvalidOperationException("relay rejected message");

        var result = await fixture.Sender.SendAsync(
            CreateRequest(),
            CreateConfiguration(useAuthentication: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("relay rejected message", result.Message);
        Assert.Equal(DiagnosticStageStatus.Failed, fixture.Recorder.StageStatuses["Send"]);
        Assert.False(fixture.Recorder.RunSucceeded);
        Assert.True(fixture.Session.DisconnectCalled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendAsyncPropagatesCancellationAfterRecordingIt()
    {
        var fixture = new SenderFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Session.ConnectException = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Sender.SendAsync(
            CreateRequest(),
            CreateConfiguration(useAuthentication: false),
            Guid.NewGuid(),
            cancellation.Token));

        Assert.Equal(DiagnosticStageStatus.Failed, fixture.Recorder.StageStatuses["Connect/TLS"]);
        Assert.False(fixture.Recorder.RunSucceeded);
        Assert.Equal("Diagnostic test email canceled.", fixture.Recorder.RunMessage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendAsyncRejectsMissingHostBeforeCreatingSession()
    {
        var fixture = new SenderFixture();

        var result = await fixture.Sender.SendAsync(
            CreateRequest(),
            new RelayConfigurationSnapshot(),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Upstream host is not configured.", result.Message);
        Assert.False(fixture.Factory.CreateCalled);
        Assert.Equal(DiagnosticStageStatus.Failed, fixture.Recorder.StageStatuses["Configuration"]);
    }

    private static TestEmailRequest CreateRequest()
    {
        return new TestEmailRequest
        {
            FromAddress = "sender@relaywright.test",
            ToAddress = "recipient@relaywright.test",
            Subject = "relaywright.test",
            Body = "Diagnostic body"
        };
    }

    private static RelayConfigurationSnapshot CreateConfiguration(bool useAuthentication)
    {
        return new RelayConfigurationSnapshot
        {
            UpstreamHost = "smtp.relaywright.test",
            UpstreamPort = 587,
            UpstreamTimeoutSeconds = 30,
            UseUpstreamAuthentication = useAuthentication
        };
    }

    private sealed class SenderFixture
    {
        public SenderFixture()
        {
            Factory = new FakeSessionFactory(Session);
            Sender = new UpstreamTestEmailSender(
                Factory,
                Recorder,
                new FakeOperationalEventService(),
                NullLogger<UpstreamTestEmailSender>.Instance);
        }

        public FakeSmtpSession Session { get; } = new();

        public FakeSessionFactory Factory { get; }

        public FakeDiagnosticRunRecorder Recorder { get; } = new();

        public UpstreamTestEmailSender Sender { get; }
    }

    private sealed class FakeSessionFactory(FakeSmtpSession session) : IUpstreamDiagnosticSmtpSessionFactory
    {
        public bool CreateCalled { get; private set; }

        public int TimeoutMilliseconds { get; private set; }

        public IUpstreamDiagnosticSmtpSession Create(int timeoutMilliseconds)
        {
            CreateCalled = true;
            TimeoutMilliseconds = timeoutMilliseconds;
            return session;
        }
    }

    private sealed class FakeSmtpSession : IUpstreamDiagnosticSmtpSession
    {
        public bool IsSecure { get; set; } = true;

        public bool IsConnected { get; private set; }

        public bool AuthenticateCalled { get; private set; }

        public bool DisconnectCalled { get; private set; }

        public MimeMessage? SentMessage { get; private set; }

        public Exception? ConnectException { get; set; }

        public Exception? SendException { get; set; }

        public Task ConnectAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
        {
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
        {
            AuthenticateCalled = true;
            return Task.CompletedTask;
        }

        public Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            if (SendException is not null)
            {
                throw SendException;
            }

            SentMessage = message;
            return Task.FromResult("250 accepted");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCalled = true;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeDiagnosticRunRecorder : IDiagnosticRunRecorder
    {
        private long _nextStageId;
        private readonly Dictionary<long, string> _stageNamesById = [];

        public List<string> StageNames { get; } = [];

        public Dictionary<string, DiagnosticStageStatus> StageStatuses { get; } = [];

        public bool? RunSucceeded { get; private set; }

        public string? RunMessage { get; private set; }

        public Task<DiagnosticRun> StartRunAsync(DiagnosticRunKind kind, Guid? sessionId, string? requestedBy, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DiagnosticRun { Id = Guid.NewGuid(), Kind = kind, SessionId = sessionId });
        }

        public Task<DiagnosticStage> StartStageAsync(Guid runId, int sequence, string name, string message, CancellationToken cancellationToken)
        {
            var id = ++_nextStageId;
            _stageNamesById[id] = name;
            StageNames.Add(name);
            return Task.FromResult(new DiagnosticStage { Id = id, DiagnosticRunId = runId, Sequence = sequence, Name = name });
        }

        public Task CompleteStageAsync(long stageId, DiagnosticStageStatus status, string message, string? detail, CancellationToken cancellationToken)
        {
            StageStatuses[_stageNamesById[stageId]] = status;
            return Task.CompletedTask;
        }

        public Task CompleteRunAsync(Guid runId, bool succeeded, string message, CancellationToken cancellationToken)
        {
            RunSucceeded = succeeded;
            RunMessage = message;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticRun>> GetRecentRunsAsync(DiagnosticRunKind? kind, int count, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DiagnosticRun>>([]);

        public Task<DiagnosticRun?> GetRunAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult<DiagnosticRun?>(null);
    }

    private sealed class FakeOperationalEventService : IOperationalEventService
    {
        public Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
