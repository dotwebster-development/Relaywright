using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed class AdminAccountService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOperationalEventService eventService,
    IAdminSecurityActivityService adminSecurityActivityService,
    IOptions<IdentityOptions> identityOptions,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
    IOptions<SecurityStampValidatorOptions> securityStampOptions,
    ILogger<AdminAccountService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<AdminAccountPageState> GetPageStateAsync(
        ClaimsPrincipal principal,
        AuthenticateResult authenticationResult,
        CancellationToken cancellationToken)
    {
        var loginActivity = await adminSecurityActivityService.GetLoginActivityAsync(
            principal.Identity?.Name,
            clock.GetUtcNow(),
            cancellationToken);
        return new AdminAccountPageState(
            PasswordPolicySummary.FromOptions(identityOptions.Value),
            AdminSessionSummary.Create(
                principal,
                authenticationResult,
                cookieOptions.Get(IdentityConstants.ApplicationScheme),
                securityStampOptions.Value),
            loginActivity);
    }

    public async Task<AdminAccountCommandResult> ChangePasswordAsync(
        ClaimsPrincipal principal,
        string currentPassword,
        string newPassword)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            logger.LogWarning("Password change challenged because current user could not be resolved.");
            return AdminAccountCommandResult.UserNotFound();
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Password change failed. UserId={UserId}; UserName={UserName}; ErrorCodes={ErrorCodes}",
                user.Id,
                user.UserName,
                string.Join(",", result.Errors.Select(x => x.Code)));
            return AdminAccountCommandResult.Rejected(result.Errors.Select(x => x.Description));
        }

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        logger.LogInformation("Password changed successfully. UserId={UserId}; UserName={UserName}", user.Id, user.UserName);
        return AdminAccountCommandResult.Success();
    }

    public async Task<AdminAccountCommandResult> SignOutAllSessionsAsync(
        ClaimsPrincipal principal,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            logger.LogWarning("Session invalidation challenged because current user could not be resolved.");
            return AdminAccountCommandResult.UserNotFound();
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Session invalidation failed. UserId={UserId}; UserName={UserName}; ErrorCodes={ErrorCodes}",
                user.Id,
                user.UserName,
                string.Join(",", result.Errors.Select(x => x.Code)));
            return AdminAccountCommandResult.Rejected(result.Errors.Select(x => x.Description));
        }

        await signInManager.SignOutAsync();
        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Security,
            Message = "Admin sessions invalidated.",
            Detail = $"UserName={Normalize(user.UserName)}",
            RemoteIpAddress = remoteIpAddress
        }, cancellationToken);

        logger.LogWarning(
            "Admin sessions invalidated. UserId={UserId}; UserName={UserName}; RemoteIp={RemoteIp}",
            user.Id,
            user.UserName,
            remoteIpAddress);
        return AdminAccountCommandResult.Success();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }
}

public sealed record AdminAccountPageState(
    PasswordPolicySummary PasswordPolicy,
    AdminSessionSummary SessionSummary,
    AdminLoginActivitySummary LoginActivity);

public sealed record AdminAccountCommandResult(bool Succeeded, bool IsUserMissing, IReadOnlyList<string> Errors)
{
    public static AdminAccountCommandResult Success() => new(true, false, []);

    public static AdminAccountCommandResult UserNotFound() => new(false, true, []);

    public static AdminAccountCommandResult Rejected(IEnumerable<string> errors) =>
        new(false, false, errors.ToArray());
}
