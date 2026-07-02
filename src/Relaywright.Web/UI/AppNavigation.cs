using Microsoft.AspNetCore.Http;

namespace Relaywright.Web.UI;

public sealed record AppNavItem(string Label, string Page);

public sealed record SettingsSearchItem(string Label, string Group, string Page, string Keywords);

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

    public IReadOnlyList<SettingsSearchItem> SettingsSearchItems { get; } = AppNavigation.SettingsSearchItems;

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
    public const string SystemKey = "system";
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
                new AppNavItem("Submission Policy", "/Settings/SubmissionPolicy"),
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
            SystemKey,
            "System",
            "Admin and maintenance",
            "/Operations/Alerts",
            [
                new AppNavItem("Alerts", "/Operations/Alerts"),
                new AppNavItem("Backups", "/Operations/Backups"),
                new AppNavItem("Change History", "/Operations/ChangeHistory"),
                new AppNavItem("Web Interface", "/Settings/WebHttps"),
                new AppNavItem("Certificate", "/Settings/WebCertificate"),
                new AppNavItem("Account Security", "/Account/ChangePassword")
            ]),
        new AppNavSection(
            DiagnosticsKey,
            "Diagnostics",
            "Connectivity and checks",
            "/Diagnostics/Index",
            [
                new AppNavItem("Diagnostics", "/Diagnostics/Index"),
                new AppNavItem("Flow Checker", "/Diagnostics/Flow"),
                new AppNavItem("Test Email", "/Diagnostics/TestEmail")
            ])
    ];

    internal static readonly SettingsSearchItem[] SettingsSearchItems =
    [
        new SettingsSearchItem(
            "Relay Settings",
            "Settings",
            "/Settings/Relay",
            "smtp listener bind address port hostname starttls upstream smart host relay timeout authentication oauth basic username password retry cleanup retention delivery queue certificate"),
        new SettingsSearchItem(
            "Submission Policy",
            "Settings",
            "/Settings/SubmissionPolicy",
            "policy max message size recipients allowed senders blocked senders recipient domains allowed domains blocked domains acceptance rules"),
        new SettingsSearchItem(
            "Trusted IPs",
            "Settings",
            "/Settings/TrustedNetworks",
            "trusted networks cidr ip devices printers scanner owner location rate limit hourly max size recipients allowed blocked senders domains"),
        new SettingsSearchItem(
            "Alerts",
            "System",
            "/Operations/Alerts",
            "alerts thresholds cooldown email recipients notifications queue depth oldest message failed expired listener down disk space certificate expiry upstream failures"),
        new SettingsSearchItem(
            "Backups",
            "System",
            "/Operations/Backups",
            "backup restore readiness schedule retention validation encryption password download delete database spool keys certificates"),
        new SettingsSearchItem(
            "Web Interface",
            "System",
            "/Settings/WebHttps",
            "admin web interface listener https http port restart service browser ui"),
        new SettingsSearchItem(
            "Certificate",
            "System",
            "/Settings/WebCertificate",
            "https certificate pfx p12 pem private key self signed dns names expiry password cert upload"),
        new SettingsSearchItem(
            "Change History",
            "System",
            "/Operations/ChangeHistory",
            "change history snapshots rollback revert settings configuration restore previous"),
        new SettingsSearchItem(
            "Account Security",
            "System",
            "/Account/ChangePassword",
            "admin account password credentials login change password account security sessions")
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

        if (currentPath.StartsWith("/Settings/SubmissionPolicy", StringComparison.OrdinalIgnoreCase))
        {
            return (SettingsKey, "/Settings/SubmissionPolicy");
        }

        if (currentPath.StartsWith("/Settings/WebHttps", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Settings/WebHttps");
        }

        if (currentPath.StartsWith("/Settings/WebCertificate", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Settings/WebCertificate");
        }

        if (currentPath.StartsWith("/Settings", StringComparison.OrdinalIgnoreCase))
        {
            return (SettingsKey, "/Settings/Relay");
        }

        if (currentPath.StartsWith("/Operations/Alerts", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Operations/Alerts");
        }

        if (currentPath.StartsWith("/Operations/Backups", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Operations/Backups");
        }

        if (currentPath.StartsWith("/Operations/ChangeHistory", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Operations/ChangeHistory");
        }

        if (currentPath.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase))
        {
            return (SystemKey, "/Account/ChangePassword");
        }

        if (currentPath.StartsWith("/Operations/Status", StringComparison.OrdinalIgnoreCase))
        {
            return (OverviewKey, "/Index");
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
            if (currentPath.StartsWith("/Diagnostics/TestEmail", StringComparison.OrdinalIgnoreCase))
            {
                return (DiagnosticsKey, "/Diagnostics/TestEmail");
            }

            if (currentPath.StartsWith("/Diagnostics/Flow", StringComparison.OrdinalIgnoreCase))
            {
                return (DiagnosticsKey, "/Diagnostics/Flow");
            }

            return (DiagnosticsKey, "/Diagnostics/Index");
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
