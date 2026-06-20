using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Relaywright.Web.Identity;

namespace Relaywright.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public void OnGet(string? returnUrl = null)
    {
        Input.ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.UserName,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "Admin sign-in succeeded. UserName={UserName}; RememberMe={RememberMe}; RemoteIp={RemoteIp}; ReturnUrlPresent={ReturnUrlPresent}",
                Input.UserName,
                Input.RememberMe,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                !string.IsNullOrWhiteSpace(Input.ReturnUrl));

            return LocalRedirect(Input.ReturnUrl ?? Url.Page("/Index")!);
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning(
                "Admin sign-in locked out. UserName={UserName}; RemoteIp={RemoteIp}",
                Input.UserName,
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        else
        {
            logger.LogWarning(
                "Admin sign-in failed. UserName={UserName}; RemoteIp={RemoteIp}; IsNotAllowed={IsNotAllowed}; RequiresTwoFactor={RequiresTwoFactor}",
                Input.UserName,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                result.IsNotAllowed,
                result.RequiresTwoFactor);
        }

        ErrorMessage = "Invalid user name or password.";
        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
