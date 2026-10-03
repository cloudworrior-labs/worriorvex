using Microsoft.Playwright;

namespace WorriorVex.UITests;

/// <summary>One Chromium for the whole run; each test opens its own page against its own app host.</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = default!;
    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        // Chromium by default; WORRIORVEX_UITEST_BROWSER=webkit runs the same tests on WebKit, the engine behind the macOS and Linux shells.
        var engine = Environment.GetEnvironmentVariable("WORRIORVEX_UITEST_BROWSER")?.ToLowerInvariant() switch
        {
            "webkit" => Playwright.Webkit,
            "firefox" => Playwright.Firefox,
            _ => Playwright.Chromium,
        };
        Browser = await engine.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }
}

[CollectionDefinition("browser")]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>;

/// <summary>A started app and a page on it; the common ground of every test.</summary>
public abstract class UITest(BrowserFixture browser) : IAsyncLifetime
{
    protected AppHost App { get; } = new();
    protected IPage Page { get; private set; } = default!;
    private IBrowserContext _context = default!;

    /// <summary>Set up the host before it starts (a package to open, a shell answer).</summary>
    protected virtual void Configure(AppHost app) { }

    public async Task InitializeAsync()
    {
        Configure(App);
        await App.StartAsync();
        _context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1400, Height = 900 } });
        Page = await _context.NewPageAsync();
        Page.Console += (_, m) => Console.WriteLine("BROWSER: " + m.Text);
        await Page.GotoAsync(App.Url);
        // The workspace is up once the navigation pane has rendered.
        try
        {
            await Page.Locator(".wn-nav").WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            using var http = new HttpClient();
            var html = await http.GetStringAsync(App.Url);
            var js = await http.GetAsync(App.Url + "_framework/blazor.web.js");
            var css = await http.GetAsync(App.Url + "_content/WorriorVex.UI/css/app.css");
            throw new InvalidOperationException($"url={App.Url} html={html[..Math.Min(600, html.Length)]} js={js.StatusCode} css={css.StatusCode} page={(await Page.ContentAsync())[..600]}");
        }
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await App.DisposeAsync();
    }

    /// <summary>The note's own saved state: waits until the status bar says it is saved.</summary>
    protected Task WaitForSavedAsync() => Page.Locator(".wn-status-saved").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

    protected ILocator Nav(string text) => Page.Locator(".wn-nav .wn-navitem", new PageLocatorOptions { HasTextString = text });
}
