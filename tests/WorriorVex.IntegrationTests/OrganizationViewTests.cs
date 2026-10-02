using WorriorVex.Application.Content;
using WorriorVex.Application.Storage;
using WorriorVex.Domain;

namespace WorriorVex.IntegrationTests;

public class OrganizationViewTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Favorites_and_pins_are_separate_and_pinned_notes_lead_their_list()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var older = await app.Notes.CreateAsync(title: "Older");
        clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await app.Notes.CreateAsync(title: "Newer");

        await app.Notes.SetFavoriteAsync(older.Id, true);
        await app.Notes.SetPinnedAsync(older.Id, true);

        var favorites = await app.Notes.ListFavoritesAsync();
        Assert.Equal(older.Id, Assert.Single(favorites).Id);
        Assert.Equal([older.Id, newer.Id], (await app.Notes.ListAsync(older.NotebookId)).Select(n => n.Id));
        var detail = await app.Notes.GetAsync(older.Id);
        Assert.True(detail!.IsFavorite);
        Assert.True(detail.IsPinned);
        Assert.Equal(Start, detail.UpdatedAt);

        await app.Notes.SetFavoriteAsync(older.Id, false);
        Assert.Empty(await app.Notes.ListFavoritesAsync());
        Assert.True((await app.Notes.GetAsync(older.Id))!.IsPinned);
        Assert.Single(await app.Search.SearchAsync("is:pinned"));
    }

    [Fact]
    public async Task Recent_lists_notes_by_when_they_were_opened_without_touching_UpdatedAt()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var a = await app.Notes.CreateAsync(title: "A");
        var b = await app.Notes.CreateAsync(title: "B");
        await app.Notes.CreateAsync(title: "Never opened");

        clock.Advance(TimeSpan.FromHours(1));
        await app.Notes.RecordOpenedAsync(a.Id);
        clock.Advance(TimeSpan.FromHours(1));
        await app.Notes.RecordOpenedAsync(b.Id);

        var recent = await app.Notes.ListRecentAsync();
        Assert.Equal([b.Id, a.Id], recent.Select(n => n.Id));
        Assert.Equal(Start, (await app.Notes.GetAsync(a.Id))!.UpdatedAt);
        Assert.Equal(Start.AddHours(1), recent[1].LastOpenedAt);

        await app.Trash.MoveToTrashAsync(b.Id);
        Assert.Equal([a.Id], (await app.Notes.ListRecentAsync()).Select(n => n.Id));
        Assert.Equal([a.Id], (await app.Notes.ListRecentAsync(1)).Select(n => n.Id));
    }

    [Fact]
    public async Task Notes_can_be_listed_by_tag()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var tagged = await app.Notes.CreateAsync(title: "Tagged");
        await app.Notes.CreateAsync(title: "Plain");
        var tag = await app.Tags.AddToNoteAsync(tagged.Id, "idea");

        Assert.Equal(tagged.Id, Assert.Single(await app.Notes.ListByTagAsync(tag.Id)).Id);
        Assert.Empty(await app.Notes.ListByTagAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_duplicate_has_the_same_text_and_tags_and_its_own_image_files()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var notebook = await app.Notebooks.CreateAsync("Projects");
        var folder = await app.Tree.CreateFolderAsync(notebook.Id, null, "Folder");
        var original = await app.Notes.CreateAsync(parentId: folder.Id, title: "Plan");
        var image = await app.Attachments.AddAsync(original.Id, "diagram.png", "image/png", new MemoryStream([1, 2, 3]));
        var html = $"<h1>Plan</h1><img src=\"{NoteContentRules.AttachmentSource(image.StoredFileName)}\" alt=\"diagram\"><p>text</p>";
        await app.Notes.UpdateAsync(original.Id, "Plan", html);
        await app.Tags.AddToNoteAsync(original.Id, "project");
        clock.Advance(TimeSpan.FromMinutes(5));

        var copy = await app.Notes.DuplicateAsync(original.Id);

        Assert.Equal("Copy of Plan", copy.Title);
        Assert.Equal((notebook.Id, (Guid?)folder.Id), (copy.NotebookId, copy.ParentId));
        Assert.Equal(Start.AddMinutes(5), copy.UpdatedAt);
        Assert.Contains("<h1>Plan</h1>", copy.Content);
        Assert.DoesNotContain(image.StoredFileName, copy.Content);

        var copiedAttachment = Assert.Single(await app.Attachments.ListAsync(copy.Id));
        Assert.Contains(NoteContentRules.AttachmentSource(copiedAttachment.StoredFileName), copy.Content);
        Assert.Equal("diagram.png", copiedAttachment.FileName);
        Assert.Equal(2, Directory.GetFiles(app.Get<IApplicationDataPathProvider>().AttachmentsDirectory).Length);
        Assert.Equal("project", Assert.Single(await app.Tags.GetForNoteAsync(copy.Id)).Name);
        Assert.Equal(2, (await app.Search.SearchAsync("tag:project")).Count);
        Assert.Equal(2, (await app.Notes.ListAsync(notebook.Id, folder.Id)).Count);

        await app.Trash.MoveToTrashAsync(original.Id);
        await app.Trash.EmptyAsync();
        await using var stream = await app.Attachments.OpenReadAsync(copiedAttachment.Id);
        Assert.Equal(3, stream.Length);
        await app.Trash.MoveToTrashAsync(copy.Id);
        await Assert.ThrowsAsync<DomainException>(() => app.Notes.DuplicateAsync(copy.Id));
    }
}
