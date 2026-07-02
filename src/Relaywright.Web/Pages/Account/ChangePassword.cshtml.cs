using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Account;

public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
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

    public async Task OnGetAsync()
    {
        await LoadPageStateAsync();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync();
            return Page();
        }

        if (!string.Equals(Input.NewPassword, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            logger.LogWarning("Password change rejected because confirmation did not match. User={UserName}", User.Identity?.Name);
            ModelState.AddModelError(string.Empty, "The new password and confirmation password do not match.");
            await LoadPageStateAsync();
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

            await LoadPageStateAsync();
            return Page();
        }

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        StatusMessage = "Password changed. Sign in again with the new password.";
        logger.LogInformation("Password changed successfully. UserId={UserId}; UserName={UserName}", user.Id, user.UserName);
        return RedirectToPage("/Account/Login");
    }

    private async Task LoadPageStateAsync()
    {
        PasswordPolicy = PasswordPolicySummary.FromOptions(identityOptions.Value);

        var authenticationResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        SessionSummary = AdminSessionSummary.Create(
            User,
            authenticationResult,
            cookieOptions.Get(IdentityConstants.ApplicationScheme),
            securityStampOptions.Value);
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
}
