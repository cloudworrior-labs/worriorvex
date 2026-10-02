namespace WorriorVex.Application.Links;

/// <summary>The note at the other end of a link.</summary>
public sealed record LinkedNote(Guid NoteId, Guid NotebookId, string Title);

/// <summary>
/// Links between notes. The links a note's text contains (<c>note:</c> addresses) are recorded whenever
/// the note is saved, so what is listed here always matches what is written.
/// </summary>
public interface INoteLinkService
{
    /// <summary>Records that one note links to another. Doing it twice changes nothing.</summary>
    Task LinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default);

    Task UnlinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default);

    /// <summary>Notes this note links to, leaving out notes in the trash.</summary>
    Task<IReadOnlyList<LinkedNote>> GetLinksAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Notes that link to this note, leaving out notes in the trash.</summary>
    Task<IReadOnlyList<LinkedNote>> GetBacklinksAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes that share tags with this note and are not already linked from it, those sharing the
    /// most tags first.
    /// </summary>
    Task<IReadOnlyList<LinkedNote>> GetRelatedAsync(Guid noteId, int limit = 8, CancellationToken cancellationToken = default);
}
