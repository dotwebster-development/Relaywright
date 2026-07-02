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
        var script = ReadRepositoryFile("scripts", "windows", "Install-Relaywright.ps1");

        Assert.Contains("#define AppVersion \"1.0.0\"", installer, StringComparison.Ordinal);
        Assert.Contains("OptionPage.Values[0] := False;", installer, StringComparison.Ordinal);
        Assert.Contains("FirewallPage.Values[0] := 'LocalSubnet';", installer, StringComparison.Ordinal);
        Assert.Contains("[switch]$EnableHttp", script, StringComparison.Ordinal);
        Assert.Contains("[string]$FirewallRemoteAddress = \"LocalSubnet\"", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DatabaseProvider = \"\"", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DatabaseConnectionStringFile = \"\"", script, StringComparison.Ordinal);
        Assert.Contains("Database__Provider=$($databaseSettings.Provider)", script, StringComparison.Ordinal);
        Assert.Contains("Database__ConnectionString=$($databaseSettings.ConnectionString)", script, StringComparison.Ordinal);
        Assert.Contains("DatabasePage := CreateInputOptionPage", installer, StringComparison.Ordinal);
        Assert.Contains("SQLite local database", installer, StringComparison.Ordinal);
        Assert.Contains("Microsoft SQL Server", installer, StringComparison.Ordinal);
        Assert.Contains("MySQL", installer, StringComparison.Ordinal);
        Assert.Contains("relaywright-database-connection.txt", installer, StringComparison.Ordinal);
        Assert.Contains("-DatabaseConnectionStringFile", installer, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LinuxInstallerDefaultsUseStableVersionAndHttpsOnly()
    {
        var script = ReadRepositoryFile("scripts", "linux", "install-relaywright.sh");

        Assert.Contains("version=\"1.0.0\"", script, StringComparison.Ordinal);
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

        Assert.Contains("<VersionPrefix Condition=\"'$(VersionPrefix)' == ''\">1.0.0</VersionPrefix>", props, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion Condition=\"'$(AssemblyVersion)' == ''\">1.0.0.0</AssemblyVersion>", props, StringComparison.Ordinal);
        Assert.DoesNotContain("beta.1", props, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WebsiteUsesCurrentReleaseRepositoryAndLatestLinks()
    {
        var site = ReadRepositoryFile("site", "index.html");

        Assert.Contains("https://github.com/dotwebster-development/Relaywright/releases/latest", site, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotwebster-development/Relaywright/wiki", site, StringComparison.Ordinal);
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
        var checklist = ReadRepositoryFile("docs", "RELEASE_CHECKLIST.md");
        var dashboard = ReadRepositoryFile("src", "Relaywright.Web", "Pages", "Index.cshtml");

        Assert.Contains("Repository docs are the source of truth", guidelines, StringComparison.Ordinal);
        Assert.Contains("GitHub Wiki is the operator manual", guidelines, StringComparison.Ordinal);
        Assert.Contains("Website and Wiki have been reviewed", checklist, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotwebster-development/Relaywright/wiki", dashboard, StringComparison.Ordinal);
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
