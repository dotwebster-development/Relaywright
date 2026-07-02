using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AdminHttpsCertificateServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task SavePfxStoresProtectedPasswordAndLoadableCertificate()
    {
        using var fixture = CertificateFixture.Create();
        var password = "cert-secret";
        await using var pfxStream = CreatePfxStream("relaywright.test", password);
        var file = new FormFile(pfxStream, 0, pfxStream.Length, "CertificateInput.PfxFile", "relaywright.pfx");

        var configuration = await fixture.Service.SavePfxAsync(file, password, CancellationToken.None);

        Assert.Equal(AdminHttpsCertificateMode.Pfx, configuration.Mode);
        Assert.True(File.Exists(configuration.CertificatePath));
        Assert.True(File.Exists(fixture.Paths.AdminHttpsCertificateConfigurationPath));
        Assert.DoesNotContain(password, await File.ReadAllTextAsync(fixture.Paths.AdminHttpsCertificateConfigurationPath));

        using var certificate = AdminHttpsCertificateService.LoadConfiguredCertificate(
            fixture.Paths,
            fixture.DataProtectionProvider);

        Assert.NotNull(certificate);
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GenerateSelfSignedStoresLoadableCertificate()
    {
        using var fixture = CertificateFixture.Create();

        var configuration = await fixture.Service.GenerateSelfSignedAsync("localhost, relaywright.test", 1, CancellationToken.None);

        Assert.Equal(AdminHttpsCertificateMode.SelfSigned, configuration.Mode);
        Assert.Contains("localhost", configuration.DnsNames);
        Assert.True(File.Exists(configuration.CertificatePath));

        using var certificate = AdminHttpsCertificateService.LoadConfiguredCertificate(
            fixture.Paths,
            fixture.DataProtectionProvider);

        Assert.NotNull(certificate);
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SavePemStoresCertificateAndKey()
    {
        using var fixture = CertificateFixture.Create();
        var pem = CreatePemFiles("relaywright.test");
        await using var certificateStream = new MemoryStream(pem.CertificateBytes);
        await using var keyStream = new MemoryStream(pem.KeyBytes);
        var certificateFile = new FormFile(certificateStream, 0, certificateStream.Length, "CertificateInput.CertificateFile", "relaywright.crt");
        var keyFile = new FormFile(keyStream, 0, keyStream.Length, "CertificateInput.KeyFile", "relaywright.key");

        var configuration = await fixture.Service.SavePemAsync(certificateFile, keyFile, null, CancellationToken.None);

        Assert.Equal(AdminHttpsCertificateMode.Pem, configuration.Mode);
        Assert.True(File.Exists(configuration.CertificatePath));
        Assert.True(File.Exists(configuration.KeyPath));

        using var certificate = AdminHttpsCertificateService.LoadConfiguredCertificate(
            fixture.Paths,
            fixture.DataProtectionProvider);

        Assert.NotNull(certificate);
        Assert.True(certificate.HasPrivateKey);
    }

    private static MemoryStream CreatePfxStream(string dnsName, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={dnsName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));
        return new MemoryStream(certificate.Export(X509ContentType.Pfx, password));
    }

    private static (byte[] CertificateBytes, byte[] KeyBytes) CreatePemFiles(string dnsName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={dnsName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));
        return (
            System.Text.Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()),
            System.Text.Encoding.ASCII.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));
    }

    private sealed class CertificateFixture : IDisposable
    {
        private CertificateFixture(
            string root,
            AppPaths paths,
            IDataProtectionProvider dataProtectionProvider,
            AdminHttpsCertificateService service)
        {
            Root = root;
            Paths = paths;
            DataProtectionProvider = dataProtectionProvider;
            Service = service;
        }

        private string Root { get; }

        public AppPaths Paths { get; }

        public IDataProtectionProvider DataProtectionProvider { get; }

        public AdminHttpsCertificateService Service { get; }

        public static CertificateFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"relaywright-admin-https-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var paths = new AppPaths(root, new StorageOptions());
            paths.EnsureCreated();

            var provider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(
                new DirectoryInfo(paths.KeyRingDirectory),
                builder => builder.SetApplicationName("Relaywright"));
            var service = new AdminHttpsCertificateService(
                paths,
                provider,
                NullLogger<AdminHttpsCertificateService>.Instance);

            return new CertificateFixture(root, paths, provider, service);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
