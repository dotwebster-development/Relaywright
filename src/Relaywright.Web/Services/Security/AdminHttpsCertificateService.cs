using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateService(
    AppPaths paths,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<AdminHttpsCertificateService> logger) : IAdminHttpsCertificateService
{
    public const string ProtectorPurpose = "Relaywright.Web.AdminHttpsCertificate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public static X509Certificate2? LoadConfiguredCertificate(
        AppPaths paths,
        IDataProtectionProvider dataProtectionProvider)
    {
        if (!File.Exists(paths.AdminHttpsCertificateConfigurationPath))
        {
            return null;
        }

        var configurationJson = File.ReadAllText(paths.AdminHttpsCertificateConfigurationPath);
        var configuration = JsonSerializer.Deserialize<AdminHttpsCertificateConfiguration>(configurationJson, JsonOptions)
            ?? throw new InvalidOperationException("Admin HTTPS certificate configuration could not be read.");

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        return LoadCertificate(configuration, UnprotectPassword(configuration, protector));
    }

    public async Task<AdminHttpsCertificateConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.AdminHttpsCertificateConfigurationPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(paths.AdminHttpsCertificateConfigurationPath);
        return await JsonSerializer.DeserializeAsync<AdminHttpsCertificateConfiguration>(stream, JsonOptions, cancellationToken);
    }

    public async Task<AdminHttpsCertificateConfiguration> SavePfxAsync(
        IFormFile certificateFile,
        string? password,
        CancellationToken cancellationToken)
    {
        if (certificateFile.Length <= 0)
        {
            throw new InvalidOperationException("Select a PFX certificate file.");
        }

        var targetPath = Path.Combine(paths.CertificateDirectory, "admin-web.pfx");
        var tempPath = CreateTemporaryPath(".pfx");

        try
        {
            await CopyUploadAsync(certificateFile, tempPath, cancellationToken);
            using var certificate = LoadPfx(tempPath, password);
            EnsureHasPrivateKey(certificate);

            File.Move(tempPath, targetPath, overwrite: true);

            var configuration = new AdminHttpsCertificateConfiguration
            {
                Mode = AdminHttpsCertificateMode.Pfx,
                CertificatePath = targetPath,
                ProtectedPassword = ProtectPassword(password),
                DnsNames = GetCertificateDnsNames(certificate),
                NotAfterUtc = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            await SaveConfigurationAsync(configuration, cancellationToken);
            logger.LogInformation("Admin HTTPS PFX certificate configured. CertificatePath={CertificatePath}", targetPath);
            return configuration;
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    public async Task<AdminHttpsCertificateConfiguration> SavePemAsync(
        IFormFile certificateFile,
        IFormFile keyFile,
        string? keyPassword,
        CancellationToken cancellationToken)
    {
        if (certificateFile.Length <= 0)
        {
            throw new InvalidOperationException("Select a certificate file.");
        }

        if (keyFile.Length <= 0)
        {
            throw new InvalidOperationException("Select a private key file.");
        }

        var certificatePath = Path.Combine(paths.CertificateDirectory, "admin-web.crt");
        var keyPath = Path.Combine(paths.CertificateDirectory, "admin-web.key");
        var tempCertificatePath = CreateTemporaryPath(".crt");
        var tempKeyPath = CreateTemporaryPath(".key");

        try
        {
            await CopyUploadAsync(certificateFile, tempCertificatePath, cancellationToken);
            await CopyUploadAsync(keyFile, tempKeyPath, cancellationToken);

            using var certificate = LoadPem(tempCertificatePath, tempKeyPath, keyPassword);
            EnsureHasPrivateKey(certificate);

            File.Move(tempCertificatePath, certificatePath, overwrite: true);
            File.Move(tempKeyPath, keyPath, overwrite: true);

            var configuration = new AdminHttpsCertificateConfiguration
            {
                Mode = AdminHttpsCertificateMode.Pem,
                CertificatePath = certificatePath,
                KeyPath = keyPath,
                ProtectedPassword = ProtectPassword(keyPassword),
                DnsNames = GetCertificateDnsNames(certificate),
                NotAfterUtc = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            await SaveConfigurationAsync(configuration, cancellationToken);
            logger.LogInformation("Admin HTTPS PEM certificate configured. CertificatePath={CertificatePath}; KeyPath={KeyPath}", certificatePath, keyPath);
            return configuration;
        }
        finally
        {
            DeleteIfExists(tempCertificatePath);
            DeleteIfExists(tempKeyPath);
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

        var password = CreateRandomPassword();
        var targetPath = Path.Combine(paths.CertificateDirectory, "admin-web-selfsigned.pfx");
        var tempPath = CreateTemporaryPath(".pfx");

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN={names[0]}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            false));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        foreach (var name in names)
        {
            if (IPAddress.TryParse(name, out var ipAddress))
            {
                subjectAlternativeNames.AddIpAddress(ipAddress);
            }
            else
            {
                subjectAlternativeNames.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(validYears);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        await File.WriteAllBytesAsync(tempPath, certificate.Export(X509ContentType.Pfx, password), cancellationToken);
        File.Move(tempPath, targetPath, overwrite: true);

        var configuration = new AdminHttpsCertificateConfiguration
        {
            Mode = AdminHttpsCertificateMode.SelfSigned,
            CertificatePath = targetPath,
            ProtectedPassword = ProtectPassword(password),
            DnsNames = names.ToArray(),
            NotAfterUtc = notAfter,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await SaveConfigurationAsync(configuration, cancellationToken);
        logger.LogInformation(
            "Admin HTTPS self-signed certificate generated. CertificatePath={CertificatePath}; DnsNames={DnsNames}; NotAfterUtc={NotAfterUtc}",
            targetPath,
            string.Join(",", names),
            notAfter);
        return configuration;
    }

    private static X509Certificate2 LoadCertificate(AdminHttpsCertificateConfiguration configuration, string? password)
    {
        return configuration.Mode switch
        {
            AdminHttpsCertificateMode.Pfx or AdminHttpsCertificateMode.SelfSigned => LoadPfx(configuration.CertificatePath, password),
            AdminHttpsCertificateMode.Pem => LoadPem(
                configuration.CertificatePath,
                configuration.KeyPath ?? throw new InvalidOperationException("Admin HTTPS certificate key path is required."),
                password),
            _ => throw new InvalidOperationException($"Unsupported admin HTTPS certificate mode '{configuration.Mode}'.")
        };
    }

    private static X509Certificate2 LoadPfx(string certificatePath, string? password)
    {
        return X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet,
            Pkcs12LoaderLimits.Defaults);
    }

    private static X509Certificate2 LoadPem(string certificatePath, string keyPath, string? password)
    {
        return string.IsNullOrWhiteSpace(password)
            ? X509Certificate2.CreateFromPemFile(certificatePath, keyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(certificatePath, password, keyPath);
    }

    private static void EnsureHasPrivateKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The HTTPS certificate must include a private key.");
        }
    }

    private static string[] GetCertificateDnsNames(X509Certificate2 certificate)
    {
        return certificate.GetNameInfo(X509NameType.DnsName, false) is { Length: > 0 } dnsName
            ? [dnsName]
            : [];
    }

    private static List<string> ParseNames(string dnsNames)
    {
        return dnsNames
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CreateRandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
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

    private async Task SaveConfigurationAsync(
        AdminHttpsCertificateConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var tempPath = CreateTemporaryPath(".json");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, paths.AdminHttpsCertificateConfigurationPath, overwrite: true);
    }

    private async Task CopyUploadAsync(IFormFile file, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.CertificateDirectory);
        await using var destination = File.Create(destinationPath);
        await file.CopyToAsync(destination, cancellationToken);
    }

    private string CreateTemporaryPath(string extension)
    {
        Directory.CreateDirectory(paths.CertificateDirectory);
        return Path.Combine(paths.CertificateDirectory, $"{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
