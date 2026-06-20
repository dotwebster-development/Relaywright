using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ResolvesSpoolPathUnderSpoolRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root, new StorageOptions());

            var resolved = paths.GetSpoolAbsolutePath(Path.Combine("2026", "06", "20", "message.eml"));

            Assert.StartsWith(paths.SpoolRootDirectory, resolved, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("..\\outside.eml")]
    [InlineData("2026\\..\\..\\outside.eml")]
    public void RejectsSpoolPathTraversal(string relativePath)
    {
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root, new StorageOptions());

            Assert.Throws<InvalidOperationException>(() => paths.GetSpoolAbsolutePath(relativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smtp-relay-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
