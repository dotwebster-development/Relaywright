using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Pages.Account;

public sealed class ChangePasswordModel(AdminAccountService accountService) : PageModel
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
            ModelState.AddModelError(string.Empty, "The new password and confirmation password do not match.");
            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        var result = await accountService.ChangePasswordAsync(User, Input.CurrentPassword, Input.NewPassword);
        if (result.IsUserMissing)
        {
            return Challenge();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        StatusMessage = "Password changed. Sign in again with the new password.";
        return RedirectToPage("/Account/Login");
    }

    public async Task<IActionResult> OnPostSignOutAllSessionsAsync(CancellationToken cancellationToken)
    {
        var result = await accountService.SignOutAllSessionsAsync(
            User,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result.IsUserMissing)
        {
            return Challenge();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadPageStateAsync(cancellationToken);
            return Page();
        }

        StatusMessage = "All admin sessions were signed out. Sign in again to continue.";
        return RedirectToPage("/Account/Login");
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
    {
        var authenticationResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var state = await accountService.GetPageStateAsync(User, authenticationResult, cancellationToken);
        PasswordPolicy = state.PasswordPolicy;
        SessionSummary = state.SessionSummary;
        LoginActivity = state.LoginActivity;
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed record AccountRecoveryGuidanceItem(string Label, string Detail);
}
