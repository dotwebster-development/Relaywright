using System.Net;
using SmtpServer;
using SmtpServer.Net;

namespace Relaywright.Web.Services.Smtp;

public static class SmtpSessionContextExtensions
{
    private const string SessionIdKey = "Relaywright.SessionId";
    private const string TrustedNetworkIdKey = "Relaywright.TrustedNetworkId";
    private const string RecipientCountKey = "Relaywright.RecipientCount";

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

    public static void SetTrustedNetworkId(this ISessionContext context, int trustedNetworkId)
    {
        context.Properties[TrustedNetworkIdKey] = trustedNetworkId;
        context.Properties[RecipientCountKey] = 0;
    }

    public static int GetNextRecipientNumber(this ISessionContext context)
    {
        var current = context.Properties.TryGetValue(RecipientCountKey, out var value) && value is int count
            ? count
            : 0;
        return current + 1;
    }

    public static void CommitRecipientAccepted(this ISessionContext context)
    {
        context.Properties[RecipientCountKey] = GetNextRecipientNumber(context);
    }
}
