using System.Security.Cryptography;
using System.Text;
using WorriorVex.Application.Content;
using WorriorVex.Application.Import;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure.Import;

namespace WorriorVex.IntegrationTests;

public class KeepNoteImportTests
{
    private const long Created = 1_262_304_000;   // 2010-01-01T00:00:00Z
    private const long Modified = 1_262_390_400;  // 2010-01-02T00:00:00Z

    [Fact]
    public async Task A_version_6_notebook_is_imported_whole_and_the_source_is_untouched()
    {
        using var data = new TempDataDirectory();
        var source = Path.Combine(data.Path, "source");
        var ids = WriteNotebook(source, version: 6);
        var before = Snapshot(source);
        await using var app = await TestApp.StartAsync(data.Path);

        var scan = await app.Get<IKeepNoteImporter>().ScanAsync(source);
        Assert.Equal(("My KeepNote", 6, 1, 4, 1, 1, 1, 1, 1), (scan.NotebookName, scan.FormatVersion, scan.Folders, scan.Pages, scan.PagesWithChildren, scan.Attachments, scan.Images, scan.ItemsInTrash, scan.Links));
        Assert.Equal(2, scan.UnsupportedAttributes["icon"]);
        Assert.Contains("title_bgcolor", scan.UnsupportedAttributes.Keys);
        Assert.Empty(scan.Problems);

        var lines = new List<string>();
        var report = await app.Get<IKeepNoteImporter>().ImportAsync(source, new Progress<string>(lines.Add));

        Assert.Equal("My KeepNote", report.NotebookName);
        Assert.Equal((2, 4, 1, 1, 1, 1, 0), (report.Folders, report.Notes, report.Attachments, report.Images, report.ItemsInTrash, report.Links, report.UnresolvedLinks));
        Assert.Empty(report.Problems);
        Assert.Equal(before, Snapshot(source));

        // Structure: Work (folder) > Plan (page with a child page -> folder + note) > Details; Loose at the top; trash holds Old.
        var notebook = (await app.Notebooks.ListAsync()).Single(n => n.Name == "My KeepNote");
        var folders = await app.Tree.ListFoldersAsync(notebook.Id);
        var work = folders.Single(f => f.Name == "Work" && f.ParentId is null);
        var planFolder = folders.Single(f => f.Name == "Plan" && f.ParentId == work.Id);
        var topNotes = await app.Notes.ListAsync(notebook.Id);
        Assert.Equal(["Loose"], topNotes.Select(n => n.Title));
        var planNotes = await app.Notes.ListAsync(notebook.Id, planFolder.Id);
        Assert.Equal(["Details", "Plan"], planNotes.Select(n => n.Title).Order());

        var plan = await app.Notes.GetAsync(planNotes.Single(n => n.Title == "Plan").Id);
        Assert.NotNull(plan);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Created), plan.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Modified), plan.UpdatedAt);
        Assert.Contains("<p>First line</p>", plan.Content);
        Assert.Contains("<strong>bold</strong>", plan.Content);
        Assert.Contains("<code>mono</code>", plan.Content);
        Assert.Contains("kept text", plan.Content);
        Assert.DoesNotContain("<font", plan.Content);
        Assert.DoesNotContain("style=", plan.Content);
        Assert.DoesNotContain("nbk://", plan.Content);

        var details = planNotes.Single(n => n.Title == "Details");
        Assert.Contains(NoteContentRules.NoteLinkAddress(details.Id), plan.Content);
        Assert.Equal([plan.Id], (await app.Links.GetBacklinksAsync(details.Id)).Select(l => l.NoteId));

        var planAttachments = await app.Attachments.ListAsync(plan.Id);
        Assert.Equal(["budget.xlsx", "diagram.png"], planAttachments.Select(a => a.FileName).Order());
        var image = planAttachments.Single(a => a.FileName == "diagram.png");
        Assert.Contains(NoteContentRules.AttachmentSource(image.StoredFileName), plan.Content);
        Assert.Equal(3, image.Size);
        Assert.Equal(2, Directory.GetFiles(app.Get<IApplicationDataPathProvider>().AttachmentsDirectory).Length);

        var trash = await app.Trash.ListAsync();
        Assert.Equal("Old", Assert.Single(trash).Name);
        Assert.Empty(await app.Search.SearchAsync("forgotten"));
        Assert.Equal(plan.Id, Assert.Single(await app.Search.SearchAsync("mono")).NoteId);
        Assert.Contains(lines, l => l.Contains("Plan"));
        _ = ids;
    }

    [Fact]
    public async Task A_version_5_notebook_with_flat_attributes_is_read_too()
    {
        using var data = new TempDataDirectory();
        var source = Path.Combine(data.Path, "source");
        WriteNotebook(source, version: 5);
        await using var app = await TestApp.StartAsync(data.Path);

        var report = await app.Get<IKeepNoteImporter>().ImportAsync(source);

        Assert.Equal(4, report.Notes);
        Assert.Equal(1, report.Links);
    }

    [Fact]
    public async Task Older_formats_missing_folders_and_broken_pages_are_reported_not_guessed()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var importer = app.Get<IKeepNoteImporter>();

        await Assert.ThrowsAsync<KeepNoteImportException>(() => importer.ScanAsync(Path.Combine(data.Path, "nowhere")));
        var plain = Directory.CreateDirectory(Path.Combine(data.Path, "plain")).FullName;
        await Assert.ThrowsAsync<KeepNoteImportException>(() => importer.ScanAsync(plain));

        var old = Path.Combine(data.Path, "old");
        WriteNotebook(old, version: 3);
        var error = await Assert.ThrowsAsync<KeepNoteImportException>(() => importer.ScanAsync(old));
        Assert.Contains("version 3", error.Message);

        var broken = Path.Combine(data.Path, "broken");
        WriteNotebook(broken, version: 6);
        File.WriteAllText(Path.Combine(broken, "Work", "Plan", "node.xml"), "<node><version>6</version><dict><key>nodeid</key>");
        File.Delete(Path.Combine(broken, "Loose", "page.html"));
        var scan = await importer.ScanAsync(broken);
        Assert.Contains(scan.Problems, p => p.Contains("Plan") && p.Contains("Skipped"));
        Assert.Contains(scan.Problems, p => p.Contains("Loose") && p.Contains("page.html"));

        var report = await importer.ImportAsync(broken);
        Assert.Equal(2, report.Notes);
        Assert.Contains(report.Problems, p => p.Contains("Loose"));
    }

    [Fact]
    public void KeepNote_markup_becomes_paragraphs_and_known_tags_and_loses_no_words()
    {
        var page = KeepNotePageConverter.Convert(
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"x\"><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head><body>"
            + "line one<br/>line <b>two</b> <i>it</i> <u>u</u> <strike>gone</strike> <tt>code</tt><br/><br/>"
            + "<span style=\"font-size: 14pt\">big</span> <font color=\"red\">red</font> <nobr>nowrap</nobr><br/>"
            + "<ul><li>item</li></ul>after list<br/><hr/><img src=\"pic%20one.png\" width=\"10\"/><img src=\"../escape.png\"/><img src=\"http://x/y.png\"/>"
            + "<a href=\"nbk:///abc\">link</a><script>bad()</script></body></html>");

        Assert.Equal("pic one.png", Assert.Single(page.Images).FileName);
        var html = page.Html;
        Assert.True(html.StartsWith("<p>line one</p><p>line <strong>two</strong> <em>it</em> <u>u</u> <s>gone</s> <code>code</code></p><p></p>", StringComparison.Ordinal), html);
        // Unknown wrappers (font, nobr) go, their words stay; styles are left for the sanitiser to strip.
        Assert.Contains("big</span> red nowrap</p>", html);
        Assert.Contains("<ul><li>item</li></ul><p>after list</p><hr>", html);
        Assert.Contains("alt=\"pic one\"", html);
        Assert.DoesNotContain("escape.png", html);
        Assert.DoesNotContain("http://x", html);
        Assert.DoesNotContain("bad()", html);
        Assert.DoesNotContain("<font", html);

        var rewritten = KeepNotePageConverter.RewriteLinks(html, new Dictionary<string, Guid> { ["abc"] = Guid.Empty }, out var resolved, out var unresolved);
        Assert.Equal((1, 0), (resolved, unresolved));
        Assert.Contains("href=\"note:00000000-0000-0000-0000-000000000000\"", rewritten);
        KeepNotePageConverter.RewriteLinks(html, new Dictionary<string, Guid>(), out resolved, out unresolved);
        Assert.Equal((0, 1), (resolved, unresolved));
    }

    /// <summary>
    /// A small notebook: Work/Plan (page, links to its child Details, has an image and a file),
    /// Loose (page at the top), and Old in the trash.
    /// </summary>
    private static Dictionary<string, string> WriteNotebook(string root, int version)
    {
        var ids = new Dictionary<string, string>
        {
            ["root"] = "11111111-1111-1111-1111-111111111111",
            ["work"] = "22222222-2222-2222-2222-222222222222",
            ["plan"] = "33333333-3333-3333-3333-333333333333",
            ["details"] = "44444444-4444-4444-4444-444444444444",
            ["budget"] = "55555555-5555-5555-5555-555555555555",
            ["loose"] = "66666666-6666-6666-6666-666666666666",
            ["trash"] = "77777777-7777-7777-7777-777777777777",
            ["old"] = "88888888-8888-8888-8888-888888888888",
        };

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "notebook.nbk"), $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<notebook>\n<version>{version}</version>\n<pref>\n<dict></dict>\n</pref>\n</notebook>\n");
        WriteNode(root, version, ids["root"], KeepNoteReader.ContentTypeFolder, "My KeepNote", 0);

        var work = Path.Combine(root, "Work");
        WriteNode(work, version, ids["work"], KeepNoteReader.ContentTypeFolder, "Work", 0, ("icon", "folder.png"), ("title_bgcolor", "#ffffff"));

        var plan = Path.Combine(work, "Plan");
        WriteNode(plan, version, ids["plan"], KeepNoteReader.ContentTypePage, "Plan", 0, ("icon", "note.png"));
        File.WriteAllText(Path.Combine(plan, "page.html"),
            "<html><body>First line<br/><b>bold</b> and <tt>mono</tt> and <font color=\"red\">kept text</font><br/>"
            + $"<a href=\"nbk:///{ids["details"]}\">details</a><br/><img src=\"diagram.png\" /></body></html>");
        File.WriteAllBytes(Path.Combine(plan, "diagram.png"), [1, 2, 3]);

        var details = Path.Combine(plan, "Details");
        WriteNode(details, version, ids["details"], KeepNoteReader.ContentTypePage, "Details", 0);
        File.WriteAllText(Path.Combine(details, "page.html"), "<html><body>the details</body></html>");

        var budget = Path.Combine(plan, "budget.xlsx");
        WriteNode(budget, version, ids["budget"], "application/vnd.ms-excel", "budget.xlsx", 1, ("payload_filename", "budget.xlsx"));
        File.WriteAllBytes(Path.Combine(budget, "budget.xlsx"), [9, 9, 9, 9]);

        var loose = Path.Combine(root, "Loose");
        WriteNode(loose, version, ids["loose"], KeepNoteReader.ContentTypePage, "Loose", 1);
        File.WriteAllText(Path.Combine(loose, "page.html"), "<html><body>loose page</body></html>");

        var trash = Path.Combine(root, "__TRASH__");
        WriteNode(trash, version, ids["trash"], KeepNoteReader.ContentTypeTrash, "Trash", 2);
        var old = Path.Combine(trash, "Old");
        WriteNode(old, version, ids["old"], KeepNoteReader.ContentTypePage, "Old", 0);
        File.WriteAllText(Path.Combine(old, "page.html"), "<html><body>forgotten</body></html>");
        return ids;
    }

    private static void WriteNode(string directory, int version, string nodeId, string contentType, string title, int order, params (string Key, string Value)[] extra)
    {
        Directory.CreateDirectory(directory);
        var attributes = new List<(string Key, string Value)>
        {
            ("nodeid", nodeId), ("content_type", contentType), ("title", title), ("order", order.ToString()),
            ("created_time", Created.ToString()), ("modified_time", Modified.ToString()),
        };
        attributes.AddRange(extra);

        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<node>\n");
        xml.Append($"<version>{version}</version>\n");
        if (version >= 6)
        {
            xml.Append("<dict>\n");
            foreach (var (key, value) in attributes)
            {
                var isInteger = key is "order" or "created_time" or "modified_time";
                xml.Append($"  <key>{key}</key>\n  {(isInteger ? $"<integer>{value}</integer>" : $"<string>{System.Security.SecurityElement.Escape(value)}</string>")}\n");
            }

            xml.Append("</dict>\n");
        }
        else
        {
            foreach (var (key, value) in attributes)
            {
                xml.Append($"<attr key=\"{key}\">{System.Security.SecurityElement.Escape(value)}</attr>\n");
            }
        }

        xml.Append("</node>\n");
        File.WriteAllText(Path.Combine(directory, "node.xml"), xml.ToString());
    }

    private static string Snapshot(string directory)
    {
        var parts = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(directory, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        return string.Join("\n", parts);
    }
}
