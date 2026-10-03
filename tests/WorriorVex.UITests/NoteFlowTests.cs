using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using WorriorVex.Application.Notes;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class NoteFlowTests(BrowserFixture browser) : UITest(browser)
{
    [Fact]
    public async Task A_note_can_be_written_and_is_saved()
    {
        await Page.ClickAsync(".wn-topbar .wn-button-primary");
        await Page.FillAsync(".wn-title", "Shopping");
        await Page.Keyboard.PressAsync("Enter");
        // Enter hands the focus to the text; typing starts once it has arrived, as a person's would.
        await Expect(Page.Locator(".wn-prose")).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("milk and eggs");
        await WaitForSavedAsync();

        var notes = await App.Get<INoteService>().ListAllAsync();
        var note = Assert.Single(notes);
        Assert.Equal("Shopping", note.Title);
        var detail = (await App.Get<INoteService>().GetAsync(note.Id))!;
        Assert.Contains("milk and eggs", detail.Content);
        await Expect(Page.Locator(".wn-notelist .wn-noteitem-title")).ToHaveTextAsync("Shopping");
    }

    [Fact]
    public async Task Notebooks_and_folders_grow_as_a_tree()
    {
        await Page.ClickAsync(".wn-nav button[title='New notebook']");
        await Page.FillAsync("#wn-prompt-input", "Projects");
        await Page.Keyboard.PressAsync("Enter");
        await Nav("Projects").WaitForAsync();

        // The + at the end of the row adds a branch.
        await Nav("Projects").HoverAsync();
        await Page.ClickAsync(".wn-navrow:has(.wn-navitem:text-is('Projects')) .wn-row-add");
        await Page.FillAsync("#wn-prompt-input", "Alpha");
        await Page.Keyboard.PressAsync("Enter");
        await Nav("Alpha").WaitForAsync();
        await Expect(Nav("Alpha")).ToHaveAttributeAsync("data-level", "1");

        // The box at the left collapses and expands it.
        var twisty = Page.Locator(".wn-navrow:has(.wn-navitem:text-is('Projects')) .wn-twisty");
        await Expect(twisty).ToHaveTextAsync("−");
        await twisty.ClickAsync();
        await Expect(Nav("Alpha")).ToHaveCountAsync(0);
        await twisty.ClickAsync();
        await Expect(Nav("Alpha")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Renaming_in_place_keeps_the_body()
    {
        var notes = App.Get<INoteService>();
        var note = await notes.CreateAsync(title: "Draft", content: "<p>the body</p>");
        await Page.ReloadAsync();
        await Page.Locator(".wn-noteitem", new PageLocatorOptions { HasTextString = "Draft" }).WaitForAsync();

        var item = Page.Locator(".wn-noteitem:has-text('Draft')");
        await item.ClickAsync();
        await Page.Locator(".wn-title").WaitForAsync();
        await item.FocusAsync();
        await item.PressAsync("F2");
        await Page.FillAsync(".wn-inline-rename", "Final");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Locator(".wn-noteitem", new PageLocatorOptions { HasTextString = "Final" }).WaitForAsync();

        var saved = (await notes.GetAsync(note.Id))!;
        Assert.Equal("Final", saved.Title);
        Assert.Equal("<p>the body</p>", saved.Content);
    }

    [Fact]
    public async Task Search_finds_a_note_and_suggests_a_correction()
    {
        var notes = App.Get<INoteService>();
        await notes.CreateAsync(title: "Quarterly budget", content: "<p>numbers</p>");
        await notes.CreateAsync(title: "Holiday plan", content: "<p>beach</p>");
        await Page.ReloadAsync();
        await Page.Locator(".wn-nav").WaitForAsync();

        await Page.FillAsync(".wn-search-input", "budget");
        await Expect(Page.Locator(".wn-notelist .wn-noteitem")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".wn-notelist .wn-noteitem-title")).ToContainTextAsync("budget");

        await Page.FillAsync(".wn-search-input", "budgit");
        await Expect(Page.Locator(".wn-suggestion")).ToContainTextAsync("budget");
        await Page.ClickAsync(".wn-suggestion .wn-linkbutton");
        await Expect(Page.Locator(".wn-notelist .wn-noteitem")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task The_language_setting_changes_the_whole_interface()
    {
        await Nav("Settings").ClickAsync();
        await Page.SelectOptionAsync("#wn-language", "nl");
        await Expect(Nav("Instellingen")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".wn-settings h1")).ToHaveTextAsync("Instellingen");
        await Page.SelectOptionAsync("#wn-language", "en");
        try
        {
            await Expect(Nav("Settings")).ToHaveCountAsync(1);
        }
        catch (PlaywrightException)
        {
            Assert.Fail(string.Join("\n", App.Logs.Where(l => !l.Contains("EntityFrameworkCore")).Select(l => l[..Math.Min(1200, l.Length)])));
        }
    }

    [Fact]
    public async Task Deleting_a_note_moves_it_to_the_trash_and_back()
    {
        var notes = App.Get<INoteService>();
        var note = await notes.CreateAsync(title: "Doomed", content: "<p>x</p>");
        await Page.ReloadAsync();
        await Page.Locator(".wn-noteitem:has-text('Doomed')").ClickAsync();
        await Page.Locator(".wn-title").WaitForAsync();

        await Page.ClickAsync(".wn-title-row button[title='Move this note to the trash']");
        await Expect(Page.Locator(".wn-noteitem:has-text('Doomed')")).ToHaveCountAsync(0);

        await Nav("Trash").ClickAsync();
        await Page.Locator(".wn-noteitem:has-text('Doomed')").ClickAsync();
        await Page.ClickAsync(".wn-trash-detail .wn-button-primary");
        await Expect(Page.Locator(".wn-banner-info")).ToContainTextAsync("restored");
        Assert.Null((await notes.GetAsync(note.Id))!.DeletedAt);
    }
}
