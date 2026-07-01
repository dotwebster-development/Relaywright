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
