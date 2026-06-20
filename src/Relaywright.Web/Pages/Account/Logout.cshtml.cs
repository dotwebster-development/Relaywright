using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Relaywright.Web.Identity;

namespace Relaywright.Web.Pages.Account;

public sealed class LogoutModel(
    SignInManager<ApplicationUser> signInManager,
    ILogger<LogoutModel> logger) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        var userName = User.Identity?.Name;
        await signInManager.SignOutAsync();
        logger.LogInformation("Admin signed out. User={UserName}; RemoteIp={RemoteIp}", userName, HttpContext.Connection.RemoteIpAddress?.ToString());
        return RedirectToPage("/Account/Login");
    }
}
