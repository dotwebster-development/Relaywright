using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Relaywright.Web.Services.Security;

public sealed record AdminSessionSummary(
    string UserName,
    bool IsPersistent,
    DateTimeOffset? IssuedUtc,
    DateTimeOffset? ExpiresUtc,
    CookieSecurePolicy SecurePolicy,
    SameSiteMode SameSite,
    bool HttpOnly,
    bool SlidingExpiration,
    TimeSpan SecurityStampValidationInterval)
{
    public static AdminSessionSummary Create(
        ClaimsPrincipal user,
        AuthenticateResult authenticateResult,
        CookieAuthenticationOptions cookieOptions,
        SecurityStampValidatorOptions securityStampOptions)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(authenticateResult);
        ArgumentNullException.ThrowIfNull(cookieOptions);
        ArgumentNullException.ThrowIfNull(securityStampOptions);
        return new AdminSessionSummary(
            user.Identity?.Name ?? "unknown",
            authenticateResult.Properties?.IsPersistent == true,
            authenticateResult.Properties?.IssuedUtc,
            authenticateResult.Properties?.ExpiresUtc,
            cookieOptions.Cookie.SecurePolicy,
            cookieOptions.Cookie.SameSite,
            cookieOptions.Cookie.HttpOnly,
            cookieOptions.SlidingExpiration,
            securityStampOptions.ValidationInterval);
    }

    public string PersistenceLabel => IsPersistent ? "Persistent" : "Session";

    public string SecurePolicyLabel => SecurePolicy switch
    {
        CookieSecurePolicy.Always => "HTTPS only",
        CookieSecurePolicy.SameAsRequest => "Same as request",
        CookieSecurePolicy.None => "Not forced",
        _ => SecurePolicy.ToString()
    };

    public string SecurityStampValidationLabel => SecurityStampValidationInterval == TimeSpan.Zero
        ? "Every request"
        : $"Every {FormatDuration(SecurityStampValidationInterval)}";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1) return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s";
        if (duration.TotalHours < 1) return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        return $"{Math.Max(1, (int)Math.Round(duration.TotalHours))}h";
    }
}
