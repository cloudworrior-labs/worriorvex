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
}
