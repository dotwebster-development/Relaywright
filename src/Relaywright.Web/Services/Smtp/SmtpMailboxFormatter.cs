using SmtpServer.Mail;

namespace Relaywright.Web.Services.Smtp;

internal static class SmtpMailboxFormatter
{
    public static string Format(IMailbox? mailbox)
    {
        if (mailbox is null)
        {
            return string.Empty;
        }

        var user = mailbox.User ?? string.Empty;
        var host = mailbox.Host ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return user;
        }

        return $"{user}@{host}";
    }
}
