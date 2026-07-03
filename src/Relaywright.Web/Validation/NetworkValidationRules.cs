using System.Net;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Validation;

public static partial class ValidationRules
{
    public static bool IsIpAddress(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && IPAddress.TryParse(value.Trim(), out _);
    }

    public static bool IsCidrRange(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && CidrRange.TryParse(value.Trim(), out _);
    }

    public static bool IsHostNameOrIpAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return IPAddress.TryParse(trimmed, out _)
            || IsDomainName(trimmed, allowWildcard: false);
    }

    public static bool IsDomainName(string value, bool allowWildcard)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Length > 253
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains(':', StringComparison.Ordinal)
            || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (allowWildcard && trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        var labels = trimmed.Split('.', StringSplitOptions.None);
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63
                || label[0] == '-'
                || label[^1] == '-')
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }
}
