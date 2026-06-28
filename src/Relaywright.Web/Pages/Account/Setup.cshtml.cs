using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Account;

[AllowAnonymous]
public sealed class SetupModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOperationalEventService eventService,
    IAdminHttpsCertificateService adminHttpsCertificateService,
    IBackupRestoreService backupRestoreService,
    ILogger<SetupModel> logger) : PageModel
{
    private static readonly SemaphoreSlim InitialAdminGate = new(1, 1);

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public CertificateInputModel CertificateInput { get; set; } = new();

    [BindProperty]
    public RestoreInputModel RestoreInput { get; set; } = new();

    public SetupStep CurrentStep { get; private set; } = SetupStep.Welcome;

    public string? CreatedUserName { get; private set; }

    public string? DisplayAdminUserName => CreatedUserName ?? User.Identity?.Name;

    public AdminHttpsCertificateConfiguration? ConfiguredCertificate { get; private set; }

    public BackupRestoreResult? RestoreResult { get; private set; }

    public bool IsWelcomeStep => CurrentStep == SetupStep.Welcome;

    public bool IsRestoreStep => CurrentStep == SetupStep.Restore;

    public bool IsAdminAccountStep => CurrentStep == SetupStep.AdminAccount;

    public bool IsCertificateStep => CurrentStep == SetupStep.HttpsCertificate;

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
        CurrentStep = SetupStep.Restore;
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

    public async Task<IActionResult> OnPostSkipRestoreAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        ModelState.Clear();
        CurrentStep = SetupStep.AdminAccount;
        return Page();
    }

    public async Task<IActionResult> OnPostStageRestoreAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        CurrentStep = SetupStep.Restore;
        RemoveModelStateEntries(nameof(Input));
        RemoveModelStateEntries(nameof(CertificateInput));

        try
        {
            RestoreResult = await backupRestoreService.StageRestoreAsync(
                RequireFile(RestoreInput.BackupFile, "Select a Relaywright backup file."),
                RestoreInput.EncryptionPassword,
                cancellationToken);
            if (!RestoreResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, RestoreResult.Message);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "First-run restore staging failed. RemoteIp={RemoteIp}",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, exception.Message);
        }

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

    public async Task<IActionResult> OnPostConfigureCertificateAsync(CancellationToken cancellationToken)
    {
        var guardResult = await EnsureAuthenticatedSetupUserAsync(cancellationToken);
        if (guardResult is not null)
        {
            return guardResult;
        }

        CurrentStep = SetupStep.HttpsCertificate;
        RemoveModelStateEntries(nameof(Input));

        try
        {
            ConfiguredCertificate = CertificateInput.Mode switch
            {
                AdminHttpsCertificateMode.Pfx => await adminHttpsCertificateService.SavePfxAsync(
                    RequireFile(CertificateInput.PfxFile, "Select a PFX certificate file."),
                    CertificateInput.PfxPassword,
                    cancellationToken),
                AdminHttpsCertificateMode.Pem => await adminHttpsCertificateService.SavePemAsync(
                    RequireFile(CertificateInput.CertificateFile, "Select a certificate file."),
                    RequireFile(CertificateInput.KeyFile, "Select a private key file."),
                    CertificateInput.KeyPassword,
                    cancellationToken),
                AdminHttpsCertificateMode.SelfSigned => await adminHttpsCertificateService.GenerateSelfSignedAsync(
                    CertificateInput.SelfSignedDnsNames,
                    CertificateInput.SelfSignedValidYears,
                    cancellationToken),
                _ => throw new InvalidOperationException("Choose a certificate option.")
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "First-run HTTPS certificate setup failed. Mode={Mode}; RemoteIp={RemoteIp}",
                CertificateInput.Mode,
                HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }

        CurrentStep = SetupStep.Complete;
        return Page();
    }

    public async Task<IActionResult> OnPostSkipCertificateAsync(CancellationToken cancellationToken)
    {
        var guardResult = await EnsureAuthenticatedSetupUserAsync(cancellationToken);
        if (guardResult is not null)
        {
            return guardResult;
        }

        ConfiguredCertificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);
        CurrentStep = SetupStep.Complete;
        return Page();
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
        CertificateInput.SelfSignedDnsNames = GetDefaultCertificateNames();
        CurrentStep = SetupStep.HttpsCertificate;
        return Page();
    }

    private async Task<bool> HasAnyUserAsync(CancellationToken cancellationToken)
    {
        return await userManager.Users.AnyAsync(cancellationToken);
    }

    private async Task<IActionResult?> EnsureAuthenticatedSetupUserAsync(CancellationToken cancellationToken)
    {
        if (!await HasAnyUserAsync(cancellationToken))
        {
            CurrentStep = SetupStep.Welcome;
            return Page();
        }

        return User.Identity?.IsAuthenticated == true
            ? null
            : RedirectToPage("/Account/Login");
    }

    private static IFormFile RequireFile(IFormFile? file, string message)
    {
        return file is { Length: > 0 }
            ? file
            : throw new InvalidOperationException(message);
    }

    private void RemoveModelStateEntries(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith(prefix + ".", StringComparison.Ordinal)).ToArray())
        {
            ModelState.Remove(key);
        }
    }

    public static string GetDefaultCertificateNames()
    {
        return string.Join(", ", new[] { "localhost", Environment.MachineName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public enum SetupStep
    {
        Welcome,
        Restore,
        AdminAccount,
        HttpsCertificate,
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

    public sealed class CertificateInputModel
    {
        public AdminHttpsCertificateMode Mode { get; set; } = AdminHttpsCertificateMode.SelfSigned;

        public IFormFile? PfxFile { get; set; }

        public string? PfxPassword { get; set; }

        public IFormFile? CertificateFile { get; set; }

        public IFormFile? KeyFile { get; set; }

        public string? KeyPassword { get; set; }

        public string SelfSignedDnsNames { get; set; } = GetDefaultCertificateNames();

        [Range(1, 10)]
        public int SelfSignedValidYears { get; set; } = 2;
    }

    public sealed class RestoreInputModel
    {
        public IFormFile? BackupFile { get; set; }

        public string? EncryptionPassword { get; set; }
    }
}
