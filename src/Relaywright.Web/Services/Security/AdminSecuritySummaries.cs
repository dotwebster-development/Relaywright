using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Services.Security;

public static class AdminSecurityDefaults
{
    public static void ConfigureSecurityStampValidator(SecurityStampValidatorOptions options)
    {
        options.ValidationInterval = TimeSpan.Zero;
    }
}

public sealed record AdminLoginSecurityEvent(
    string UserName,
    bool Succeeded,
    bool RememberMe,
    string Result,
    string? RemoteIpAddress)
{
    private const int MaxValueLength = 256;

    public static AdminLoginSecurityEvent Success(
        string userName,
        bool rememberMe,
        string? remoteIpAddress) =>
        new(userName, true, rememberMe, "Succeeded", remoteIpAddress);

    public static AdminLoginSecurityEvent Failure(
        string userName,
        bool rememberMe,
        string result,
        string? remoteIpAddress) =>
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

        var normalized = value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        return normalized.Length <= MaxValueLength
            ? normalized
            : normalized[..MaxValueLength];
    }
}

public sealed record PasswordPolicySummary(
    int RequiredLength,
    int RequiredUniqueChars,
    bool RequireDigit,
    bool RequireLowercase,
    bool RequireUppercase,
    bool RequireNonAlphanumeric)
{
    public static PasswordPolicySummary FromOptions(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new PasswordPolicySummary(
            options.Password.RequiredLength,
            options.Password.RequiredUniqueChars,
            options.Password.RequireDigit,
            options.Password.RequireLowercase,
            options.Password.RequireUppercase,
            options.Password.RequireNonAlphanumeric);
    }

    public IReadOnlyList<string> Requirements
    {
        get
        {
            var requirements = new List<string>
            {
                $"At least {RequiredLength} characters"
            };

            if (RequiredUniqueChars > 1)
            {
                requirements.Add($"At least {RequiredUniqueChars} unique characters");
            }

            if (RequireDigit)
            {
                requirements.Add("At least one number");
            }

            if (RequireLowercase)
            {
                requirements.Add("At least one lowercase letter");
            }

            if (RequireUppercase)
            {
                requirements.Add("At least one uppercase letter");
            }

            if (RequireNonAlphanumeric)
            {
                requirements.Add("At least one symbol");
            }

            return requirements;
        }
    }

    public string CompactDescription => string.Join(", ", Requirements);
}

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

    public string SecurityStampValidationLabel =>
        SecurityStampValidationInterval == TimeSpan.Zero
            ? "Every request"
            : $"Every {FormatDuration(SecurityStampValidationInterval)}";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s";
        }

        if (duration.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalHours))}h";
    }
}

public sealed record AdminWebSecuritySummary(
    bool HasManagedListener,
    int HttpsPort,
    bool HttpEnabled,
    int HttpPort,
    DateTimeOffset? ListenerUpdatedUtc,
    bool HasManagedCertificate,
    AdminHttpsCertificateMode? CertificateMode,
    IReadOnlyList<string> CertificateDnsNames,
    DateTimeOffset? CertificateExpiresUtc,
    DateTimeOffset? CertificateUpdatedUtc,
    bool RestartRequired,
    string? RestartReason,
    string? RestartRequestedBy,
    DateTimeOffset? RestartRequestedUtc)
{
    public static AdminWebSecuritySummary Create(
        AdminWebListenerConfiguration? listener,
        AdminHttpsCertificateConfiguration? certificate,
        RuntimeStatusSnapshot runtimeStatus,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        var effectiveListener = listener ?? new AdminWebListenerConfiguration();

        return new AdminWebSecuritySummary(
            listener is not null,
            effectiveListener.HttpsPort,
            effectiveListener.EnableHttp,
            effectiveListener.HttpPort,
            listener?.UpdatedUtc,
            certificate is not null,
            certificate?.Mode,
            certificate?.DnsNames ?? [],
            certificate?.NotAfterUtc,
            certificate?.UpdatedUtc,
            runtimeStatus.RestartRequired,
            runtimeStatus.RestartReason,
            runtimeStatus.RestartRequestedBy,
            runtimeStatus.RestartRequestedUtc);
    }

    public string ListenerSourceLabel => HasManagedListener ? "Relaywright-managed" : "Deployment default";

    public string HttpStatusLabel => HttpEnabled ? $"Enabled on port {HttpPort}" : "Disabled";

    public string HttpBadgeClass => HttpEnabled ? "severity-warning" : "status-enabled";

    public string CertificateModeLabel => HasManagedCertificate
        ? CertificateMode?.ToString() ?? "Configured"
        : "Hosting or deployment certificate";

    public string CertificateDnsLabel => CertificateDnsNames.Count == 0
        ? "Not available"
        : string.Join(", ", CertificateDnsNames);

    public string CertificateStatusLabel(DateTimeOffset now)
    {
        if (!HasManagedCertificate)
        {
            return "Not managed by Relaywright";
        }

        if (CertificateExpiresUtc is null)
        {
            return "Expiry not available";
        }

        if (CertificateExpiresUtc <= now)
        {
            return "Expired";
        }

        if (CertificateExpiresUtc <= now.AddDays(30))
        {
            return "Expiring soon";
        }

        return "Valid";
    }

    public string CertificateBadgeClass(DateTimeOffset now)
    {
        if (!HasManagedCertificate || CertificateExpiresUtc is null)
        {
            return "status-unknown";
        }

        if (CertificateExpiresUtc <= now)
        {
            return "status-failed";
        }

        return CertificateExpiresUtc <= now.AddDays(30)
            ? "severity-warning"
            : "status-enabled";
    }

    public string RestartStatusLabel => RestartRequired ? "Restart required" : "Current";

    public string RestartBadgeClass => RestartRequired ? "severity-warning" : "status-enabled";
}

public sealed record SetupHardeningChecklist(IReadOnlyList<SetupHardeningItem> Items)
{
    public static SetupHardeningChecklist Create(
        bool adminExists,
        PasswordPolicySummary passwordPolicy,
        AdminHttpsCertificateConfiguration? certificate,
        AdminWebListenerConfiguration? listener,
        BootstrapAdminOptions bootstrapOptions,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);
        ArgumentNullException.ThrowIfNull(bootstrapOptions);
        ArgumentNullException.ThrowIfNull(environment);

        return new SetupHardeningChecklist(
        [
            new SetupHardeningItem(
                "Admin account",
                adminExists ? "Created" : "Required",
                adminExists ? "An administrator already exists." : "The first administrator will be created in this setup.",
                adminExists ? "status-enabled" : "severity-warning"),
            new SetupHardeningItem(
                "Password policy",
                "Active",
                passwordPolicy.CompactDescription,
                "status-enabled"),
            CreateBootstrapPasswordItem(bootstrapOptions, environment),
            new SetupHardeningItem(
                "HTTPS certificate",
                certificate is null ? "Not configured" : "Configured",
                certificate is null
                    ? "Relaywright will use the current hosting certificate until one is saved."
                    : $"{certificate.Mode} certificate saved for the admin web listener.",
                certificate is null ? "severity-warning" : "status-enabled"),
            new SetupHardeningItem(
                "HTTP listener",
                listener?.EnableHttp == true ? "HTTP enabled" : "HTTPS only",
                listener?.EnableHttp == true
                    ? $"HTTP is configured on port {listener.HttpPort}; keep it limited to trusted networks."
                    : "No Relaywright-managed HTTP listener is enabled.",
                listener?.EnableHttp == true ? "severity-warning" : "status-enabled")
        ]);
    }

    private static SetupHardeningItem CreateBootstrapPasswordItem(
        BootstrapAdminOptions bootstrapOptions,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(bootstrapOptions.Password))
        {
            return new SetupHardeningItem(
                "Default password guard",
                "First-run setup",
                "No bootstrap password is configured; admin creation stays in the setup flow.",
                "status-enabled");
        }

        if (string.Equals(
                bootstrapOptions.Password,
                BootstrapAdminOptions.DefaultDevelopmentPassword,
                StringComparison.Ordinal))
        {
            return new SetupHardeningItem(
                "Default password guard",
                environment.IsDevelopment() ? "Development only" : "Blocked",
                environment.IsDevelopment()
                    ? "The default development password is accepted only in Development."
                    : "Startup blocks the default development password outside Development.",
                environment.IsDevelopment() ? "severity-warning" : "status-failed");
        }

        return new SetupHardeningItem(
            "Default password guard",
            "Non-default",
            "Configured bootstrap credentials are not the development default.",
            "status-enabled");
    }
}

public sealed record SetupHardeningItem(
    string Label,
    string Status,
    string Detail,
    string BadgeClass);
