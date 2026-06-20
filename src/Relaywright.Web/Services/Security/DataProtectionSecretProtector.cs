using Microsoft.AspNetCore.DataProtection;

namespace Relaywright.Web.Services.Security;

public sealed class DataProtectionSecretProtector(
    IDataProtectionProvider provider,
    ILogger<DataProtectionSecretProtector> logger) : ISecretProtector
{
    private const string CurrentPurpose = "Relaywright.Web.SecretProtector";

    private readonly IDataProtector _protector = provider.CreateProtector(CurrentPurpose);

    public string Protect(string? plainText)
    {
        return string.IsNullOrWhiteSpace(plainText)
            ? string.Empty
            : _protector.Protect(plainText);
    }

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (Exception exception) when (!LooksLikeProtectedPayload(protectedText))
        {
            // Accept previously plain-text values during early bootstrap/migration.
            logger.LogWarning(exception, "Using a legacy unprotected secret value. Re-save the related settings to protect it.");
            return protectedText;
        }
    }

    private static bool LooksLikeProtectedPayload(string value)
    {
        return value.StartsWith("CfDJ", StringComparison.Ordinal);
    }
}
