using System.Net;
using SmtpServer;
using SmtpServer.Net;

namespace Relaywright.Web.Services.Smtp;

public static class SmtpSessionContextExtensions
{
    private const string SessionIdKey = "Relaywright.SessionId";

    public static IPAddress? GetRemoteIpAddress(this ISessionContext context)
    {
        if (context.Properties.TryGetValue(EndpointListener.RemoteEndPointKey, out var value) && value is IPEndPoint endpoint)
        {
            return endpoint.Address;
        }

        return null;
    }

    public static Guid GetOrCreateSessionId(this ISessionContext context)
    {
        if (context.Properties.TryGetValue(SessionIdKey, out var value))
        {
            if (value is Guid guid)
            {
                return guid;
            }

            if (value is string text && Guid.TryParse(text, out guid))
            {
                return guid;
            }
        }

        var created = Guid.NewGuid();
        context.Properties[SessionIdKey] = created;
        return created;
    }
}
