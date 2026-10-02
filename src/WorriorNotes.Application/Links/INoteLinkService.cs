namespace WorriorNotes.Application.Links;

/// <summary>The note at the other end of a link.</summary>
public sealed record LinkedNote(Guid NoteId, Guid NotebookId, string Title);

public interface INoteLinkService
{
    /// <summary>Records that one note links to another. Doing it twice changes nothing.</summary>
    Task LinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default);

    Task UnlinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default);

    /// <summary>Notes this note links to, leaving out notes in the trash.</summary>
    Task<IReadOnlyList<LinkedNote>> GetLinksAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Notes that link to this note, leaving out notes in the trash.</summary>
    Task<IReadOnlyList<LinkedNote>> GetBacklinksAsync(Guid noteId, CancellationToken cancellationToken = default);
}
