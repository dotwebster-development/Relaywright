using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed record AdminLoginSecurityEvent(
    string UserName,
    bool Succeeded,
    bool RememberMe,
    string Result,
    string? RemoteIpAddress)
{
    private const int MaxValueLength = 256;

    public static AdminLoginSecurityEvent Success(string userName, bool rememberMe, string? remoteIpAddress) =>
        new(userName, true, rememberMe, "Succeeded", remoteIpAddress);

    public static AdminLoginSecurityEvent Failure(string userName, bool rememberMe, string result, string? remoteIpAddress) =>
        new(userName, false, rememberMe, result, remoteIpAddress);

    public OperationalEventRequest ToOperationalEventRequest()
    {
        return new OperationalEventRequest
        {
            Category = OperationalEventCategory.Security,
            Severity = Succeeded ? EventSeverity.Information : EventSeverity.Warning,
            RemoteIpAddress = Normalize(RemoteIpAddress),
            Message = Succeeded ? "Admin sign-in succeeded." : "Admin sign-in failed.",
            Detail = $"UserName={Normalize(UserName) ?? "unknown"}; Result={Normalize(Result) ?? "Unknown"}; RememberMe={RememberMe}"
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= MaxValueLength ? normalized : normalized[..MaxValueLength];
    }
}
