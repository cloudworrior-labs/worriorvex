using Microsoft.Playwright;
using WorriorVex.Application.Notes;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class CalendarTests(BrowserFixture browser) : UITest(browser)
{
    [Fact]
    public async Task Several_notes_are_typed_into_a_day_edited_and_opened()
    {
        await Nav("Calendar").ClickAsync();
        var today = Page.Locator(".wn-cal-day.is-today");
        await Expect(Page.Locator(".wn-cal-day")).ToHaveCountAsync(42);

        // Click the day, type, Enter; the box stays for the next one.
        await today.ClickAsync();
        await Expect(today.Locator(".wn-inline-rename")).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("Dentist 10:00");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(today.Locator(".wn-cal-entry")).ToHaveCountAsync(1);
        await Expect(today.Locator(".wn-inline-rename")).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("Call supplier");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(today.Locator(".wn-cal-entry")).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("Escape");

        // Click an entry to change its words.
        await today.Locator(".wn-cal-entry", new LocatorLocatorOptions { HasTextString = "Dentist" }).ClickAsync();
        await Page.FillAsync(".wn-cal-day.is-today .wn-inline-rename", "Dentist 11:30");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(today.Locator(".wn-cal-entry").First).ToHaveTextAsync("Dentist 11:30");

        var notes = await App.Get<INoteService>().ListAllAsync();
        Assert.Equal(["Call supplier", "Dentist 11:30"], notes.Select(n => n.Title).OrderBy(t => t));

        // ↗ opens the note in the editor, in the Calendar notebook.
        var row = today.Locator(".wn-cal-entryrow", new LocatorLocatorOptions { HasTextString = "Call supplier" });
        await row.HoverAsync();
        await row.Locator(".wn-cal-action").First.ClickAsync();
        await Expect(Page.Locator(".wn-title")).ToHaveValueAsync("Call supplier");
        await Expect(Page.Locator(".wn-notelist h2")).ToHaveTextAsync("Calendar");

        // Next month and back.
        await Nav("Calendar").First.ClickAsync();
        var title = await Page.Locator("#wn-calendar-title").InnerTextAsync();
        await Page.ClickAsync(".wn-cal-nav button[aria-label='Next month']");
        await Expect(Page.Locator("#wn-calendar-title")).Not.ToHaveTextAsync(title);
        await Page.ClickAsync(".wn-cal-nav button:has-text('Today')");
        await Expect(Page.Locator("#wn-calendar-title")).ToHaveTextAsync(title);
        await Expect(Page.Locator(".wn-cal-day.is-today .wn-cal-entry")).ToHaveCountAsync(2);
    }
}

[Collection("browser")]
public sealed class TemplateFromHelpPageTests(BrowserFixture browser) : UITest(browser)
{
    [Fact]
    public async Task A_template_note_made_while_on_the_calendar_opens_in_the_inbox()
    {
        await Nav("Calendar").ClickAsync();
        await Expect(Page.Locator(".wn-cal-day")).ToHaveCountAsync(42);

        await Page.ClickAsync(".wn-topbar button[aria-label='New note from a template']");
        await Page.ClickAsync(".wn-picker-list .wn-noteitem:has-text('Meeting notes')");

        await Expect(Page.Locator(".wn-title")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^Meeting notes "));
        await Expect(Page.Locator(".wn-prose h2").First).ToHaveTextAsync("Attendees");
        await Expect(Page.Locator(".wn-notelist h2")).ToHaveTextAsync("Inbox");
        await Expect(Page.Locator(".wn-prose input[type=checkbox]")).ToHaveCountAsync(1);

        // Typing into it saves normally afterwards.
        await Page.Locator(".wn-prose").ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync(" decided");
        await WaitForSavedAsync();
        var notes = await App.Get<INoteService>().ListAllAsync();
        Assert.Contains(notes, n => n.Title.StartsWith("Meeting notes "));
        Assert.DoesNotContain(App.Logs, l => l.Contains("[Error]") || l.Contains("[Critical]"));
    }
}
