using WorriorVex.Application.Attachments;
using WorriorVex.Application.Content;
using WorriorVex.Application.Export;
using WorriorVex.Application.Import;
using WorriorVex.Application.Links;

namespace WorriorVex.IntegrationTests;

public sealed class ImportTests
{
    [Fact]
    public async Task A_package_from_one_workspace_imports_into_another_with_links_files_and_tags()
    {
        using var source = new TempDataDirectory();
        using var target = new TempDataDirectory();
        string packagePath;
        await using (var app = await TestApp.StartAsync(source.Path))
        {
            var notebook = await app.Notebooks.CreateAsync("Projects");
            var folder = await app.Tree.CreateFolderAsync(notebook.Id, null, "Alpha");
            var linked = await app.Notes.CreateAsync(notebook.Id, folder.Id, "Target", "<p>target</p>");
            var note = await app.Notes.CreateAsync(notebook.Id, null, "Source", $"<p>see <a href=\"{NoteContentRules.NoteLinkAddress(linked.Id)}\">Target</a></p>");
            var attachment = await app.Attachments.AddAsync(note.Id, "pic.png", "image/png", new MemoryStream([1, 2, 3]));
            await app.Notes.UpdateAsync(note.Id, "Source", $"<p>see <a href=\"{NoteContentRules.NoteLinkAddress(linked.Id)}\">Target</a></p><img src=\"attachments/{attachment.StoredFileName}\">");
            await app.Tags.AddToNoteAsync(note.Id, "work");
            var trashed = await app.Notes.CreateAsync(notebook.Id, null, "Gone", "<p>x</p>");
            await app.Trash.MoveToTrashAsync(trashed.Id);
            await app.Notes.CreateAsync(title: "In the inbox", content: "<p>inbox</p>");
            packagePath = Path.Combine(source.Path, "all.worriorvex");
            await app.Get<IExportService>().ExportPackageAsync(packagePath);
        }

        await using (var app = await TestApp.StartAsync(target.Path))
        {
            await app.Notebooks.CreateAsync("Projects"); // a name clash: the imported one is renamed

            var report = await app.Get<IPackageImporter>().ImportAsync(packagePath);

            Assert.Equal(2, report.Notebooks);
            Assert.Equal(1, report.Folders);
            Assert.Equal(3, report.Notes);
            Assert.Equal(1, report.Attachments);
            Assert.Equal(1, report.Tags);
            Assert.Equal(1, report.Links);
            Assert.Empty(report.Problems);

            var names = (await app.Notebooks.ListAsync()).Select(n => n.Name).ToList();
            Assert.Contains("Projects (2)", names);
            Assert.Contains("Imported Inbox", names);

            var all = await app.Notes.ListAllAsync();
            var imported = all.Single(n => n.Title == "Source");
            var importedTarget = all.Single(n => n.Title == "Target");
            Assert.DoesNotContain(all, n => n.Title == "Gone");
            var detail = (await app.Notes.GetAsync(imported.Id))!;
            Assert.Contains(NoteContentRules.NoteLinkAddress(importedTarget.Id), detail.Content);
            var files = await app.Attachments.ListAsync(imported.Id);
            var file = Assert.Single(files);
            Assert.Contains("attachments/" + file.StoredFileName, detail.Content);
            Assert.Equal(3, file.Size);
            Assert.Equal("work", Assert.Single(await app.Tags.GetForNoteAsync(imported.Id)).Name);
            Assert.Single(await app.Links.GetLinksAsync(imported.Id));
        }
    }

    [Fact]
    public async Task A_folder_of_markdown_becomes_a_notebook_with_folders_images_tags_and_links()
    {
        using var data = new TempDataDirectory();
        var vault = Path.Combine(data.Path, "My Vault");
        Directory.CreateDirectory(Path.Combine(vault, "Work", "Deep"));
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        await File.WriteAllTextAsync(Path.Combine(vault, ".obsidian", "app.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(vault, "Work", "diagram.png"), [137, 80, 78, 71]);
        await File.WriteAllTextAsync(Path.Combine(vault, "Home.md"), "---\ntags: [start, home]\n---\n# Welcome\n\nSee [[Work/Plan|the plan]] and [notes](Work/Deep/Notes.md). #inline-tag\n\n- one\n- two\n");
        await File.WriteAllTextAsync(Path.Combine(vault, "Work", "Plan.md"), "## Plan\n\n![[diagram.png]]\n\nBack to [[Home]].\n");
        await File.WriteAllTextAsync(Path.Combine(vault, "Work", "Deep", "Notes.md"), "Plain *text* with `code`.\n");
        await File.WriteAllTextAsync(Path.Combine(vault, "todo.txt"), "first line\nsecond line\n\nsecond paragraph");

        await using var app = await TestApp.StartAsync(data.Path);
        var report = await app.Get<IMarkdownImporter>().ImportAsync(vault);

        Assert.Equal(1, report.Notebooks);
        Assert.Equal(2, report.Folders);
        Assert.Equal(4, report.Notes);
        Assert.Equal(1, report.Attachments);
        Assert.Equal(3, report.Tags);
        Assert.Equal(3, report.Links);
        Assert.Empty(report.Problems);

        var notebook = Assert.Single(await app.Notebooks.ListAsync(), n => n.Name == "My Vault");
        var all = await app.Notes.ListAllAsync();
        var home = (await app.Notes.GetAsync(all.Single(n => n.Title == "Home").Id))!;
        var plan = all.Single(n => n.Title == "Plan");
        var notes = all.Single(n => n.Title == "Notes");
        Assert.Contains("<h1>Welcome</h1>", home.Content);
        Assert.Contains($"href=\"{NoteContentRules.NoteLinkAddress(plan.Id)}\">the plan</a>", home.Content);
        Assert.Contains($"href=\"{NoteContentRules.NoteLinkAddress(notes.Id)}\">notes</a>", home.Content);
        Assert.Equal(["home", "inline-tag", "start"], (await app.Tags.GetForNoteAsync(home.Id)).Select(t => t.Name).OrderBy(t => t));

        var planDetail = (await app.Notes.GetAsync(plan.Id))!;
        var image = Assert.Single(await app.Attachments.ListAsync(plan.Id));
        Assert.Contains($"src=\"attachments/{image.StoredFileName}\"", planDetail.Content);
        Assert.Contains(NoteContentRules.NoteLinkAddress(home.Id), planDetail.Content);
        Assert.Equal(notebook.Id, plan.NotebookId);
        Assert.NotNull(plan.ParentId);
        Assert.NotEqual(plan.ParentId, notes.ParentId);

        var todo = (await app.Notes.GetAsync(all.Single(n => n.Title == "todo").Id))!;
        Assert.Equal("<p>first line<br>second line</p><p>second paragraph</p>", todo.Content);
    }
}
