using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Storage;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.IntegrationTests;

public class NoteLifecycleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_start_creates_the_database_from_migrations()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var paths = app.Get<IApplicationDataPathProvider>();
        Assert.Equal(Path.GetFullPath(data.Path), paths.DataDirectory);
        Assert.True(File.Exists(paths.DatabasePath));

        await using var context = await app.Get<IDbContextFactory<WorriorVexDbContext>>().CreateDbContextAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task A_new_note_lands_in_the_inbox()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var note = await app.Notes.CreateAsync(title: "Quick idea");
        var inbox = await app.Notebooks.GetInboxAsync();

        Assert.True(inbox.IsInbox);
        Assert.Equal(inbox.Id, note.NotebookId);
        var listed = Assert.Single(await app.Notes.ListAsync(inbox.Id));
        Assert.Equal("Quick idea", listed.Title);
    }

    [Fact]
    public async Task The_inbox_is_created_only_once()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var first = await app.Notebooks.GetInboxAsync();
        await app.Notes.CreateAsync();
        var second = await app.Notebooks.GetInboxAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await app.Notebooks.ListAsync());
    }

    [Fact]
    public async Task Callers_racing_for_the_inbox_on_first_use_all_get_the_same_one()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var inboxes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => app.Notebooks.GetInboxAsync())));

        Assert.Single(inboxes.Select(i => i.Id).Distinct());
        Assert.Single(await app.Notebooks.ListAsync());
    }

    [Fact]
    public async Task A_note_can_be_created_edited_and_survives_a_restart()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        Guid noteId;

        await using (var firstRun = await TestApp.StartAsync(data.Path, clock))
        {
            var created = await firstRun.Notes.CreateAsync();
            noteId = created.Id;
            Assert.Equal(Node.DefaultNoteName, created.Title);

            clock.Advance(TimeSpan.FromMinutes(2));
            await firstRun.Notes.UpdateAsync(noteId, "Architecture", "<h1>Architecture</h1><p>SQLite, local first. Zażółć gęślą jaźń ✓</p>");
        }

        await using var secondRun = await TestApp.StartAsync(data.Path, clock);
        var reloaded = await secondRun.Notes.GetAsync(noteId);

        Assert.NotNull(reloaded);
        Assert.Equal("Architecture", reloaded.Title);
        Assert.Equal("<h1>Architecture</h1><p>SQLite, local first. Zażółć gęślą jaźń ✓</p>", reloaded.Content);
        Assert.Equal(Start, reloaded.CreatedAt);
        Assert.Equal(Start.AddMinutes(2), reloaded.UpdatedAt);

        var inbox = await secondRun.Notebooks.GetInboxAsync();
        Assert.Equal(noteId, Assert.Single(await secondRun.Notes.ListAsync(inbox.Id)).Id);
    }

    [Fact]
    public async Task Saving_unchanged_content_does_not_move_UpdatedAt()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data.Path, clock);
        var note = await app.Notes.CreateAsync(title: "Stable", content: "<p>same</p>");

        clock.Advance(TimeSpan.FromHours(1));
        var saved = await app.Notes.UpdateAsync(note.Id, "Stable", "<p>same</p>");

        Assert.Equal(Start, saved.UpdatedAt);
    }

    [Fact]
    public async Task Notes_are_listed_most_recently_updated_first()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data.Path, clock);

        var older = await app.Notes.CreateAsync(title: "Older");
        clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await app.Notes.CreateAsync(title: "Newer");
        clock.Advance(TimeSpan.FromMinutes(1));
        await app.Notes.UpdateAsync(older.Id, "Older, edited", "<p>x</p>");

        var listed = await app.Notes.ListAsync(older.NotebookId);

        Assert.Equal([older.Id, newer.Id], listed.Select(n => n.Id));
    }

    [Fact]
    public async Task Updating_a_missing_note_reports_it_instead_of_failing_silently()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        var missing = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<NoteNotFoundException>(() => app.Notes.UpdateAsync(missing, "x", "y"));

        Assert.Equal(missing, error.NoteId);
        Assert.Null(await app.Notes.GetAsync(missing));
    }

    [Fact]
    public async Task Creating_a_note_in_an_unknown_notebook_is_rejected()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);

        await Assert.ThrowsAsync<DomainException>(() => app.Notes.CreateAsync(notebookId: Guid.NewGuid()));
    }
}
