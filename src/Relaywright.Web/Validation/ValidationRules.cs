using System.Net;
using System.Net.Mail;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Validation;

public static class ValidationRules
{
    private static readonly char[] Delimiters = [',', ';', '\r', '\n', '\t', ' '];

    public static IReadOnlyList<string> SplitDelimitedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    public static string? NormalizeDelimitedList(string? value)
    {
        var entries = SplitDelimitedList(value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries.Length == 0 ? null : string.Join(Environment.NewLine, entries);
    }

    public static bool ContainsDisallowedControlCharacter(
        string? value,
        bool allowLineBreaks,
        out char invalidCharacter)
    {
        invalidCharacter = '\0';
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsControl(character))
            {
                continue;
            }

            if (allowLineBreaks && character is '\r' or '\n' or '\t')
            {
                continue;
            }

            invalidCharacter = character;
            return true;
        }

        return false;
    }

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

    public static bool IsMicrosoftTenantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return Guid.TryParse(trimmed, out _)
            || IsDomainName(trimmed, allowWildcard: false);
    }

    public static bool IsGuidText(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Guid.TryParse(value.Trim(), out _);
    }

    public static bool IsCertificateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return IPAddress.TryParse(trimmed, out _)
            || IsDomainName(trimmed, allowWildcard: false);
    }

    public static bool HasAllowedExtension(string fileNameOrPath, IReadOnlyCollection<string> allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return false;
        }

        var extension = Path.GetExtension(fileNameOrPath.Trim());
        return !string.IsNullOrWhiteSpace(extension)
            && allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
