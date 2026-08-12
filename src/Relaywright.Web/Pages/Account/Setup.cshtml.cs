using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Pages.Account;

[AllowAnonymous]
public sealed class SetupModel(
    SignInManager<ApplicationUser> signInManager,
    FirstRunSetupService setupService,
    CertificateFormValidator certificateFormValidator,
    ILogger<SetupModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public CertificateInputModel CertificateInput { get; set; } = new();

    public SetupStep CurrentStep { get; private set; } = SetupStep.Welcome;

    public string? CreatedUserName { get; private set; }

    public string? DisplayAdminUserName => CreatedUserName ?? User.Identity?.Name;

    public AdminHttpsCertificateConfiguration? ConfiguredCertificate { get; private set; }

    public PasswordPolicySummary PasswordPolicy { get; private set; } =
        PasswordPolicySummary.FromOptions(new IdentityOptions());

    public SetupHardeningChecklist HardeningChecklist { get; private set; } = new([]);

    public bool IsWelcomeStep => CurrentStep == SetupStep.Welcome;

    public bool IsAdminAccountStep => CurrentStep == SetupStep.AdminAccount;

    public bool IsCertificateStep => CurrentStep == SetupStep.HttpsCertificate;

    public bool IsCompleteStep => CurrentStep == SetupStep.Complete;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await HasAnyUserAsync(cancellationToken))
        {
            return RedirectToPage("/Account/Login");
        }

        await LoadPageStateAsync(adminExists: false, cancellationToken);
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
        await LoadPageStateAsync(adminExists: false, cancellationToken);
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
        await LoadPageStateAsync(adminExists: false, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        CurrentStep = SetupStep.AdminAccount;

        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync(adminExists: false, cancellationToken);
            return Page();
        }

        if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "The password and confirmation password do not match.");
            await LoadPageStateAsync(adminExists: false, cancellationToken);
            return Page();
        }

        var result = await setupService.CreateInitialAdminAsync(
            Input.UserName,
            Input.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (result.AdminAlreadyExists)
        {
            ModelState.AddModelError(string.Empty, "Initial admin has already been created.");
            await LoadPageStateAsync(adminExists: true, cancellationToken);
            return Page();
        }

        if (result.User is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            await LoadPageStateAsync(adminExists: false, cancellationToken);
            return Page();
        }

        await signInManager.SignInAsync(result.User, isPersistent: false);
        CreatedUserName = result.User.UserName;
        CertificateInput.SelfSignedDnsNames = GetDefaultCertificateNames();
        CurrentStep = SetupStep.HttpsCertificate;
        await LoadPageStateAsync(adminExists: true, cancellationToken);
        return Page();
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
        ValidateCertificateInput();

        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync(adminExists: true, cancellationToken);
            return Page();
        }

        try
        {
            ConfiguredCertificate = await setupService.ConfigureCertificateAsync(
                new FirstRunCertificateRequest(
                    CertificateInput.Mode,
                    CertificateInput.PfxFile,
                    CertificateInput.PfxPassword,
                    CertificateInput.CertificateFile,
                    CertificateInput.KeyFile,
                    CertificateInput.KeyPassword,
                    CertificateInput.SelfSignedDnsNames,
                    CertificateInput.SelfSignedValidYears),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "First-run HTTPS certificate setup failed. Mode={Mode}; RemoteIp={RemoteIp}",
                CertificateInput.Mode,
                HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadPageStateAsync(adminExists: true, cancellationToken);
            return Page();
        }

        CurrentStep = SetupStep.Complete;
        await LoadPageStateAsync(adminExists: true, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSkipCertificateAsync(CancellationToken cancellationToken)
    {
        var guardResult = await EnsureAuthenticatedSetupUserAsync(cancellationToken);
        if (guardResult is not null)
        {
            return guardResult;
        }

        ConfiguredCertificate = await setupService.GetCertificateAsync(cancellationToken);
        CurrentStep = SetupStep.Complete;
        await LoadPageStateAsync(adminExists: true, cancellationToken);
        return Page();
    }

    private async Task LoadPageStateAsync(bool adminExists, CancellationToken cancellationToken)
    {
        var state = await setupService.GetStateAsync(adminExists, ConfiguredCertificate, cancellationToken);
        PasswordPolicy = state.PasswordPolicy;
        ConfiguredCertificate = state.Certificate;
        HardeningChecklist = state.HardeningChecklist;
    }

    private async Task<bool> HasAnyUserAsync(CancellationToken cancellationToken)
    {
        return await setupService.HasAnyUserAsync(cancellationToken);
    }

    private async Task<IActionResult?> EnsureAuthenticatedSetupUserAsync(CancellationToken cancellationToken)
    {
        if (!await HasAnyUserAsync(cancellationToken))
        {
            CurrentStep = SetupStep.Welcome;
            await LoadPageStateAsync(adminExists: false, cancellationToken);
            return Page();
        }

        return User.Identity?.IsAuthenticated == true
            ? null
            : RedirectToPage("/Account/Login");
    }

    private void ValidateCertificateInput()
    {
        var errors = certificateFormValidator.Validate(
            CertificateInput.Mode,
            CertificateInput.PfxFile,
            CertificateInput.CertificateFile,
            CertificateInput.KeyFile);
        foreach (var error in errors)
        {
            var propertyName = error.Field switch
            {
                "pfxFile" => nameof(CertificateInputModel.PfxFile),
                "certificateFile" => nameof(CertificateInputModel.CertificateFile),
                "keyFile" => nameof(CertificateInputModel.KeyFile),
                _ => nameof(CertificateInputModel.Mode)
            };
            ModelState.AddModelError($"{nameof(CertificateInput)}.{propertyName}", error.Message);
        }
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
        AdminAccount,
        HttpsCertificate,
        Complete
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "User Name")]
        [StringLength(256)]
        [NoControlCharacters]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Confirm Password")]
        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class CertificateInputModel
    {
        public AdminHttpsCertificateMode Mode { get; set; } = AdminHttpsCertificateMode.SelfSigned;

        [AllowedFileExtensions(".pfx", ".p12")]
        public IFormFile? PfxFile { get; set; }

        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string? PfxPassword { get; set; }

        [AllowedFileExtensions(".crt", ".cer", ".pem")]
        public IFormFile? CertificateFile { get; set; }

        [AllowedFileExtensions(".key", ".pem")]
        public IFormFile? KeyFile { get; set; }

        [StringLength(ValidationLimits.MaximumSecretLength)]
        [NoControlCharacters]
        public string? KeyPassword { get; set; }

        [StringLength(ValidationLimits.MaximumTextLength)]
        [NoControlCharacters(true)]
        [CertificateNames]
        public string SelfSignedDnsNames { get; set; } = GetDefaultCertificateNames();

        [Range(1, 10)]
        public int SelfSignedValidYears { get; set; } = 2;
    }
}
