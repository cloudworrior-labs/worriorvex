using Microsoft.Playwright;
using WorriorVex.Application.Attachments;
using WorriorVex.Application.Notes;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class AttachmentPanelTests(BrowserFixture browser) : UITest(browser)
{
    [Fact]
    public async Task Many_attachments_fold_away_and_never_squeeze_the_note()
    {
        var note = await App.Get<INoteService>().CreateAsync(title: "Lots of files", content: "<p>text</p>");
        var attachments = App.Get<IAttachmentService>();
        for (var i = 1; i <= 15; i++)
        {
            await attachments.AddAsync(note.Id, $"file-{i}.txt", "text/plain", new MemoryStream([1, 2, 3]));
        }

        await Page.ReloadAsync();
        await Page.Locator(".wn-noteitem:has-text('Lots of files')").ClickAsync();
        var toggle = Page.Locator(".wn-attachments-toggle");
        await Expect(toggle).ToContainTextAsync("15");

        // Folded by default: one line, the editor keeps the space.
        await Expect(Page.Locator(".wn-attachment")).ToHaveCountAsync(0);
        var surface = await Page.Locator(".wn-editor-surface").BoundingBoxAsync();
        Assert.True(surface!.Height > 400, $"editor surface is {surface.Height}px");

        // Open: all 15 are there, but the list is capped and scrolls; the editor still has room.
        await toggle.ClickAsync();
        await Expect(Page.Locator(".wn-attachment")).ToHaveCountAsync(15);
        var list = await Page.Locator("#wn-attachment-list").BoundingBoxAsync();
        var viewport = Page.ViewportSize!.Height;
        Assert.True(list!.Height <= viewport * 0.31, $"list is {list.Height}px of {viewport}");
        var surfaceOpen = await Page.Locator(".wn-editor-surface").BoundingBoxAsync();
        Assert.True(surfaceOpen!.Height > 200, $"editor surface is {surfaceOpen.Height}px with the list open");

        // The choice is remembered for the next note.
        Assert.True(App.Get<WorriorVex.Application.Settings.ISettingsService>().Current.AttachmentsExpanded);
    }
}
