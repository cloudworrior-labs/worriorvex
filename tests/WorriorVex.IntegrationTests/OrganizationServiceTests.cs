using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WorriorVex.Application.Common;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Revisions;
using WorriorVex.Application.Storage;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.IntegrationTests;

public class OrganizationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tags_are_shared_between_notes_matched_without_case_and_counted()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var first = await app.Notes.CreateAsync(title: "First");
        var second = await app.Notes.CreateAsync(title: "Second");

        var project = await app.Tags.AddToNoteAsync(first.Id, "#Project");
        await app.Tags.AddToNoteAsync(first.Id, "project");
        await app.Tags.AddToNoteAsync(second.Id, "PROJECT");
        await app.Tags.AddToNoteAsync(second.Id, "idea");

        var all = await app.Tags.ListAsync();
        Assert.Equal([("idea", 1), ("Project", 2)], all.Select(t => (t.Name, t.NoteCount)));
        Assert.Equal(project.Id, Assert.Single(await app.Tags.GetForNoteAsync(first.Id)).Id);

        await app.Trash.MoveToTrashAsync(second.Id);
        Assert.Equal(1, (await app.Tags.ListAsync()).Single(t => t.Id == project.Id).NoteCount);
    }

    [Fact]
    public async Task A_tag_can_be_removed_renamed_and_deleted_without_touching_the_notes()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var note = await app.Notes.CreateAsync(title: "Note");
        var todo = await app.Tags.AddToNoteAsync(note.Id, "todo");
        var idea = await app.Tags.AddToNoteAsync(note.Id, "idea");

        await app.Tags.RemoveFromNoteAsync(note.Id, idea.Id);
        Assert.Equal("todo", Assert.Single(await app.Tags.GetForNoteAsync(note.Id)).Name);

        await Assert.ThrowsAsync<DomainException>(() => app.Tags.RenameAsync(todo.Id, "IDEA"));
        Assert.Equal("task", (await app.Tags.RenameAsync(todo.Id, "task")).Name);

        await app.Tags.DeleteAsync(todo.Id);
        Assert.Empty(await app.Tags.GetForNoteAsync(note.Id));
        Assert.Equal("idea", Assert.Single(await app.Tags.ListAsync()).Name);
        Assert.NotNull(await app.Notes.GetAsync(note.Id));
        await Assert.ThrowsAsync<NoteNotFoundException>(() => app.Tags.AddToNoteAsync(Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task A_link_shows_as_a_backlink_on_the_other_note_and_hides_while_a_note_is_in_the_trash()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var a = await app.Notes.CreateAsync(title: "Note A");
        var b = await app.Notes.CreateAsync(title: "Note B");
        var c = await app.Notes.CreateAsync(title: "Architecture");

        await app.Links.LinkAsync(a.Id, b.Id);
        await app.Links.LinkAsync(a.Id, b.Id);
        await app.Links.LinkAsync(c.Id, b.Id);

        Assert.Equal(b.Id, Assert.Single(await app.Links.GetLinksAsync(a.Id)).NoteId);
        Assert.Equal(["Architecture", "Note A"], (await app.Links.GetBacklinksAsync(b.Id)).Select(l => l.Title));
        Assert.Empty(await app.Links.GetBacklinksAsync(a.Id));

        await app.Trash.MoveToTrashAsync(c.Id);
        Assert.Equal("Note A", Assert.Single(await app.Links.GetBacklinksAsync(b.Id)).Title);
        await app.Trash.RestoreAsync(c.Id);
        Assert.Equal(2, (await app.Links.GetBacklinksAsync(b.Id)).Count);

        await app.Links.UnlinkAsync(a.Id, b.Id);
        Assert.Empty(await app.Links.GetLinksAsync(a.Id));
        await Assert.ThrowsAsync<DomainException>(() => app.Links.LinkAsync(a.Id, a.Id));
        await Assert.ThrowsAsync<NoteNotFoundException>(() => app.Links.LinkAsync(a.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Editing_keeps_the_earlier_version_at_most_once_per_interval()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var note = await app.Notes.CreateAsync(title: "Draft", content: "<p>one</p>");

        clock.Advance(TimeSpan.FromMinutes(1));
        await app.Notes.UpdateAsync(note.Id, "Draft", "<p>two</p>");
        Assert.Empty(await app.Revisions.ListAsync(note.Id));

        clock.Advance(RevisionPolicy.SnapshotInterval);
        await app.Notes.UpdateAsync(note.Id, "Draft", "<p>three</p>");
        clock.Advance(TimeSpan.FromMinutes(1));
        await app.Notes.UpdateAsync(note.Id, "Draft", "<p>four</p>");
        await app.Notes.UpdateAsync(note.Id, "Draft", "<p>four</p>");

        var revision = Assert.Single(await app.Revisions.ListAsync(note.Id));
        Assert.Equal(RevisionPolicy.EditReason, revision.ChangeReason);
        Assert.Equal("<p>two</p>", (await app.Revisions.GetAsync(revision.Id))!.Content);
    }

    [Fact]
    public async Task Restoring_a_revision_brings_it_back_and_keeps_what_it_replaced()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(Start);
        await using var app = await TestApp.StartAsync(data, clock);
        var note = await app.Notes.CreateAsync(title: "Plan", content: "<p>original</p>");
        clock.Advance(RevisionPolicy.SnapshotInterval);
        await app.Notes.UpdateAsync(note.Id, "Plan, rewritten", "<p>rewritten</p>");
        var original = Assert.Single(await app.Revisions.ListAsync(note.Id));

        clock.Advance(TimeSpan.FromMinutes(1));
        var restored = await app.Revisions.RestoreAsync(original.Id);

        Assert.Equal(("Plan", "<p>original</p>"), (restored.Title, restored.Content));
        Assert.Equal(Start + RevisionPolicy.SnapshotInterval + TimeSpan.FromMinutes(1), restored.UpdatedAt);
        var history = await app.Revisions.ListAsync(note.Id);
        Assert.Equal([RevisionPolicy.RestoreReason, RevisionPolicy.EditReason], history.Select(r => r.ChangeReason));
        Assert.Equal("<p>rewritten</p>", (await app.Revisions.GetAsync(history[0].Id))!.Content);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => app.Revisions.RestoreAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_attachment_is_stored_under_a_generated_name_and_reads_back_byte_for_byte()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var note = await app.Notes.CreateAsync(title: "With file");
        var bytes = Encoding.UTF8.GetBytes("Zażółć gęślą jaźń");

        var added = await app.Attachments.AddAsync(note.Id, "../../outside/notes.txt", "text/plain", new MemoryStream(bytes));

        Assert.Equal(("notes.txt", "text/plain", (long)bytes.Length), (added.FileName, added.ContentType, added.Size));
        var directory = app.Get<IApplicationDataPathProvider>().AttachmentsDirectory;
        var stored = Assert.Single(Directory.GetFiles(directory));
        Assert.Equal(added.Id.ToString("N") + ".txt", Path.GetFileName(stored));
        Assert.Empty(Directory.GetFiles(data.Path, "notes.txt", SearchOption.AllDirectories));

        await using (var stream = await app.Attachments.OpenReadAsync(added.Id))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.Equal("renamed.txt", (await app.Attachments.RenameAsync(added.Id, "renamed.txt")).FileName);
        Assert.Equal("renamed.txt", Assert.Single(await app.Attachments.ListAsync(note.Id)).FileName);

        await app.Attachments.DeleteAsync(added.Id);
        Assert.Empty(await app.Attachments.ListAsync(note.Id));
        Assert.Empty(Directory.GetFiles(directory));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => app.Attachments.OpenReadAsync(added.Id));
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_and_leaves_nothing_behind()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var note = await app.Notes.CreateAsync(title: "With file");

        await Assert.ThrowsAsync<DomainException>(() =>
            app.Attachments.AddAsync(note.Id, "huge.bin", null, new EndlessStream(Attachment.MaxSizeBytes + 1)));

        Assert.Empty(await app.Attachments.ListAsync(note.Id));
        Assert.Empty(Directory.GetFiles(app.Get<IApplicationDataPathProvider>().AttachmentsDirectory));
        await Assert.ThrowsAsync<NoteNotFoundException>(() =>
            app.Attachments.AddAsync(Guid.NewGuid(), "a.txt", null, new MemoryStream([1])));
    }

    [Fact]
    public async Task Deleting_a_note_permanently_removes_its_attachment_files_but_the_trash_alone_does_not()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var folder = await app.Tree.CreateFolderAsync((await app.Notebooks.CreateAsync("Projects")).Id, null, "Folder");
        var note = await app.Notes.CreateAsync(parentId: folder.Id, title: "With file");
        var other = await app.Notes.CreateAsync(title: "Other");
        await app.Attachments.AddAsync(note.Id, "a.png", "image/png", new MemoryStream([1, 2, 3]));
        var keptFile = await app.Attachments.AddAsync(other.Id, "b.png", "image/png", new MemoryStream([4, 5, 6]));
        var directory = app.Get<IApplicationDataPathProvider>().AttachmentsDirectory;

        await app.Trash.MoveToTrashAsync(folder.Id);
        Assert.Equal(2, Directory.GetFiles(directory).Length);

        await app.Trash.EmptyAsync();

        Assert.Equal(keptFile.Id.ToString("N") + ".png", Path.GetFileName(Assert.Single(Directory.GetFiles(directory))));
        Assert.Single(await app.Attachments.ListAsync(other.Id));
    }

    [Fact]
    public async Task A_database_from_the_first_release_is_upgraded_with_its_notes_intact()
    {
        using var data = new TempDataDirectory();
        var options = new DbContextOptionsBuilder<WorriorVexDbContext>()
            .UseSqlite($"Data Source={Path.Combine(data.Path, "worriorvex.db")}")
            .Options;
        var notebookId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        await using (var old = new WorriorVexDbContext(options))
        {
            await old.GetService<IMigrator>().MigrateAsync("InitialCreate");
            const string stamp = "2026-10-01T09:00:00.0000000Z";
            await old.Database.ExecuteSqlAsync($"INSERT INTO Notebooks (Id, Name, Kind, SortOrder, CreatedAt, UpdatedAt) VALUES ({notebookId}, 'Old', 0, 0, {stamp}, {stamp})");
            await old.Database.ExecuteSqlAsync($"INSERT INTO Nodes (Id, NotebookId, ParentId, Type, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ({folderId}, {notebookId}, NULL, 0, 'Folder', 0, {stamp}, {stamp})");
            await old.Database.ExecuteSqlAsync($"INSERT INTO Nodes (Id, NotebookId, ParentId, Type, Name, SortOrder, CreatedAt, UpdatedAt) VALUES ({noteId}, {notebookId}, {folderId}, 1, 'Written before the upgrade', 0, {stamp}, {stamp})");
            await old.Database.ExecuteSqlAsync($"INSERT INTO Notes (NodeId, Content, ContentFormat) VALUES ({noteId}, '<p>still here</p>', 0)");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await using var app = await TestApp.StartAsync(data.Path);

        var note = await app.Notes.GetAsync(noteId);
        Assert.NotNull(note);
        Assert.Equal(("Written before the upgrade", "<p>still here</p>", (Guid?)folderId), (note.Title, note.Content, note.ParentId));
        Assert.Equal("Folder", Assert.Single(await app.Tree.ListFoldersAsync(notebookId)).Name);
        Assert.Equal(["Inbox", "Old"], (await app.Notebooks.ListAsync()).Select(n => n.Name));

        // Notes written before search existed are indexed at the first start after the upgrade.
        Assert.Equal(noteId, Assert.Single(await app.Search.SearchAsync("still")).NoteId);

        await app.Trash.MoveToTrashAsync(folderId);
        await app.Trash.EmptyAsync();
        Assert.Null(await app.Notes.GetAsync(noteId));
    }

    /// <summary>Yields zeros up to a length without holding them in memory.</summary>
    private sealed class EndlessStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
