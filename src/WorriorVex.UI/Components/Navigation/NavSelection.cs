namespace WorriorVex.UI.Components.Navigation;

/// <summary>What the navigation pane has selected, and so what the list beside it shows.</summary>
public abstract record NavSelection
{
    private NavSelection() { }

    public sealed record AllNotes : NavSelection;

    /// <summary>The top of a notebook. The Inbox is a notebook too.</summary>
    public sealed record Notebook(Guid NotebookId) : NavSelection;

    public sealed record Folder(Guid NotebookId, Guid FolderId) : NavSelection;

    public sealed record Trash : NavSelection;

    /// <summary>The list shows what the search box found; nothing in the navigation is selected.</summary>
    public sealed record Search : NavSelection;

    public sealed record Documentation : NavSelection;

    public sealed record About : NavSelection;
}
