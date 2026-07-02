using System.Net;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SmtpServer;
using SmtpServer.Authentication;
using SmtpServer.IO;
using SmtpServer.Mail;
using SmtpServer.Net;
using SmtpServer.Protocol;

namespace Relaywright.Web.Tests.Support;

internal sealed class FakeSessionContext(IPAddress? remoteAddress = null) : ISessionContext
{
    public Guid SessionId { get; } = Guid.NewGuid();

    public IServiceProvider ServiceProvider { get; } = new EmptyServiceProvider();

    public ISmtpServerOptions ServerOptions { get; } = new SmtpServerOptionsBuilder().Build();

    public IEndpointDefinition EndpointDefinition { get; } = new EndpointDefinitionBuilder().Build();

    public ISecurableDuplexPipe Pipe { get; } = new FakeSecurableDuplexPipe();

    public AuthenticationContext Authentication { get; } = AuthenticationContext.Unauthenticated;

    public IDictionary<string, object> Properties { get; } = CreateProperties(remoteAddress);

    public event EventHandler<SmtpCommandEventArgs>? CommandExecuting;

    public event EventHandler<SmtpCommandEventArgs>? CommandExecuted;

    public event EventHandler<SmtpResponseExceptionEventArgs>? ResponseException;

    public event EventHandler<EventArgs>? SessionAuthenticated;

    public void RaiseCommandExecuting(SmtpCommandEventArgs args) => CommandExecuting?.Invoke(this, args);

    public void RaiseCommandExecuted(SmtpCommandEventArgs args) => CommandExecuted?.Invoke(this, args);

    public void RaiseResponseException(SmtpResponseExceptionEventArgs args) => ResponseException?.Invoke(this, args);

    public void RaiseSessionAuthenticated(EventArgs args) => SessionAuthenticated?.Invoke(this, args);

    public void SetRemoteAddress(IPAddress address)
    {
        Properties[EndpointListener.RemoteEndPointKey] = new IPEndPoint(address, 2525);
    }

    public FakeSessionContext()
        : this(null)
    {
    }

    public FakeSessionContext(IPAddress? remoteAddress, Guid sessionId)
        : this(remoteAddress)
    {
        Properties["Relaywright.SessionId"] = sessionId;
    }

    public FakeSessionContext WithRemoteAddress(IPAddress address)
    {
        SetRemoteAddress(address);
        return this;
    }

    private static Dictionary<string, object> CreateProperties(IPAddress? remoteAddress)
    {
        var properties = new Dictionary<string, object>();
        if (remoteAddress is not null)
        {
            properties[EndpointListener.RemoteEndPointKey] = new IPEndPoint(remoteAddress, 2525);
        }

        return properties;
    }
}

internal sealed class FakeMessageTransaction(
    string from = "sender@example.test",
    IReadOnlyList<string>? recipients = null) : IMessageTransaction
{
    public IMailbox? From { get; set; } = new FakeMailbox(from);

    public IList<IMailbox> To { get; } = (recipients ?? ["recipient@example.test"])
        .Select(x => (IMailbox)new FakeMailbox(x))
        .ToArray();

    public IReadOnlyDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();
}

internal sealed class FakeMailbox(string address) : IMailbox
{
    private readonly string[] _parts = address.Split('@', 2);

    public string User => _parts[0];

    public string Host => _parts.Length == 2 ? _parts[1] : "localhost";
}

internal sealed class FakeSecurableDuplexPipe : ISecurableDuplexPipe
{
    private readonly Pipe _pipe = new();

    public PipeReader Input => _pipe.Reader;

    public PipeWriter Output => _pipe.Writer;

    public bool IsSecure { get; private set; }

    public SslProtocols SslProtocol { get; private set; } = SslProtocols.None;

    public Task UpgradeAsync(X509Certificate certificate, SslProtocols protocols, CancellationToken cancellationToken)
    {
        IsSecure = true;
        SslProtocol = protocols;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pipe.Reader.Complete();
        _pipe.Writer.Complete();
    }
}

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return null;
    }
}
