using Relaywright.Web.Pages.Settings;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ReleaseDefaultsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void WebListenerSettingsInputDefaultsToHttpDisabled()
    {
        var input = new WebHttpsModel.ListenerInputModel();

        Assert.False(input.EnableHttp);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WindowsInstallerDefaultsUseStableVersionHttpsOnlyAndLocalSubnetFirewall()
    {
        var installer = ReadRepositoryFile("installer", "windows", "Relaywright.iss");
        var engine = ReadRepositoryFile("installer", "windows", "RelaywrightInstallerEngine.ps1");
        var installDocs = ReadRepositoryFile("INSTALL_WINDOWS.md");

        Assert.Contains("#define AppVersion \"1.0.2\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Install-Relaywright.ps1", installer, StringComparison.Ordinal);
        Assert.Contains("RelaywrightInstallerEngine.ps1", installer, StringComparison.Ordinal);
        Assert.Contains("Relaywright - SMTP relay gateway", installer, StringComparison.Ordinal);
        Assert.Contains("CommandLineParameter('FIREWALL_REMOTE_ADDRESS', 'LocalSubnet')", installer, StringComparison.Ordinal);
        Assert.Contains("CommandLineBoolean('ENABLE_HTTP', False)", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Generate a self-signed HTTPS certificate if needed", installer, StringComparison.Ordinal);
        Assert.Contains("DatabasePage := CreateInputOptionPage", installer, StringComparison.Ordinal);
        Assert.Contains("SQLite local database", installer, StringComparison.Ordinal);
        Assert.Contains("Microsoft SQL Server", installer, StringComparison.Ordinal);
        Assert.Contains("MySQL", installer, StringComparison.Ordinal);
        Assert.Contains("DatabaseModePage := CreateInputOptionPage", installer, StringComparison.Ordinal);
        Assert.Contains("Server name:", installer, StringComparison.Ordinal);
        Assert.Contains("Database name:", installer, StringComparison.Ordinal);
        Assert.Contains("User name:", installer, StringComparison.Ordinal);
        Assert.Contains("Password:", installer, StringComparison.Ordinal);
        Assert.Contains("Use default ports", installer, StringComparison.Ordinal);
        Assert.Contains("Enable admin HTTP", installer, StringComparison.Ordinal);
        Assert.Contains("ReviewPage := CreateOutputMsgMemoPage", installer, StringComparison.Ordinal);
        Assert.Contains("Database__Provider=$provider", engine, StringComparison.Ordinal);
        Assert.Contains("Database__ConnectionString=$databaseConnectionString", engine, StringComparison.Ordinal);
        Assert.Contains("Restore-ServiceSnapshot", engine, StringComparison.Ordinal);
        Assert.Contains("/DATABASE_SERVER=sql01.example.local", installDocs, StringComparison.Ordinal);
        Assert.Contains("/ENABLE_HTTP=0", installDocs, StringComparison.Ordinal);
        Assert.Contains("/FIREWALL_REMOTE_ADDRESS=LocalSubnet", installDocs, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LinuxInstallerDefaultsUseStableVersionAndHttpsOnly()
    {
        var script = ReadRepositoryFile("scripts", "linux", "install-relaywright.sh");

        Assert.Contains("repo=\"${RELAYWRIGHT_GITHUB_REPOSITORY:-dotwebster-development/Relaywright}\"", script, StringComparison.Ordinal);
        Assert.Contains("version=\"1.0.2\"", script, StringComparison.Ordinal);
        Assert.Contains("enable_http=false", script, StringComparison.Ordinal);
        Assert.Contains("runtime_identifier=\"${RELAYWRIGHT_LINUX_RUNTIME:-}\"", script, StringComparison.Ordinal);
        Assert.Contains("--runtime RID", script, StringComparison.Ordinal);
        Assert.Contains("artifact_name=\"relaywright-${version}-${runtime_identifier}.tar.gz\"", script, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", script, StringComparison.Ordinal);
        Assert.Contains("linux-arm", script, StringComparison.Ordinal);
        Assert.Contains("database_provider=\"${RELAYWRIGHT_DATABASE_PROVIDER:-}\"", script, StringComparison.Ordinal);
        Assert.Contains("database_connection_string=\"${RELAYWRIGHT_DATABASE_CONNECTION_STRING:-}\"", script, StringComparison.Ordinal);
        Assert.Contains("--database-provider PROVIDER", script, StringComparison.Ordinal);
        Assert.Contains("--database-connection-string-file PATH", script, StringComparison.Ordinal);
        Assert.Contains("write_env_line \"Database__Provider\" \"$database_provider\"", script, StringComparison.Ordinal);
        Assert.Contains("write_env_line \"Database__ConnectionString\" \"$database_connection_string\"", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ReleaseWorkflowPublishesLinuxX64AndArmPackages()
    {
        var workflow = ReadRepositoryFile(".github", "workflows", "release.yml");

        Assert.Contains("for runtime in linux-x64 linux-arm64 linux-arm; do", workflow, StringComparison.Ordinal);
        Assert.Contains("relaywright-${RELAYWRIGHT_VERSION}-${runtime}.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("relaywright-${RELAYWRIGHT_VERSION}-linux-x64.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("relaywright-${RELAYWRIGHT_VERSION}-linux-arm64.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("relaywright-${RELAYWRIGHT_VERSION}-linux-arm.tar.gz", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LinuxReleaseValidationCanTargetArm64Runner()
    {
        var workflow = ReadRepositoryFile(".github", "workflows", "validate-linux-release.yml");

        Assert.Contains("runner_architecture:", workflow, StringComparison.Ordinal);
        Assert.Contains("- ARM64", workflow, StringComparison.Ordinal);
        Assert.Contains("- ${{ inputs.runner_architecture }}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildDefaultsUseStableVersion()
    {
        var props = ReadRepositoryFile("Directory.Build.props");

        Assert.Contains("<VersionPrefix Condition=\"'$(VersionPrefix)' == ''\">1.0.2</VersionPrefix>", props, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion Condition=\"'$(AssemblyVersion)' == ''\">1.0.2.0</AssemblyVersion>", props, StringComparison.Ordinal);
        Assert.DoesNotContain("beta.1", props, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WebsiteUsesCurrentReleaseRepositoryAndLatestLinks()
    {
        var site = ReadRepositoryFile("site", "index.html");

        Assert.Contains("https://github.com/dotwebster-development/Relaywright/releases/latest", site, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotwebster-development/Relaywright/wiki", site, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotwebster-development/Relaywright/blob/main/SUPPORT.md", site, StringComparison.Ordinal);
        Assert.Contains("--repo dotwebster-development/Relaywright --version latest", site, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/relaywright/relaywright", site, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WebsiteMentionsArm64AndUsesDashboardScreenshot()
    {
        var site = ReadRepositoryFile("site", "index.html");
        var screenshotPath = Path.Combine(RepositoryRoot, "site", "assets", "dashboard-preview.png");

        Assert.Contains("Linux ARM64", site, StringComparison.Ordinal);
        Assert.Contains("dashboard-preview.png", site, StringComparison.Ordinal);
        Assert.True(File.Exists(screenshotPath));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DocumentationGuidelinesAndDashboardWikiLinkAreTracked()
    {
        var guidelines = ReadRepositoryFile("docs", "DOCUMENTATION_GUIDELINES.md");
        var docsReadme = ReadRepositoryFile("docs", "README.md");
        var checklist = ReadRepositoryFile("docs", "RELEASE_CHECKLIST.md");
        var projectReview = ReadRepositoryFile("docs", "PROJECT_IMPROVEMENT_REVIEW.md");
        var wikiDraftsReadme = ReadRepositoryFile("docs", "wiki-drafts", "README.md");
        var readme = ReadRepositoryFile("README.md");
        var dashboard = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Index.cshtml");

        Assert.Contains("Repository docs are the source of truth", guidelines, StringComparison.Ordinal);
        Assert.Contains("GitHub Wiki is the operator manual", guidelines, StringComparison.Ordinal);
        Assert.Contains("docs/wiki-drafts/", guidelines, StringComparison.Ordinal);
        Assert.Contains("Relaywright Documentation Map", docsReadme, StringComparison.Ordinal);
        Assert.Contains("Release records", docsReadme, StringComparison.Ordinal);
        Assert.Contains("Historical snapshot", projectReview, StringComparison.Ordinal);
        Assert.Contains("Historical Improvement Backlog", projectReview, StringComparison.Ordinal);
        Assert.Contains("[Documentation map](docs/README.md)", readme, StringComparison.Ordinal);
        Assert.Contains("Website and Wiki have been reviewed", checklist, StringComparison.Ordinal);
        Assert.Contains("docs/wiki-drafts/", checklist, StringComparison.Ordinal);
        Assert.Contains("The GitHub Wiki repository is initialized", wikiDraftsReadme, StringComparison.Ordinal);
        Assert.Contains("git push origin master", wikiDraftsReadme, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotwebster-development/Relaywright/wiki", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PublicRepositoryHasProfessionalSupportAndContributionEntryPoints()
    {
        var readme = ReadRepositoryFile("README.md");
        var support = ReadRepositoryFile("SUPPORT.md");
        var contributing = ReadRepositoryFile("CONTRIBUTING.md");
        var issueConfig = ReadRepositoryFile(".github", "ISSUE_TEMPLATE", "config.yml");
        var bugReport = ReadRepositoryFile(".github", "ISSUE_TEMPLATE", "bug_report.yml");
        var featureRequest = ReadRepositoryFile(".github", "ISSUE_TEMPLATE", "feature_request.yml");
        var pullRequestTemplate = ReadRepositoryFile(".github", "pull_request_template.md");

        Assert.Contains("relaywright-logo.svg", readme, StringComparison.Ordinal);
        Assert.Contains("releases/latest", readme, StringComparison.Ordinal);
        Assert.Contains("[Support](SUPPORT.md)", readme, StringComparison.Ordinal);
        Assert.Contains("[CONTRIBUTING.md](CONTRIBUTING.md)", readme, StringComparison.Ordinal);
        Assert.Contains("Choose The Right Route", support, StringComparison.Ordinal);
        Assert.Contains("Never post:", support, StringComparison.Ordinal);
        Assert.Contains("Pull Requests", contributing, StringComparison.Ordinal);
        Assert.Contains("blank_issues_enabled: false", issueConfig, StringComparison.Ordinal);
        Assert.Contains("name: Bug report", bugReport, StringComparison.Ordinal);
        Assert.Contains("name: Feature request", featureRequest, StringComparison.Ordinal);
        Assert.Contains("Safety And Operations", pullRequestTemplate, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PagesValidationUsesTheProductVersionAsItsDefault()
    {
        var validator = ReadRepositoryFile("scripts", "Validate-PagesSite.ps1");
        var workflow = ReadRepositoryFile(".github", "workflows", "pages.yml");

        Assert.Contains("Directory.Build.props", validator, StringComparison.Ordinal);
        Assert.Contains("VersionPrefix", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("else { \"1.0.2\" }", validator, StringComparison.Ordinal);
        Assert.Contains("\"Directory.Build.props\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"scripts/Validate-PagesSite.ps1\"", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DashboardRendersTheRecordedReadinessChecklist()
    {
        var dashboard = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Index.cshtml");
        var styles = ReadRepositoryFile("src", "Relaywright.Web", "wwwroot", "css", "site.css");
        var firstRunGuide = ReadRepositoryFile("docs", "wiki-drafts", "First-Run-Setup.md");

        Assert.Contains("Production checklist", dashboard, StringComparison.Ordinal);
        Assert.Contains("Model.Readiness.Items", dashboard, StringComparison.Ordinal);
        Assert.Contains("readiness-meter", dashboard, StringComparison.Ordinal);
        Assert.Contains(".readiness-list", styles, StringComparison.Ordinal);
        Assert.Contains("production-readiness checklist", firstRunGuide, StringComparison.Ordinal);
        Assert.Contains("after the latest relay configuration save", firstRunGuide, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RelaySettingsPageUsesSectionPartials()
    {
        var page = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Settings", "Relay.cshtml");
        var listener = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Settings", "_RelayListenerFields.cshtml");
        var upstream = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Settings", "_RelayUpstreamFields.cshtml");
        var delivery = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Settings", "_RelayDeliveryRetentionFields.cshtml");

        Assert.Contains("<partial name=\"_RelayListenerFields\"", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_RelayUpstreamFields\"", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_RelayDeliveryRetentionFields\"", page, StringComparison.Ordinal);
        Assert.Contains("settings-block-title\">Listener", listener, StringComparison.Ordinal);
        Assert.Contains("settings-block-title\">Upstream", upstream, StringComparison.Ordinal);
        Assert.Contains("settings-block-title\">Delivery And Retention", delivery, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        return File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Relaywright.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root could not be found.");
        }
    }
}
