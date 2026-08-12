using Xunit;

namespace Relaywright.Web.Tests;

public sealed class FrontendArchitectureTests
{
    [Fact]
    public void RazorPagesDoNotContainInlineScriptsOrBrowserConfirmCalls()
    {
        var pagesRoot = FindRepositoryPath("src", "Relaywright.Web", "Pages");
        foreach (var page in Directory.GetFiles(pagesRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(page);
            Assert.DoesNotContain("<script>", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confirm(", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick=", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onsubmit=", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LayoutUsesAccessibleSharedBehaviorContracts()
    {
        var layout = File.ReadAllText(FindRepositoryPath(
            "src", "Relaywright.Web", "Pages", "Shared", "_Layout.cshtml"));
        Assert.Contains("~/js/app-shell.js", layout, StringComparison.Ordinal);
        Assert.Contains("~/js/confirmation-dialog.js", layout, StringComparison.Ordinal);
        Assert.Contains("role=\"combobox\"", layout, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", layout, StringComparison.Ordinal);
        Assert.Contains("data-confirm-dialog", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedCertificateBehaviorUsesOneSharedModule()
    {
        var setup = File.ReadAllText(FindRepositoryPath(
            "src", "Relaywright.Web", "Pages", "Account", "Setup.cshtml"));
        var certificate = File.ReadAllText(FindRepositoryPath(
            "src", "Relaywright.Web", "Pages", "Settings", "WebCertificate.cshtml"));
        Assert.Contains("~/js/certificate-mode.js", setup, StringComparison.Ordinal);
        Assert.Contains("~/js/certificate-mode.js", certificate, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityPipelineIncludesStrictContentSecurityPolicy()
    {
        var pipeline = File.ReadAllText(FindRepositoryPath(
            "src", "Relaywright.Web", "Infrastructure", "SecurityHeadersMiddleware.cs"));
        Assert.Contains("Content-Security-Policy", pipeline, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void StylesheetsAreSplitByResponsibilityAndLoadedWithVersioning()
    {
        var layout = File.ReadAllText(FindRepositoryPath(
            "src", "Relaywright.Web", "Pages", "Shared", "_Layout.cshtml"));
        var expectedStyles = new[]
        {
            "~/css/foundation.css",
            "~/css/layout.css",
            "~/css/pages/setup.css",
            "~/css/components/surfaces.css",
            "~/css/components/forms.css",
            "~/css/components/data.css",
            "~/css/components/controls.css",
            "~/css/components/status.css",
            "~/css/responsive.css",
            "~/css/components/shared-ui.css"
        };

        var previousIndex = -1;
        foreach (var style in expectedStyles)
        {
            var index = layout.IndexOf(style, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"{style} should be loaded in responsibility order.");
            Assert.Contains(
                $"href=\"{style}\" asp-append-version=\"true\"",
                layout,
                StringComparison.Ordinal);
            previousIndex = index;
        }

        Assert.True(File.ReadAllLines(FindRepositoryPath(
            "src", "Relaywright.Web", "wwwroot", "css", "site.css")).Length < 20);
    }

    [Fact]
    public void WindowsAndLinuxDeploymentLanesRunRenderedBrowserValidation()
    {
        var windows = File.ReadAllText(FindRepositoryPath(
            ".github", "workflows", "deploy-windows-test.yml"));
        var linux = File.ReadAllText(FindRepositoryPath(
            ".github", "workflows", "deploy-linux-test.yml"));

        Assert.Contains("Relaywright.Web.BrowserTests", windows, StringComparison.Ordinal);
        Assert.Contains("RELAYWRIGHT_BROWSER_BASE_URL", windows, StringComparison.Ordinal);
        Assert.Contains("Relaywright.Web.BrowserTests", linux, StringComparison.Ordinal);
        Assert.Contains("RELAYWRIGHT_BROWSER_BASE_URL", linux, StringComparison.Ordinal);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Relaywright.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine([current!.FullName, .. segments]);
    }
}
