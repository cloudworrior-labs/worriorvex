using WorriorVex.Domain;

namespace WorriorVex.Domain.Tests;

public class NotebookTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_the_name()
    {
        var notebook = Notebook.Create("  Projects ", Now);

        Assert.Equal("Projects", notebook.Name);
        Assert.Equal(NotebookKind.User, notebook.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_name(string name)
    {
        Assert.Throws<DomainException>(() => Notebook.Create(name, Now));
    }

    [Fact]
    public void The_inbox_sorts_first_and_cannot_be_renamed()
    {
        var inbox = Notebook.CreateInbox(Now);

        Assert.Equal(NotebookKind.Inbox, inbox.Kind);
        Assert.True(inbox.SortOrder < Notebook.Create("A", Now).SortOrder);
        Assert.Throws<DomainException>(() => inbox.Rename("Other", Now));
    }
}
