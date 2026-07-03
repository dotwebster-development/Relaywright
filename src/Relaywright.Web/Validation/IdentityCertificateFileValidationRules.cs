using System.Net;

namespace Relaywright.Web.Validation;

public static partial class ValidationRules
{
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
