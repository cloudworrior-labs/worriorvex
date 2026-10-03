using Microsoft.Playwright;
using WorriorVex.Application.Settings;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class PaneTests(BrowserFixture browser) : UITest(browser)
{
    private ILocator NavHandle => Page.Locator(".wn-resizer[aria-label='Width of the navigation pane'], .wn-resizer[aria-label='Breedte van het navigatiepaneel']");
    private ILocator ListHandle => Page.Locator(".wn-resizer[aria-label='Width of the note list'], .wn-resizer[aria-label='Breedte van de notitielijst']");

    private Task<int> NavWidthAsync() => Page.EvaluateAsync<int>("() => Math.round(document.querySelector('.wn-nav').getBoundingClientRect().width)");
    private Task<int> ListWidthAsync() => Page.EvaluateAsync<int>("() => Math.round(document.querySelector('.wn-notelist').getBoundingClientRect().width)");

    private async Task DragAsync(ILocator handle, int dx)
    {
        var box = (await handle.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + 300);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width / 2 + dx, box.Y + 300, new MouseMoveOptions { Steps = 5 });
        await Page.Mouse.UpAsync();
        await Task.Delay(300);
    }

    [Fact]
    public async Task Both_pane_edges_drag_with_the_mouse_and_are_remembered()
    {
        await WaitForSavedAsync();
        var nav = await NavWidthAsync();
        var list = await ListWidthAsync();

        await DragAsync(NavHandle, 90);
        Assert.InRange(await NavWidthAsync(), nav + 80, nav + 100);
        await DragAsync(ListHandle, -60);
        Assert.InRange(await ListWidthAsync(), list - 70, list - 50);

        var settings = App.Get<ISettingsService>().Current;
        Assert.InRange(settings.NavigationWidth, nav + 80, nav + 100);
        Assert.InRange(settings.ListWidth, list - 70, list - 50);
    }

    [Fact]
    public async Task Both_pane_edges_move_with_the_arrow_keys()
    {
        await WaitForSavedAsync();
        var nav = await NavWidthAsync();
        await NavHandle.FocusAsync();
        await NavHandle.PressAsync("ArrowRight");
        await NavHandle.PressAsync("ArrowRight");
        await Expect(Page.Locator(".wn-nav")).ToHaveCSSAsync("width", $"{nav + 32}px");

        var list = await ListWidthAsync();
        await ListHandle.FocusAsync();
        await ListHandle.PressAsync("ArrowLeft");
        await Expect(Page.Locator(".wn-notelist")).ToHaveCSSAsync("width", $"{list - 16}px");
    }

    [Fact]
    public async Task Pane_edges_keep_working_after_a_language_change_and_a_visit_to_settings()
    {
        await App.Get<WorriorVex.Application.Notes.INotebookService>().CreateAsync("Projects");
        await Page.ReloadAsync();
        await WaitForSavedAsync();
        await Nav("Settings").ClickAsync();
        await Page.SelectOptionAsync("#wn-language", "nl");
        await Expect(Nav("Instellingen")).ToHaveCountAsync(1);
        await Nav("Alle notities").ClickAsync();
        await Page.Locator(".wn-notelist").WaitForAsync();

        var nav = await NavWidthAsync();
        await DragAsync(NavHandle, 70);
        Assert.InRange(await NavWidthAsync(), nav + 60, nav + 80);

        var list = await ListWidthAsync();
        await DragAsync(ListHandle, 40);
        Assert.InRange(await ListWidthAsync(), list + 30, list + 50);

        // And the tree keys moved with the new navigation element: down from the Inbox lands on the first notebook.
        await Nav("Postvak IN").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Nav("Projects")).ToBeFocusedAsync();
    }
}
