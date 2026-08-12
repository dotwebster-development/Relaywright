using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateMaterialService
{
    public X509Certificate2 Load(
        AdminHttpsCertificateConfiguration configuration,
        string? password)
    {
        var certificate = configuration.Mode switch
        {
            AdminHttpsCertificateMode.Pfx or AdminHttpsCertificateMode.SelfSigned => LoadPfx(
                configuration.CertificatePath,
                password),
            AdminHttpsCertificateMode.Pem => LoadPem(
                configuration.CertificatePath,
                configuration.KeyPath ?? throw new InvalidOperationException("Admin HTTPS certificate key path is required."),
                password),
            _ => throw new InvalidOperationException($"Unsupported admin HTTPS certificate mode '{configuration.Mode}'.")
        };

        EnsureHasPrivateKey(certificate);
        return certificate;
    }

    public X509Certificate2 LoadPfx(string certificatePath, string? password)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            X509KeyStorageFlags.MachineKeySet,
            Pkcs12LoaderLimits.Defaults);
        EnsureHasPrivateKey(certificate);
        return certificate;
    }

    public X509Certificate2 LoadPem(string certificatePath, string keyPath, string? password)
    {
        var certificate = string.IsNullOrWhiteSpace(password)
            ? X509Certificate2.CreateFromPemFile(certificatePath, keyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(certificatePath, password, keyPath);
        EnsureHasPrivateKey(certificate);
        return certificate;
    }

    public GeneratedAdminHttpsCertificate GenerateSelfSigned(
        IReadOnlyList<string> dnsNames,
        int validYears)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN={dnsNames[0]}",
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
        foreach (var name in dnsNames)
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
        return new GeneratedAdminHttpsCertificate(
            certificate.Export(X509ContentType.Pfx, password),
            password,
            [.. dnsNames],
            notAfter);
    }

    public string[] GetDnsNames(X509Certificate2 certificate)
    {
        return certificate.GetNameInfo(X509NameType.DnsName, false) is { Length: > 0 } dnsName
            ? [dnsName]
            : [];
    }

    private static void EnsureHasPrivateKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("The HTTPS certificate must include a private key.");
        }
    }
}
