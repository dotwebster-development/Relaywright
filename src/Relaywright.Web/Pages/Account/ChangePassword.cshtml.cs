using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Relaywright.Web.Identity;

namespace Relaywright.Web.Pages.Account;

public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.NewPassword, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            logger.LogWarning("Password change rejected because confirmation did not match. User={UserName}", User.Identity?.Name);
            ModelState.AddModelError(string.Empty, "The new password and confirmation password do not match.");
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

            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Password changed.";
        logger.LogInformation("Password changed successfully. UserId={UserId}; UserName={UserName}", user.Id, user.UserName);
        return RedirectToPage();
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
