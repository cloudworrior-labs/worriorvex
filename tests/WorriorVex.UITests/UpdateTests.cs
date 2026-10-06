using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class UpdateTests(BrowserFixture browser) : UITest(browser)
{
    protected override void Configure(AppHost app) => app.Updates.Latest = "0.6.2";

    [Fact]
    public async Task Ticking_the_setting_checks_at_once_and_the_badge_stays_in_the_top_bar()
    {
        // Off by default: nothing was asked.
        Assert.Equal(0, App.Updates.Checks);
        await Expect(Page.Locator(".wn-update-badge")).ToHaveCountAsync(0);

        await Nav("Settings").ClickAsync();
        await Page.Locator("label:has-text('Check for a new version when WorriorVex starts') input").CheckAsync();

        var badge = Page.Locator(".wn-update-badge");
        await Expect(badge).ToContainTextAsync("0.6.2");
        Assert.Equal(1, App.Updates.Checks);

        // Another message in the banner does not hide it.
        await Nav("All Notes").ClickAsync();
        await Expect(badge).ToBeVisibleAsync();
        await badge.ClickAsync();
        Assert.Contains(App.Shell.OpenedPages, u => u.Host == "example.test");
    }
}
