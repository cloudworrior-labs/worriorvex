namespace WorriorVex.UI.Components.Navigation;

/// <summary>What the navigation pane has selected, and so what the list beside it shows.</summary>
public abstract record NavSelection
{
    private NavSelection() { }

    public sealed record AllNotes : NavSelection;

    public sealed record Favorites : NavSelection;

    public sealed record Recent : NavSelection;

    /// <summary>Notes carrying one tag.</summary>
    public sealed record Tag(Guid TagId) : NavSelection;

    /// <summary>The top of a notebook. The Inbox is a notebook too.</summary>
    public sealed record Notebook(Guid NotebookId) : NavSelection;

    public sealed record Folder(Guid NotebookId, Guid FolderId) : NavSelection;

    public sealed record Trash : NavSelection;

    /// <summary>The list shows what the search box found; nothing in the navigation is selected.</summary>
    public sealed record Search : NavSelection;

    /// <summary>Import, backup and export.</summary>
    public sealed record Data : NavSelection;

    public sealed record Settings : NavSelection;

    public sealed record Documentation : NavSelection;

    public sealed record About : NavSelection;
}
