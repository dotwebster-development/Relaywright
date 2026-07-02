using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;

namespace Relaywright.Web.Tests.Support;

internal sealed class TempAppData : IDisposable
{
    private TempAppData(string root, AppPaths paths)
    {
        Root = root;
        Paths = paths;
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public static TempAppData Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"relaywright-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var paths = new AppPaths(root, new StorageOptions());
        paths.EnsureCreated();

        return new TempAppData(root, paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
