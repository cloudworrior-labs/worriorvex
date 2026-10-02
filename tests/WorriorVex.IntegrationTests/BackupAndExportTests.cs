using System.IO.Compression;
using System.Text.Json;
using WorriorVex.Application.Backup;
using WorriorVex.Application.Content;
using WorriorVex.Application.Export;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure.Export;

namespace WorriorVex.IntegrationTests;

public class BackupAndExportTests
{
    [Fact]
    public async Task A_backup_holds_the_database_the_files_and_a_manifest_and_restores_whole()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(data, clock);
        await app.Notebooks.GetInboxAsync();
        var notebook = await app.Notebooks.CreateAsync("Projects");
        var note = await app.Notes.CreateAsync(notebook.Id, title: "Keep me", content: "<p>precious</p>");
        var attachment = await app.Attachments.AddAsync(note.Id, "a.png", "image/png", new MemoryStream([1, 2, 3]));
        await app.Tags.AddToNoteAsync(note.Id, "important");
        var backups = app.Get<IBackupService>();

        var path = await backups.CreateBackupAsync();

        Assert.StartsWith(app.Get<IApplicationDataPathProvider>().BackupsDirectory, path);
        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.Equal(["attachments/" + attachment.StoredFileName, "manifest.json", "worriorvex.db"], zip.Entries.Select(e => e.FullName).Order());
        }

        var info = await backups.InspectAsync(path);
        Assert.True(info.CanRestore, string.Join("; ", info.Problems));
        Assert.Equal((1, 1, 2), (info.Manifest!.Notes, info.Manifest.Attachments, info.Manifest.Notebooks));
        Assert.Equal(clock.GetUtcNow(), info.Manifest.CreatedAt);
        Assert.Single(await backups.ListAsync());

        // Change things, then restore: the change is gone, the old data is back, and the changed state was saved first.
        await app.Notes.UpdateAsync(note.Id, "Changed", "<p>changed</p>");
        var extra = await app.Notes.CreateAsync(title: "Extra");
        await app.Attachments.DeleteAsync(attachment.Id);
        clock.Advance(TimeSpan.FromMinutes(1));

        var safety = await backups.RestoreAsync(path);

        var restored = await app.Notes.GetAsync(note.Id);
        Assert.Equal(("Keep me", "<p>precious</p>"), (restored!.Title, restored.Content));
        Assert.Null(await app.Notes.GetAsync(extra.Id));
        Assert.Equal("a.png", Assert.Single(await app.Attachments.ListAsync(note.Id)).FileName);
        await using (var stream = await app.Attachments.OpenReadAsync(attachment.Id))
        {
            Assert.Equal(3, stream.Length);
        }

        Assert.Equal("important", Assert.Single(await app.Tags.GetForNoteAsync(note.Id)).Name);
        Assert.Single(await app.Search.SearchAsync("precious"));
        Assert.Contains("before-restore", safety);
        var saved = await backups.InspectAsync(safety);
        Assert.Equal(2, saved.Manifest!.Notes);
        Assert.Equal(2, (await backups.ListAsync()).Count);

        // The app keeps working after a restore.
        var afterwards = await app.Notes.CreateAsync(title: "After restore");
        Assert.NotNull(await app.Notes.GetAsync(afterwards.Id));
    }

    [Fact]
    public async Task Files_that_are_not_good_backups_are_refused_with_a_reason()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var backups = app.Get<IBackupService>();

        var missing = await backups.InspectAsync(Path.Combine(data.Path, "nothing.zip"));
        Assert.False(missing.CanRestore);

        var notZip = Path.Combine(data.Path, "text.zip");
        await File.WriteAllTextAsync(notZip, "hello");
        Assert.Contains("zip", (await backups.InspectAsync(notZip)).Problems.Single());

        var noManifest = Path.Combine(data.Path, "nomanifest.zip");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create))
        {
            zip.CreateEntry("something.txt");
        }

        Assert.Contains((await backups.InspectAsync(noManifest)).Problems, p => p.Contains("manifest"));

        var damaged = Path.Combine(data.Path, "damaged.zip");
        using (var zip = ZipFile.Open(damaged, ZipArchiveMode.Create))
        {
            var manifest = zip.CreateEntry("manifest.json");
            await using (var stream = manifest.Open())
            {
                await JsonSerializer.SerializeAsync(stream, new BackupManifest("worriorvex-backup", 1, "x", "y", DateTimeOffset.UtcNow, 1, 1, 0));
            }

            var database = zip.CreateEntry("worriorvex.db");
            await using var db = database.Open();
            await db.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });
        }

        var inspected = await backups.InspectAsync(damaged);
        Assert.False(inspected.CanRestore);
        Assert.Contains(inspected.Problems, p => p.Contains("database", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<BackupException>(() => backups.RestoreAsync(damaged));
        Assert.NotNull(await app.Notebooks.GetInboxAsync());
    }

    [Fact]
    public async Task Notes_export_to_html_markdown_and_json_with_their_pictures_and_links()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var notebook = await app.Notebooks.CreateAsync("Projects");
        var folder = await app.Tree.CreateFolderAsync(notebook.Id, null, "Design/Notes");
        var target = await app.Notes.CreateAsync(parentId: folder.Id, title: "Target: a note?", content: "<p>I am the target.</p>");
        var source = await app.Notes.CreateAsync(notebook.Id, title: "Source");
        var image = await app.Attachments.AddAsync(source.Id, "pic.png", "image/png", new MemoryStream([137, 80, 78, 71]));
        var extraFile = await app.Attachments.AddAsync(source.Id, "budget.xlsx", "application/vnd.ms-excel", new MemoryStream([1]));
        await app.Tags.AddToNoteAsync(source.Id, "project");
        var html = $"<h1>Head</h1><p>See <a href=\"{NoteContentRules.NoteLinkAddress(target.Id)}\">the target</a> and <a href=\"https://example.com/\">web</a>.</p>"
            + $"<img src=\"{NoteContentRules.AttachmentSource(image.StoredFileName)}\" alt=\"pic\">"
            + "<ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"true\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>done</p></div></li></ul>"
            + "<table><tbody><tr><th><p>A</p></th><th><p>B</p></th></tr><tr><td><p>1</p></td><td><p>2</p></td></tr></tbody></table>";
        await app.Notes.UpdateAsync(source.Id, "Source", html);
        var exporter = app.Get<IExportService>();
        var output = Path.Combine(data.Path, "out");

        // Everything, as Markdown.
        var all = await exporter.ExportAllAsync(ExportFormat.Markdown, output);
        Assert.Equal(2, all.Notes);
        Assert.Empty(all.Problems);
        var sourceMd = Path.Combine(output, "Projects", "Source.md");
        var targetMd = Path.Combine(output, "Projects", "Design-Notes", "Target a note.md");
        Assert.True(File.Exists(sourceMd), string.Join("\n", Directory.GetFiles(output, "*", SearchOption.AllDirectories)));
        Assert.True(File.Exists(targetMd));
        var markdown = await File.ReadAllTextAsync(sourceMd);
        Assert.Contains("# Source", markdown);
        Assert.Contains("Tags: project", markdown);
        Assert.Contains("# Head", markdown);
        Assert.Contains("[the target](Design-Notes/Target%20a%20note.md)", markdown);
        Assert.Contains("[web](https://example.com/)", markdown);
        Assert.Contains("![pic](Source_files/pic.png)", markdown);
        Assert.Contains("- [x] done", markdown);
        Assert.Contains("| A | B |", markdown);
        Assert.Contains("| 1 | 2 |", markdown);
        Assert.Contains("[budget.xlsx](Source_files/budget.xlsx)", markdown);
        Assert.True(File.Exists(Path.Combine(output, "Projects", "Source_files", "pic.png")));

        // Everything, as HTML: self-contained files, links between them relative.
        var htmlOut = Path.Combine(data.Path, "html");
        await exporter.ExportAllAsync(ExportFormat.Html, htmlOut);
        var page = await File.ReadAllTextAsync(Path.Combine(htmlOut, "Projects", "Source.html"));
        Assert.Contains("<title>Source</title>", page);
        Assert.Contains("data:image/png;base64,", page);
        Assert.Contains("href=\"Design-Notes/Target%20a%20note.html\"", page);
        Assert.DoesNotContain("attachments/", page);
        Assert.DoesNotContain("note:", page);

        // One note, as JSON with its files inside.
        var jsonPath = Path.Combine(data.Path, "source.json");
        await exporter.ExportNoteAsync(source.Id, ExportFormat.Json, jsonPath);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = document.RootElement;
        Assert.Equal("Source", root.GetProperty("title").GetString());
        Assert.Equal((await app.Notes.GetAsync(source.Id))!.Content, root.GetProperty("content").GetString());
        Assert.Equal("project", root.GetProperty("tags")[0].GetString());
        Assert.Equal(target.Id, root.GetProperty("linksTo")[0].GetGuid());
        var files = root.GetProperty("attachments");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal(Convert.ToBase64String(new byte[] { 137, 80, 78, 71 }), files[0].GetProperty("contentBase64").GetString());

        // One note, HTML, link to a note that is not exported stays as words.
        var single = Path.Combine(data.Path, "single.html");
        await exporter.ExportNoteAsync(source.Id, ExportFormat.Html, single);
        var singleHtml = await File.ReadAllTextAsync(single);
        Assert.Contains("<a>the target</a>", singleHtml);
        _ = extraFile;
    }

    [Fact]
    public async Task A_package_holds_everything_needed_to_rebuild_the_workspace()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var notebook = await app.Notebooks.CreateAsync("Projects");
        var note = await app.Notes.CreateAsync(notebook.Id, title: "Packed", content: "<p>content</p>");
        var attachment = await app.Attachments.AddAsync(note.Id, "a.bin", null, new MemoryStream([7, 7]));
        await app.Tags.AddToNoteAsync(note.Id, "t");
        var trashed = await app.Notes.CreateAsync(title: "Trashed");
        await app.Trash.MoveToTrashAsync(trashed.Id);
        var packagePath = Path.Combine(data.Path, "all.worriorvex");

        var result = await app.Get<IExportService>().ExportPackageAsync(packagePath);

        Assert.Equal((2, 1), (result.Notes, result.Files));
        using var zip = ZipFile.OpenRead(packagePath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("manifest.json", names);
        Assert.Contains("notebooks.json", names);
        Assert.Contains("nodes.json", names);
        Assert.Contains("tags.json", names);
        Assert.Contains($"notes/{note.Id:D}.json", names);
        Assert.Contains($"notes/{trashed.Id:D}.json", names);
        Assert.Contains("attachments/" + attachment.StoredFileName, names);

        using var manifest = JsonDocument.Parse(await new StreamReader(zip.GetEntry("manifest.json")!.Open()).ReadToEndAsync());
        Assert.Equal(ExportService.PackageFormat, manifest.RootElement.GetProperty("format").GetString());
        using var packed = JsonDocument.Parse(await new StreamReader(zip.GetEntry($"notes/{note.Id:D}.json")!.Open()).ReadToEndAsync());
        Assert.Equal("<p>content</p>", packed.RootElement.GetProperty("content").GetString());
        Assert.False(packed.RootElement.GetProperty("attachments")[0].TryGetProperty("contentBase64", out _));
    }

    [Theory]
    [InlineData("<p>Hello <strong>bold</strong> and <em>it</em> and <s>gone</s> and <code>x</code></p>", "Hello **bold** and *it* and ~~gone~~ and `x`")]
    [InlineData("<h2>Title</h2><p>a</p><p>b</p>", "## Title\n\na\n\nb")]
    [InlineData("<ul><li><p>one</p></li><li><p>two</p><ul><li><p>deep</p></li></ul></li></ul>", "- one\n- two\n  - deep")]
    [InlineData("<ol start=\"3\"><li><p>c</p></li></ol>", "3. c")]
    [InlineData("<blockquote><p>q</p></blockquote>", "> q")]
    [InlineData("<pre><code class=\"language-csharp\">var x = 1;</code></pre>", "```csharp\nvar x = 1;\n```")]
    [InlineData("<p>a<br>b</p>", "a  \nb")]
    [InlineData("<hr>", "---")]
    [InlineData("<p>special * _ # chars</p>", "special \\* \\_ \\# chars")]
    public void Html_becomes_readable_markdown(string html, string expected)
    {
        var markdown = HtmlToMarkdown.Convert(html, href => href, src => src);

        Assert.Equal(expected + "\n", markdown);
    }
}
