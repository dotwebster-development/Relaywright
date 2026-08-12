using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateService(
    IDataProtectionProvider dataProtectionProvider,
    AdminHttpsCertificateConfigurationStore configurationStore,
    AdminHttpsCertificateFileStore fileStore,
    AdminHttpsCertificateMaterialService materialService,
    ILogger<AdminHttpsCertificateService> logger) : IAdminHttpsCertificateService
{
    public const string ProtectorPurpose = "Relaywright.Web.AdminHttpsCertificate";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public static X509Certificate2? LoadConfiguredCertificate(
        AppPaths paths,
        IDataProtectionProvider dataProtectionProvider)
    {
        var configuration = new AdminHttpsCertificateConfigurationStore(paths).Load();
        if (configuration is null)
        {
            return null;
        }

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        var password = UnprotectPassword(configuration, protector);
        return new AdminHttpsCertificateMaterialService().Load(configuration, password);
    }

    public Task<AdminHttpsCertificateConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        return configurationStore.LoadAsync(cancellationToken);
    }

    public async Task<AdminHttpsCertificateConfiguration> SavePfxAsync(
        IFormFile certificateFile,
        string? password,
        CancellationToken cancellationToken)
    {
        ValidateUpload(certificateFile, "Select a PFX certificate file.");
        ValidateFileExtension(certificateFile.FileName, "PFX certificate file", [".pfx", ".p12"]);
        ValidatePassword(password, "PFX password");

        var targetPath = fileStore.GetCertificatePath("admin-web.pfx");
        var tempPath = fileStore.CreateTemporaryPath(".pfx");
        try
        {
            await fileStore.CopyUploadAsync(certificateFile, tempPath, cancellationToken);
            using var certificate = materialService.LoadPfx(tempPath, password);
            fileStore.MoveIntoPlace(tempPath, targetPath);

            var configuration = CreateConfiguration(
                AdminHttpsCertificateMode.Pfx,
                targetPath,
                null,
                password,
                materialService.GetDnsNames(certificate),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
            await configurationStore.SaveAsync(configuration, cancellationToken);
            logger.LogInformation("Admin HTTPS PFX certificate configured. CertificatePath={CertificatePath}", targetPath);
            return configuration;
        }
        finally
        {
            fileStore.DeleteIfExists(tempPath);
        }
    }

    public async Task<AdminHttpsCertificateConfiguration> SavePemAsync(
        IFormFile certificateFile,
        IFormFile keyFile,
        string? keyPassword,
        CancellationToken cancellationToken)
    {
        ValidateUpload(certificateFile, "Select a certificate file.");
        ValidateUpload(keyFile, "Select a private key file.");
        ValidateFileExtension(certificateFile.FileName, "Certificate file", [".crt", ".cer", ".pem"]);
        ValidateFileExtension(keyFile.FileName, "Private key file", [".key", ".pem"]);
        ValidatePassword(keyPassword, "Private key password");

        var certificatePath = fileStore.GetCertificatePath("admin-web.crt");
        var keyPath = fileStore.GetCertificatePath("admin-web.key");
        var tempCertificatePath = fileStore.CreateTemporaryPath(".crt");
        var tempKeyPath = fileStore.CreateTemporaryPath(".key");
        try
        {
            await fileStore.CopyUploadAsync(certificateFile, tempCertificatePath, cancellationToken);
            await fileStore.CopyUploadAsync(keyFile, tempKeyPath, cancellationToken);
            using var certificate = materialService.LoadPem(tempCertificatePath, tempKeyPath, keyPassword);
            fileStore.MoveIntoPlace(tempCertificatePath, certificatePath);
            fileStore.MoveIntoPlace(tempKeyPath, keyPath);

            var configuration = CreateConfiguration(
                AdminHttpsCertificateMode.Pem,
                certificatePath,
                keyPath,
                keyPassword,
                materialService.GetDnsNames(certificate),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
            await configurationStore.SaveAsync(configuration, cancellationToken);
            logger.LogInformation(
                "Admin HTTPS PEM certificate configured. CertificatePath={CertificatePath}; KeyPath={KeyPath}",
                certificatePath,
                keyPath);
            return configuration;
        }
        finally
        {
            fileStore.DeleteIfExists(tempCertificatePath);
            fileStore.DeleteIfExists(tempKeyPath);
        }
    }

    public async Task<AdminHttpsCertificateConfiguration> GenerateSelfSignedAsync(
        string dnsNames,
        int validYears,
        CancellationToken cancellationToken)
    {
        if (validYears is < 1 or > 10)
        {
            throw new InvalidOperationException("Self-signed certificate validity must be between 1 and 10 years.");
        }

        var names = ParseNames(dnsNames);
        if (names.Count == 0)
        {
            names.Add("localhost");
        }

        foreach (var name in names)
        {
            if (!ValidationRules.IsCertificateName(name))
            {
                throw new InvalidOperationException($"Self-signed certificate contains an invalid DNS name or IP address: {name}.");
            }
        }

        var generated = materialService.GenerateSelfSigned(names, validYears);
        var targetPath = fileStore.GetCertificatePath("admin-web-selfsigned.pfx");
        var tempPath = fileStore.CreateTemporaryPath(".pfx");
        try
        {
            await fileStore.WriteAsync(tempPath, generated.PfxBytes, cancellationToken);
            fileStore.MoveIntoPlace(tempPath, targetPath);
        }
        finally
        {
            fileStore.DeleteIfExists(tempPath);
        }

        var configuration = CreateConfiguration(
            AdminHttpsCertificateMode.SelfSigned,
            targetPath,
            null,
            generated.Password,
            generated.DnsNames,
            generated.NotAfterUtc);
        await configurationStore.SaveAsync(configuration, cancellationToken);
        logger.LogInformation(
            "Admin HTTPS self-signed certificate generated. CertificatePath={CertificatePath}; DnsNames={DnsNames}; NotAfterUtc={NotAfterUtc}",
            targetPath,
            string.Join(",", names),
            generated.NotAfterUtc);
        return configuration;
    }

    private AdminHttpsCertificateConfiguration CreateConfiguration(
        AdminHttpsCertificateMode mode,
        string certificatePath,
        string? keyPath,
        string? password,
        string[] dnsNames,
        DateTimeOffset notAfterUtc)
    {
        return new AdminHttpsCertificateConfiguration
        {
            Mode = mode,
            CertificatePath = certificatePath,
            KeyPath = keyPath,
            ProtectedPassword = ProtectPassword(password),
            DnsNames = dnsNames,
            NotAfterUtc = notAfterUtc,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static void ValidateUpload(IFormFile file, string message)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static List<string> ParseNames(string dnsNames)
    {
        return dnsNames
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateFileExtension(string fileName, string label, IReadOnlyCollection<string> extensions)
    {
        if (!ValidationRules.HasAllowedExtension(fileName, extensions))
        {
            throw new InvalidOperationException(ValidationMessages.FileExtension(label, extensions));
        }
    }

    private static void ValidatePassword(string? password, string label)
    {
        if (password is null)
        {
            return;
        }

        if (password.Length > ValidationLimits.MaximumSecretLength)
        {
            throw new InvalidOperationException(ValidationMessages.MaximumLength(label, ValidationLimits.MaximumSecretLength));
        }

        if (ValidationRules.ContainsDisallowedControlCharacter(password, allowLineBreaks: false, out _))
        {
            throw new InvalidOperationException(ValidationMessages.UnsupportedControlCharacter(label));
        }
    }

    private static string? UnprotectPassword(
        AdminHttpsCertificateConfiguration configuration,
        IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(configuration.ProtectedPassword)
            ? null
            : protector.Unprotect(configuration.ProtectedPassword);
    }

    private string ProtectPassword(string? password)
    {
        return string.IsNullOrWhiteSpace(password)
            ? string.Empty
            : _protector.Protect(password);
    }
}
