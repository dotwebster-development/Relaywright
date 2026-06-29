using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class WebCertificateModel(
    IAdminHttpsCertificateService adminHttpsCertificateService,
    IApplicationRestartService applicationRestartService,
    IOperationalEventService eventService,
    ILogger<WebCertificateModel> logger) : PageModel
{
    [BindProperty]
    public CertificateInputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public AdminHttpsCertificateConfiguration? CurrentCertificate { get; private set; }

    public bool HasCurrentCertificate => CurrentCertificate is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveCertificateAsync(CancellationToken cancellationToken)
    {
        try
        {
            CurrentCertificate = Input.Mode switch
            {
                AdminHttpsCertificateMode.Pfx => await adminHttpsCertificateService.SavePfxAsync(
                    RequireFile(Input.PfxFile, "Select a PFX certificate file."),
                    Input.PfxPassword,
                    cancellationToken),
                AdminHttpsCertificateMode.Pem => await adminHttpsCertificateService.SavePemAsync(
                    RequireFile(Input.CertificateFile, "Select a certificate file."),
                    RequireFile(Input.KeyFile, "Select a private key file."),
                    Input.KeyPassword,
                    cancellationToken),
                AdminHttpsCertificateMode.SelfSigned => await adminHttpsCertificateService.GenerateSelfSignedAsync(
                    Input.SelfSignedDnsNames,
                    Input.SelfSignedValidYears,
                    cancellationToken),
                _ => throw new InvalidOperationException("Choose a certificate option.")
            };

            var restart = await applicationRestartService.RequestRestartAsync(
                "Admin web HTTPS certificate changed.",
                User.Identity?.Name,
                cancellationToken);
            StatusMessage = $"Web HTTPS certificate saved. {restart.Message}";
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.Security,
                Message = $"Admin web HTTPS certificate configured using {CurrentCertificate.Mode}.",
                RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, cancellationToken);

            logger.LogInformation(
                "Admin web HTTPS certificate saved from settings page. Mode={Mode}; User={UserName}; RemoteIp={RemoteIp}",
                CurrentCertificate.Mode,
                User.Identity?.Name,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Admin web HTTPS certificate save failed from settings page. Mode={Mode}; User={UserName}; RemoteIp={RemoteIp}",
                Input.Mode,
                User.Identity?.Name,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            await LoadPageStateAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
    {
        CurrentCertificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);
    }

    private static IFormFile RequireFile(IFormFile? file, string message)
    {
        return file is { Length: > 0 }
            ? file
            : throw new InvalidOperationException(message);
    }

    public static string GetDefaultCertificateNames()
    {
        return string.Join(", ", new[] { "localhost", Environment.MachineName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
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
}
