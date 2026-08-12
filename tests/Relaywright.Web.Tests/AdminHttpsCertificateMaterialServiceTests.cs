using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AdminHttpsCertificateMaterialServiceTests
{
    private readonly AdminHttpsCertificateMaterialService _service = new();

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateSelfSignedProducesLoadablePrivateKeyCertificate()
    {
        var generated = _service.GenerateSelfSigned(["localhost", "127.0.0.1"], 1);
        using var certificate = X509CertificateLoader.LoadPkcs12(
            generated.PfxBytes,
            generated.Password,
            X509KeyStorageFlags.EphemeralKeySet,
            Pkcs12LoaderLimits.Defaults);

        Assert.True(certificate.HasPrivateKey);
        Assert.Equal(["localhost", "127.0.0.1"], generated.DnsNames);
        Assert.True(generated.NotAfterUtc > DateTimeOffset.UtcNow.AddMonths(11));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LoadPfxRejectsCertificateWithoutPrivateKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"relaywright-public-cert-{Guid.NewGuid():N}.pfx");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=relaywright.test",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using var source = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));
            using var publicOnly = X509CertificateLoader.LoadCertificate(source.Export(X509ContentType.Cert));
            File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx));

            var exception = Assert.Throws<InvalidOperationException>(() => _service.LoadPfx(path, null));

            Assert.Contains("private key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
