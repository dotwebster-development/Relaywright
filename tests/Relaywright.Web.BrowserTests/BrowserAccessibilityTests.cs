using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Xunit;

namespace Relaywright.Web.BrowserTests;

[Collection("Relaywright browser")]
public sealed class BrowserAccessibilityTests(RelaywrightWebFixture application) : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Fact]
    public async Task LoginAndAuthenticatedShellRenderWithoutClientErrors()
    {
        var clientErrors = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                clientErrors.Add(message.Text);
            }
        };
        Page.PageError += (_, error) => clientErrors.Add(error);

        var response = await Page.GotoAsync($"{application.BaseUrl}/Account/Login");
        Assert.NotNull(response);
        Assert.Contains("script-src 'self'", (await response.AllHeadersAsync())["content-security-policy"]);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Login" })).ToBeVisibleAsync();

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Combobox, new() { Name = "Search settings" })).ToBeVisibleAsync();
        Assert.Empty(clientErrors);
    }

    [Fact]
    public async Task SettingsSearchSupportsKeyboardNavigationAndAnnouncements()
    {
        await SignInAsync();
        var search = Page.GetByRole(AriaRole.Combobox, new() { Name = "Search settings" });
        await search.FillAsync("certificate");
        await Expect(search).ToHaveAttributeAsync("aria-expanded", "true");
        await search.PressAsync("ArrowDown");

        var activeId = await search.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrWhiteSpace(activeId));
        await Expect(Page.Locator($"#{activeId}")).ToHaveAttributeAsync("role", "option");
        await Expect(Page.Locator("[data-settings-search-announcement]"))
            .ToContainTextAsync("matching settings");

        await search.PressAsync("Escape");
        await Expect(search).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task SharedConfirmationDialogCancelsWithoutSubmitting()
    {
        await SignInAsync();
        await Page.GotoAsync($"{application.BaseUrl}/Settings/TrustedNetworks");

        var deleteButtons = Page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
        var deleteButtonCount = await deleteButtons.CountAsync();
        Assert.True(deleteButtonCount > 0);
        var rowCount = await Page.Locator("table tbody tr").CountAsync();
        await deleteButtons.First.ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToContainTextAsync("Delete trusted network");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.Locator("table tbody tr")).ToHaveCountAsync(rowCount);
    }

    [Fact]
    public async Task MobileNavigationMovesFocusIntoDrawerAndReturnsIt()
    {
        await Page.SetViewportSizeAsync(720, 900);
        await SignInAsync();

        var menu = Page.GetByRole(AriaRole.Button, new() { Name = "Menu" });
        await menu.ClickAsync();
        await Expect(menu).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.getElementById('app-rail').contains(document.activeElement)"));

        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.True(await menu.EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    private async Task SignInAsync()
    {
        await Page.GotoAsync($"{application.BaseUrl}/Account/Login");
        await Page.GetByLabel("User Name").FillAsync(application.UserName);
        await Page.GetByLabel("Password").FillAsync(application.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        await Page.WaitForURLAsync($"{application.BaseUrl}/");
    }
}

[CollectionDefinition("Relaywright browser", DisableParallelization = true)]
public sealed class RelaywrightBrowserCollection : ICollectionFixture<RelaywrightWebFixture>;
