namespace WorriorVex.UI.Components.Notes;

/// <summary>The latest title and body the user typed for a note.</summary>
public readonly record struct NoteEdit(Guid NoteId, string Title, string Content);
