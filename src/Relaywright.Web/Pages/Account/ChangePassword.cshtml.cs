using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Account;

public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOperationalEventService eventService,
    IAdminSecurityActivityService adminSecurityActivityService,
    IOptions<IdentityOptions> identityOptions,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
    IOptions<SecurityStampValidatorOptions> securityStampOptions,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public PasswordPolicySummary PasswordPolicy { get; private set; } =
        PasswordPolicySummary.FromOptions(new IdentityOptions());

    public AdminSessionSummary? SessionSummary { get; private set; }

    public AdminLoginActivitySummary LoginActivity { get; private set; } =
        new(null, null, null, 0, 0);

    public IReadOnlyList<AccountRecoveryGuidanceItem> RecoveryGuidance { get; } =
    [
        new AccountRecoveryGuidanceItem(
            "Account storage",
            "Admin accounts are stored in the ASP.NET Core Identity tables in the configured Relaywright database."),
        new AccountRecoveryGuidanceItem(
            "Bootstrap behavior",
            "Bootstrap admin settings create an initial account only when no admin users exist; they do not reset existing users."),
        new AccountRecoveryGuidanceItem(
            "Password recovery",
            "Plaintext passwords cannot be recovered. Use a controlled password reset or restore procedure instead."),
        new AccountRecoveryGuidanceItem(
            "Data Protection keys",
            "Do not delete the Data Protection key ring casually; it protects stored secrets and certificate passwords."),
        new AccountRecoveryGuidanceItem(
            "Break-glass process",
            "Use a verified backup or controlled database maintenance window, and record the recovery action in operational notes.")
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        if (!string.Equals(Input.NewPassword, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            logger.LogWarning("Password change rejected because confirmation did not match. User={UserName}", User.Identity?.Name);
            ModelState.AddModelError(string.Empty, "The new password and confirmation password do not match.");
            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            logger.LogWarning("Password change challenged because current user could not be resolved.");
            return Challenge();
        }

        var result = await userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Password change failed. UserId={UserId}; UserName={UserName}; ErrorCodes={ErrorCodes}",
                user.Id,
                user.UserName,
                string.Join(",", result.Errors.Select(x => x.Code)));

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        StatusMessage = "Password changed. Sign in again with the new password.";
        logger.LogInformation("Password changed successfully. UserId={UserId}; UserName={UserName}", user.Id, user.UserName);
        return RedirectToPage("/Account/Login");
    }

    public async Task<IActionResult> OnPostSignOutAllSessionsAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            logger.LogWarning("Session invalidation challenged because current user could not be resolved.");
            return Challenge();
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Session invalidation failed. UserId={UserId}; UserName={UserName}; ErrorCodes={ErrorCodes}",
                user.Id,
                user.UserName,
                string.Join(",", result.Errors.Select(x => x.Code)));

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        await signInManager.SignOutAsync();
        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Security,
            Message = "Admin sessions invalidated.",
            Detail = $"UserName={Normalize(user.UserName)}",
            RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, cancellationToken);

        StatusMessage = "All admin sessions were signed out. Sign in again to continue.";
        logger.LogWarning(
            "Admin sessions invalidated. UserId={UserId}; UserName={UserName}; RemoteIp={RemoteIp}",
            user.Id,
            user.UserName,
            HttpContext.Connection.RemoteIpAddress?.ToString());
        return RedirectToPage("/Account/Login");
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
    {
        PasswordPolicy = PasswordPolicySummary.FromOptions(identityOptions.Value);

        var authenticationResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        SessionSummary = AdminSessionSummary.Create(
            User,
            authenticationResult,
            cookieOptions.Get(IdentityConstants.ApplicationScheme),
            securityStampOptions.Value);
        LoginActivity = await adminSecurityActivityService.GetLoginActivityAsync(
            User.Identity?.Name,
            DateTimeOffset.UtcNow,
            cancellationToken);
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

        return normalized.Length <= 256
            ? normalized
            : normalized[..256];
    }

    public sealed class InputModel
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed record AccountRecoveryGuidanceItem(string Label, string Detail);
}
