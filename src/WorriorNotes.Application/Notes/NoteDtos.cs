namespace WorriorNotes.Application.Notes;

/// <summary>A row in a note list: enough to show and select a note, without its body.</summary>
public sealed record NoteSummary(Guid Id, Guid NotebookId, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A note with its body, as opened in the editor.</summary>
public sealed record NoteDetail(Guid Id, Guid NotebookId, string Title, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record NotebookSummary(Guid Id, string Name, bool IsInbox);
