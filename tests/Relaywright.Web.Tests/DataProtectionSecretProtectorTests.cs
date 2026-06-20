using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DataProtectionSecretProtectorTests
{
    [Fact]
    public void RoundTripsRelaywrightProtectedSecrets()
    {
        using var fixture = SecretProtectionFixture.Create();

        var protectedText = fixture.Protector.Protect("secret-value");

        Assert.NotEqual("secret-value", protectedText);
        Assert.Equal("secret-value", fixture.Protector.Unprotect(protectedText));
    }

    private sealed class SecretProtectionFixture : IDisposable
    {
        private SecretProtectionFixture(string root, DataProtectionSecretProtector protector)
        {
            Root = root;
            Protector = protector;
        }

        private string Root { get; }

        public DataProtectionSecretProtector Protector { get; }

        public static SecretProtectionFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"relaywright-secret-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var paths = new AppPaths(root, new StorageOptions());
            paths.EnsureCreated();

            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(paths.KeyRingDirectory),
                builder => builder.SetApplicationName("Relaywright"));
            var protector = new DataProtectionSecretProtector(
                provider,
                NullLogger<DataProtectionSecretProtector>.Instance);

            return new SecretProtectionFixture(root, protector);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
