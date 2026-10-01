using WorriorNotes.Domain;

namespace WorriorNotes.Domain.Tests;

public class NodeTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);
    private static readonly Guid NotebookId = Guid.NewGuid();

    [Fact]
    public void CreateNote_sets_identity_timestamps_and_html_content()
    {
        var node = Node.CreateNote(NotebookId, null, "  Plan  ", "<p>Hello</p>", Created);

        Assert.NotEqual(Guid.Empty, node.Id);
        Assert.Equal(NodeType.Note, node.Type);
        Assert.Equal("Plan", node.Name);
        Assert.Equal(Created, node.CreatedAt);
        Assert.Equal(Created, node.UpdatedAt);
        Assert.NotNull(node.Note);
        Assert.Equal(node.Id, node.Note.NodeId);
        Assert.Equal("<p>Hello</p>", node.Note.Content);
        Assert.Equal(ContentFormat.Html, node.Note.ContentFormat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateNote_without_a_title_is_untitled(string? title)
    {
        var node = Node.CreateNote(NotebookId, null, title, null, Created);

        Assert.Equal(Node.DefaultNoteName, node.Name);
        Assert.Equal(string.Empty, node.Note!.Content);
    }

    [Fact]
    public void CreateNote_requires_a_notebook()
    {
        Assert.Throws<DomainException>(() => Node.CreateNote(Guid.Empty, null, "x", null, Created));
    }

    [Fact]
    public void Edit_changes_title_and_content_and_moves_UpdatedAt()
    {
        var node = Node.CreateNote(NotebookId, null, "Old", "<p>old</p>", Created);

        node.Edit("New", "<p>new</p>", Later);

        Assert.Equal("New", node.Name);
        Assert.Equal("<p>new</p>", node.Note!.Content);
        Assert.Equal(Later, node.UpdatedAt);
        Assert.Equal(Created, node.CreatedAt);
    }

    [Fact]
    public void Edit_with_identical_values_does_not_move_UpdatedAt()
    {
        var node = Node.CreateNote(NotebookId, null, "Same", "<p>same</p>", Created);

        node.Edit("Same", "<p>same</p>", Later);

        Assert.Equal(Created, node.UpdatedAt);
    }

    [Fact]
    public void Edit_is_rejected_for_folders_and_trashed_notes()
    {
        var folder = Node.CreateFolder(NotebookId, null, "Folder", Created);
        var trashed = Node.CreateNote(NotebookId, null, "Note", null, Created);
        trashed.MoveToTrash(Later);

        Assert.Throws<DomainException>(() => folder.Edit("x", "y", Later));
        Assert.Throws<DomainException>(() => trashed.Edit("x", "y", Later));
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected()
    {
        var tooLong = new string('a', Node.MaxNameLength + 1);

        Assert.Throws<DomainException>(() => Node.CreateNote(NotebookId, null, tooLong, null, Created));
    }

    [Fact]
    public void CreateFolder_requires_a_name()
    {
        Assert.Throws<DomainException>(() => Node.CreateFolder(NotebookId, null, "  ", Created));
    }

    [Fact]
    public void Trash_is_a_soft_delete_that_can_be_restored()
    {
        var node = Node.CreateNote(NotebookId, null, "Note", "<p>keep me</p>", Created);

        node.MoveToTrash(Later);
        Assert.True(node.IsDeleted);
        Assert.Equal(Later, node.DeletedAt);

        node.MoveToTrash(Later.AddHours(1));
        Assert.Equal(Later, node.DeletedAt);

        node.Restore();
        Assert.False(node.IsDeleted);
        Assert.Equal("<p>keep me</p>", node.Note!.Content);
    }
}
