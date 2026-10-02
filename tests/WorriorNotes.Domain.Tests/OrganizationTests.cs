using WorriorNotes.Domain;

namespace WorriorNotes.Domain.Tests;

public class OrganizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid NotebookId = Guid.NewGuid();

    [Fact]
    public void Moving_a_node_changes_its_place_but_not_UpdatedAt()
    {
        var note = Node.CreateNote(NotebookId, null, "Note", null, Now);
        var otherNotebook = Guid.NewGuid();
        var folder = Guid.NewGuid();

        note.MoveTo(otherNotebook, folder, 3);

        Assert.Equal(otherNotebook, note.NotebookId);
        Assert.Equal(folder, note.ParentId);
        Assert.Equal(3, note.SortOrder);
        Assert.Equal(Now, note.UpdatedAt);
    }

    [Fact]
    public void A_folder_cannot_become_its_own_parent()
    {
        var folder = Node.CreateFolder(NotebookId, null, "Folder", Now);

        Assert.Throws<DomainException>(() => folder.MoveTo(NotebookId, folder.Id, 0));
    }

    [Fact]
    public void Favorite_and_pinned_are_independent_and_only_for_notes()
    {
        var note = Node.CreateNote(NotebookId, null, "Note", null, Now);
        var folder = Node.CreateFolder(NotebookId, null, "Folder", Now);

        note.SetFavorite(true);
        Assert.True(note.IsFavorite);
        Assert.False(note.IsPinned);

        note.SetPinned(true);
        note.SetFavorite(false);
        Assert.True(note.IsPinned);
        Assert.False(note.IsFavorite);

        Assert.Throws<DomainException>(() => folder.SetFavorite(true));
        Assert.Throws<DomainException>(() => folder.SetPinned(true));
    }

    [Fact]
    public void A_notebook_can_go_to_the_trash_and_back_but_the_inbox_cannot()
    {
        var notebook = Notebook.Create("Projects", Now);
        var inbox = Notebook.CreateInbox(Now);

        notebook.MoveToTrash(Now.AddMinutes(1));
        Assert.True(notebook.IsDeleted);
        notebook.Restore();
        Assert.False(notebook.IsDeleted);

        Assert.Throws<DomainException>(() => inbox.MoveToTrash(Now));
    }

    [Theory]
    [InlineData("project", "project", "project")]
    [InlineData("  #Project ", "Project", "project")]
    public void A_tag_name_is_cleaned_and_compared_without_case(string input, string name, string normalized)
    {
        var tag = Tag.Create(input, Now);

        Assert.Equal(name, tag.Name);
        Assert.Equal(normalized, tag.NormalizedName);
        Assert.Equal(normalized, Tag.NormalizeName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("two words")]
    [InlineData("a,b")]
    [InlineData("C#")]
    public void An_unusable_tag_name_is_rejected(string name)
    {
        Assert.Throws<DomainException>(() => Tag.Create(name, Now));
    }

    [Fact]
    public void A_tag_name_longer_than_the_limit_is_rejected()
    {
        Assert.Throws<DomainException>(() => Tag.Create(new string('a', Tag.MaxNameLength + 1), Now));
    }

    [Fact]
    public void A_note_cannot_link_to_itself()
    {
        var id = Guid.NewGuid();

        Assert.Throws<DomainException>(() => NoteLink.Create(id, id, Now));
        Assert.Equal(id, NoteLink.Create(id, Guid.NewGuid(), Now).SourceNoteId);
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\me\notes.txt", "notes.txt")]
    [InlineData("..", "file")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("a<b>:c?.txt", "abc.txt")]
    [InlineData("  spaced name.md  ", "spaced name.md")]
    public void An_attachment_file_name_is_reduced_to_a_plain_name(string? input, string expected)
    {
        Assert.Equal(expected, Attachment.CleanFileName(input));
    }

    [Fact]
    public void An_attachment_is_stored_under_a_generated_name_that_keeps_only_a_safe_extension()
    {
        var id = Guid.NewGuid();
        var noteId = Guid.NewGuid();

        var picture = Attachment.Create(id, noteId, "Holiday Photo.JPG", "image/jpeg", 10, "abc", Now);
        var odd = Attachment.Create(id, noteId, "archive.tar.g/z", "not a type", 10, "abc", Now);
        var script = Attachment.Create(id, noteId, "run.sh;rm -rf", null, 10, "abc", Now);

        Assert.Equal(id.ToString("N") + ".jpg", picture.StoredFileName);
        Assert.Equal("Holiday Photo.JPG", picture.OriginalFileName);
        Assert.Equal("image/jpeg", picture.ContentType);

        Assert.Equal(id.ToString("N"), odd.StoredFileName);
        Assert.Equal(Attachment.DefaultContentType, odd.ContentType);

        Assert.Equal(id.ToString("N"), script.StoredFileName);
    }

    [Fact]
    public void An_attachment_over_the_size_limit_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "big.bin", null, Attachment.MaxSizeBytes + 1, "abc", Now));
    }

    [Fact]
    public void A_long_change_reason_is_cut_to_fit()
    {
        var revision = NoteRevision.Create(Guid.NewGuid(), "T", "<p>c</p>", Now, new string('r', NoteRevision.MaxChangeReasonLength + 50));

        Assert.Equal(NoteRevision.MaxChangeReasonLength, revision.ChangeReason.Length);
    }
}
