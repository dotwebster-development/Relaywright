using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Pages.Account;

[AllowAnonymous]
public sealed class SetupModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOperationalEventService eventService,
    ILogger<SetupModel> logger) : PageModel
{
    private static readonly SemaphoreSlim InitialAdminGate = new(1, 1);

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SetupStep CurrentStep { get; private set; } = SetupStep.Welcome;

    public string? CreatedUserName { get; private set; }

    public bool IsWelcomeStep => CurrentStep == SetupStep.Welcome;

    public bool IsAdminAccountStep => CurrentStep == SetupStep.AdminAccount;

    public bool IsCompleteStep => CurrentStep == SetupStep.Complete;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        ModelState.Clear();
        CurrentStep = SetupStep.AdminAccount;
        return Page();
    }

    public async Task<IActionResult> OnPostBackAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        ModelState.Clear();
        CurrentStep = SetupStep.Welcome;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        CurrentStep = SetupStep.AdminAccount;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "The password and confirmation password do not match.");
            return Page();
        }

        await InitialAdminGate.WaitAsync(cancellationToken);
        try
        {
            if (await HasAnyUserAsync(cancellationToken))
            {
                logger.LogWarning(
                    "First-run setup rejected because an admin already exists. RemoteIp={RemoteIp}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());
                ModelState.AddModelError(string.Empty, "Initial admin has already been created.");
                return Page();
            }

            return await CreateInitialAdminAsync(cancellationToken);
        }
        finally
        {
            InitialAdminGate.Release();
        }
    }

    private async Task<IActionResult> CreateInitialAdminAsync(CancellationToken cancellationToken)
    {
        var userName = Input.UserName.Trim();
        var admin = new ApplicationUser
        {
            UserName = userName,
            DisplayName = userName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, Input.Password);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "First-run admin creation failed. UserName={UserName}; RemoteIp={RemoteIp}; ErrorCodes={ErrorCodes}",
                userName,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                string.Join(",", result.Errors.Select(x => x.Code)));

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogWarning(
            "First-run admin user created. UserId={UserId}; UserName={UserName}; RemoteIp={RemoteIp}",
            admin.Id,
            admin.UserName,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.Security,
            Message = "Initial admin user created through first-run setup.",
            RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, cancellationToken);

        await signInManager.SignInAsync(admin, isPersistent: false);
        CreatedUserName = admin.UserName;
        CurrentStep = SetupStep.Complete;
        return Page();
    }

    private async Task<bool> HasAnyUserAsync(CancellationToken cancellationToken)
    {
        return await userManager.Users.AnyAsync(cancellationToken);
    }

    public enum SetupStep
    {
        Welcome,
        AdminAccount,
        Complete
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "User Name")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
