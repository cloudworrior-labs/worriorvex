using Microsoft.Playwright;
using WorriorVex.Application.Export;
using WorriorVex.Application.Notes;
using static Microsoft.Playwright.Assertions;

namespace WorriorVex.UITests;

[Collection("browser")]
public sealed class OpenPackageTests(BrowserFixture browser) : UITest(browser)
{
    private static readonly string PackagePath = Path.Combine(Path.GetTempPath(), "worriorvex-uitests", $"open-{Guid.NewGuid():N}.worriorvex");

    protected override void Configure(AppHost app)
    {
        // A package written by another app instance, as a person would receive by e-mail or USB stick.
        Directory.CreateDirectory(Path.GetDirectoryName(PackagePath)!);
        var source = new AppHost();
        source.StartAsync().GetAwaiter().GetResult();
        try
        {
            var notebook = source.Get<INotebookService>().CreateAsync("From elsewhere").GetAwaiter().GetResult();
            source.Get<INoteService>().CreateAsync(notebook.Id, null, "Travelled note", "<p>arrived</p>").GetAwaiter().GetResult();
            source.Get<IExportService>().ExportPackageAsync(PackagePath).GetAwaiter().GetResult();
        }
        finally
        {
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        app.PackageToImport = PackagePath;
    }

    [Fact]
    public async Task A_package_given_at_start_is_imported_and_shown()
    {
        await Expect(Page.Locator(".wn-data .wn-report")).ToContainTextAsync("Import complete");
        await Expect(Nav("From elsewhere")).ToHaveCountAsync(1);
        Assert.Contains(await App.Get<INoteService>().ListAllAsync(), n => n.Title == "Travelled note");
    }
}
