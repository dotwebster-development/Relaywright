using Microsoft.AspNetCore.Http;

namespace Relaywright.Web.UI;

public sealed record AppNavItem(string Label, string Page);

public sealed record AppNavSection(
    string Key,
    string Label,
    string Description,
    string Page,
    IReadOnlyList<AppNavItem> Items);

public sealed class AppNavState(
    IReadOnlyList<AppNavSection> sections,
    string activeSectionKey,
    string activeItemPage)
{
    public IReadOnlyList<AppNavSection> Sections { get; } = sections;

    public string ActiveSectionKey { get; } = activeSectionKey;

    public string ActiveItemPage { get; } = activeItemPage;

    public AppNavSection ActiveSection =>
        Sections.FirstOrDefault(x => string.Equals(x.Key, ActiveSectionKey, StringComparison.OrdinalIgnoreCase))
        ?? Sections[0];

    public bool IsSectionActive(AppNavSection section) =>
        string.Equals(section.Key, ActiveSectionKey, StringComparison.OrdinalIgnoreCase);

    public bool IsItemActive(AppNavItem item) =>
        string.Equals(item.Page, ActiveItemPage, StringComparison.OrdinalIgnoreCase);
}

public static class AppNavigation
{
    public const string OverviewKey = "overview";
    public const string SettingsKey = "settings";
    public const string OperationsKey = "operations";
    public const string DiagnosticsKey = "diagnostics";

    private static readonly AppNavSection[] NavigationSections =
    [
        new AppNavSection(
            OverviewKey,
            "Overview",
            "Live service summary",
            "/Index",
            [new AppNavItem("Dashboard", "/Index")]),
        new AppNavSection(
            SettingsKey,
            "Settings",
            "Relay and trust policy",
            "/Settings/Relay",
            [
                new AppNavItem("Relay Settings", "/Settings/Relay"),
                new AppNavItem("Web HTTPS", "/Settings/WebHttps"),
                new AppNavItem("Trusted IPs", "/Settings/TrustedNetworks")
            ]),
        new AppNavSection(
            OperationsKey,
            "Operations",
            "Queue and event tracking",
            "/Queue/Index",
            [
                new AppNavItem("Queue", "/Queue/Index"),
                new AppNavItem("Logs", "/Logs/Index")
            ]),
        new AppNavSection(
            DiagnosticsKey,
            "Diagnostics",
            "Connectivity and checks",
            "/Diagnostics/Index",
            [
                new AppNavItem("Diagnostics", "/Diagnostics/Index"),
                new AppNavItem("Test Email", "/Diagnostics/TestEmail")
            ])
    ];

    public static AppNavState Resolve(PathString requestPath)
    {
        var currentPath = Normalize(requestPath);
        var (sectionKey, itemPage) = ResolveTargets(currentPath);
        return new AppNavState(NavigationSections, sectionKey, itemPage);
    }

    private static (string SectionKey, string ItemPage) ResolveTargets(string currentPath)
    {
        if (currentPath.Equals("/", StringComparison.OrdinalIgnoreCase)
            || currentPath.Equals("/Index", StringComparison.OrdinalIgnoreCase))
        {
            return (OverviewKey, "/Index");
        }

        if (currentPath.StartsWith("/Settings/TrustedNetworks", StringComparison.OrdinalIgnoreCase))
        {
            return (SettingsKey, "/Settings/TrustedNetworks");
        }

        if (currentPath.StartsWith("/Settings/WebHttps", StringComparison.OrdinalIgnoreCase))
        {
            return (SettingsKey, "/Settings/WebHttps");
        }

        if (currentPath.StartsWith("/Settings", StringComparison.OrdinalIgnoreCase))
        {
            return (SettingsKey, "/Settings/Relay");
        }

        if (currentPath.StartsWith("/Logs", StringComparison.OrdinalIgnoreCase))
        {
            return (OperationsKey, "/Logs/Index");
        }

        if (currentPath.StartsWith("/Messages", StringComparison.OrdinalIgnoreCase)
            || currentPath.StartsWith("/Queue", StringComparison.OrdinalIgnoreCase))
        {
            return (OperationsKey, "/Queue/Index");
        }

        if (currentPath.StartsWith("/Diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            return currentPath.StartsWith("/Diagnostics/TestEmail", StringComparison.OrdinalIgnoreCase)
                ? (DiagnosticsKey, "/Diagnostics/TestEmail")
                : (DiagnosticsKey, "/Diagnostics/Index");
        }

        return (OverviewKey, "/Index");
    }

    private static string Normalize(PathString requestPath)
    {
        if (!requestPath.HasValue || string.IsNullOrWhiteSpace(requestPath.Value))
        {
            return "/";
        }

        return requestPath.Value!;
    }
}
