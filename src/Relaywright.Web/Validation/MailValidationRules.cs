using System.Net.Mail;

namespace Relaywright.Web.Validation;

public static partial class ValidationRules
{
    public static bool IsMailboxAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _)
            || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal)
            || trimmed.Count(x => x == '@') != 1)
        {
            return false;
        }

        try
        {
            var address = new MailAddress(trimmed);
            return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsSenderPolicyPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("*@", StringComparison.Ordinal))
        {
            return IsDomainName(trimmed[2..], allowWildcard: false);
        }

        if (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            return IsDomainName(trimmed[1..], allowWildcard: false);
        }

        return IsMailboxAddress(trimmed);
    }

    public static bool IsRecipientDomainPattern(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && IsDomainName(value.Trim(), allowWildcard: true);
    }
}
