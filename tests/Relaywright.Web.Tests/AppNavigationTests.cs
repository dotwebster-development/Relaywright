using Microsoft.AspNetCore.Http;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.UI;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AppNavigationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ChangePasswordRouteUsesAccountSecurityNavigationLabel()
    {
        var state = AppNavigation.Resolve(new PathString("/Account/ChangePassword"));
        var systemSection = Assert.Single(state.Sections, x => x.Key == AppNavigation.SystemKey);
        var item = Assert.Single(systemSection.Items, x => x.Page == "/Account/ChangePassword");

        Assert.Equal(AppNavigation.SystemKey, state.ActiveSectionKey);
        Assert.Equal("/Account/ChangePassword", state.ActiveItemPage);
        Assert.Equal("Account Security", item.Label);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AccountSecuritySearchEntryKeepsPasswordKeywords()
    {
        var state = AppNavigation.Resolve(new PathString("/Index"));
        var item = Assert.Single(state.SettingsSearchItems, x => x.Page == "/Account/ChangePassword");

        Assert.Equal("Account Security", item.Label);
        Assert.Contains("password", item.Keywords, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("account security", item.Keywords, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StatusLabelsUseOperatorFacingText()
    {
        Assert.Equal("In progress", StatusLabels.For(QueuedMessageStatus.InProgress));
        Assert.Equal("Retry scheduled", StatusLabels.For(QueuedMessageStatus.RetryScheduled));
        Assert.Equal("Failed", StatusLabels.ForRuntime("Faulted"));
    }
}
