using Microsoft.Playwright;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Tree;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class DragTests(BrowserFixture browser) : UITest(browser)
{
    [Fact]
    public async Task A_notebook_dragged_onto_another_becomes_a_folder_there_after_asking()
    {
        var notebooks = App.Get<INotebookService>();
        var source = await notebooks.CreateAsync("Databricks MI Certification");
        await notebooks.CreateAsync("Databricks");
        var chapter = await App.Get<ITreeService>().CreateFolderAsync(source.Id, null, "1.0 Designing");
        await App.Get<INoteService>().CreateAsync(source.Id, chapter.Id, "Introduction", "<p>x</p>");
        await Page.ReloadAsync();
        await Nav("Databricks MI Certification").WaitForAsync();

        await Page.Locator(".wn-navitem:text-is('Databricks MI Certification')")
            .DragToAsync(Page.Locator(".wn-navitem:text-is('Databricks')"));

        await Expect(Page.Locator(".wn-dialog h2")).ToContainTextAsync("Move the notebook");
        await Page.ClickAsync(".wn-dialog button:has-text('Move it there')");

        // Now a folder (level 1) under Databricks, holding its own folder and note.
        await Expect(Page.Locator(".wn-navitem:text-is('Databricks MI Certification')")).ToHaveAttributeAsync("data-level", "1");
        await Expect(Page.Locator(".wn-navitem:text-is('1.0 Designing')")).ToHaveAttributeAsync("data-level", "2");
        Assert.DoesNotContain(await notebooks.ListAsync(), n => n.Name == "Databricks MI Certification");
    }
}
