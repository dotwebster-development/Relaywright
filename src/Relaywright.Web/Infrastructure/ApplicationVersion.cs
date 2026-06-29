using System.Reflection;

namespace Relaywright.Web.Infrastructure;

public static class ApplicationVersion
{
    private static readonly Assembly Assembly = typeof(ApplicationVersion).Assembly;

    public static string InformationalVersion { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public static string DisplayVersion { get; } = InformationalVersion.Split('+', 2)[0];
}
