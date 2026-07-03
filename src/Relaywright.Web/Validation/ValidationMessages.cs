namespace Relaywright.Web.Validation;

public static class ValidationMessages
{
    public static string Required(string label)
    {
        return $"{label} is required.";
    }

    public static string PortRange(string label)
    {
        return $"{label} must be between {ValidationLimits.MinimumPort} and {ValidationLimits.MaximumPort}.";
    }

    public static string AtLeast(string label, long minimum, string unit)
    {
        return $"{label} must be at least {minimum} {unit}.";
    }

    public static string MaximumLength(string label, int maximumLength)
    {
        return $"{label} must be {maximumLength} characters or fewer.";
    }

    public static string UnsupportedControlCharacter(string label)
    {
        return $"{label} contains an unsupported control character.";
    }

    public static string HostNameOrIpAddress(string label)
    {
        return $"{label} must be a hostname or IP address, not a URL or path.";
    }

    public static string FileExtension(string label, IReadOnlyCollection<string> extensions)
    {
        return $"{label} must use one of these file extensions: {string.Join(", ", extensions)}.";
    }

    public static string TenantId(string label)
    {
        return $"{label} must be a tenant GUID or tenant domain.";
    }
}
