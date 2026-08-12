using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Security;

public sealed class FirstRunSetupService(
    UserManager<ApplicationUser> userManager,
    IOperationalEventService eventService,
    IAdminHttpsCertificateService certificateService,
    IAdminWebListenerConfigurationService listenerService,
    IOptions<IdentityOptions> identityOptions,
    IOptions<BootstrapAdminOptions> bootstrapAdminOptions,
    IHostEnvironment environment,
    ILogger<FirstRunSetupService> logger)
{
    private static readonly SemaphoreSlim InitialAdminGate = new(1, 1);

    public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken) =>
        userManager.Users.AnyAsync(cancellationToken);

    public async Task<FirstRunAdminCreationResult> CreateInitialAdminAsync(
        string userName,
        string password,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        await InitialAdminGate.WaitAsync(cancellationToken);
        try
        {
            if (await HasAnyUserAsync(cancellationToken))
            {
                logger.LogWarning(
                    "First-run setup rejected because an admin already exists. RemoteIp={RemoteIp}",
                    remoteIpAddress);
                return FirstRunAdminCreationResult.AlreadyCreated();
            }

            var normalizedUserName = userName.Trim();
            var admin = new ApplicationUser
            {
                UserName = normalizedUserName,
                DisplayName = normalizedUserName,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "First-run admin creation failed. UserName={UserName}; RemoteIp={RemoteIp}; ErrorCodes={ErrorCodes}",
                    normalizedUserName,
                    remoteIpAddress,
                    string.Join(",", result.Errors.Select(x => x.Code)));
                return FirstRunAdminCreationResult.Failed(result.Errors);
            }

            logger.LogWarning(
                "First-run admin user created. UserId={UserId}; UserName={UserName}; RemoteIp={RemoteIp}",
                admin.Id,
                admin.UserName,
                remoteIpAddress);
            await eventService.WriteAsync(new OperationalEventRequest
            {
                Category = OperationalEventCategory.Security,
                Message = "Initial admin user created through first-run setup.",
                RemoteIpAddress = remoteIpAddress
            }, cancellationToken);

            return FirstRunAdminCreationResult.Succeeded(admin);
        }
        finally
        {
            InitialAdminGate.Release();
        }
    }

    public async Task<FirstRunSetupState> GetStateAsync(
        bool adminExists,
        AdminHttpsCertificateConfiguration? configuredCertificate,
        CancellationToken cancellationToken)
    {
        var passwordPolicy = PasswordPolicySummary.FromOptions(identityOptions.Value);
        var certificate = configuredCertificate
            ?? await certificateService.GetConfigurationAsync(cancellationToken);
        var listener = await listenerService.GetConfigurationAsync(cancellationToken);
        var checklist = SetupHardeningChecklist.Create(
            adminExists,
            passwordPolicy,
            certificate,
            listener,
            bootstrapAdminOptions.Value,
            environment);
        return new FirstRunSetupState(passwordPolicy, certificate, checklist);
    }

    public Task<AdminHttpsCertificateConfiguration> ConfigureCertificateAsync(
        FirstRunCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return request.Mode switch
        {
            AdminHttpsCertificateMode.Pfx => certificateService.SavePfxAsync(
                RequireFile(request.PfxFile, "Select a PFX certificate file."),
                request.PfxPassword,
                cancellationToken),
            AdminHttpsCertificateMode.Pem => certificateService.SavePemAsync(
                RequireFile(request.CertificateFile, "Select a certificate file."),
                RequireFile(request.KeyFile, "Select a private key file."),
                request.KeyPassword,
                cancellationToken),
            AdminHttpsCertificateMode.SelfSigned => certificateService.GenerateSelfSignedAsync(
                request.SelfSignedDnsNames,
                request.SelfSignedValidYears,
                cancellationToken),
            _ => throw new InvalidOperationException("Choose a certificate option.")
        };
    }

    public Task<AdminHttpsCertificateConfiguration?> GetCertificateAsync(CancellationToken cancellationToken) =>
        certificateService.GetConfigurationAsync(cancellationToken);

    private static IFormFile RequireFile(IFormFile? file, string message) =>
        file is { Length: > 0 } ? file : throw new InvalidOperationException(message);
}

public sealed record FirstRunSetupState(
    PasswordPolicySummary PasswordPolicy,
    AdminHttpsCertificateConfiguration? Certificate,
    SetupHardeningChecklist HardeningChecklist);

public sealed record FirstRunCertificateRequest(
    AdminHttpsCertificateMode Mode,
    IFormFile? PfxFile,
    string? PfxPassword,
    IFormFile? CertificateFile,
    IFormFile? KeyFile,
    string? KeyPassword,
    string SelfSignedDnsNames,
    int SelfSignedValidYears);

public sealed record FirstRunAdminCreationResult(
    ApplicationUser? User,
    bool AdminAlreadyExists,
    IReadOnlyList<IdentityError> Errors)
{
    public static FirstRunAdminCreationResult Succeeded(ApplicationUser user) => new(user, false, []);

    public static FirstRunAdminCreationResult AlreadyCreated() => new(null, true, []);

    public static FirstRunAdminCreationResult Failed(IEnumerable<IdentityError> errors) =>
        new(null, false, errors.ToList());
}
