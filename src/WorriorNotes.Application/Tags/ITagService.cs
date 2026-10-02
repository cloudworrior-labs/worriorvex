namespace WorriorNotes.Application.Tags;

/// <param name="NoteCount">Notes outside the trash that carry the tag.</param>
public sealed record TagSummary(Guid Id, string Name, int NoteCount);

public interface ITagService
{
    Task<IReadOnlyList<TagSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagSummary>> GetForNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Puts a tag on a note, creating the tag if it is new. Doing it twice changes nothing.</summary>
    Task<TagSummary> AddToNoteAsync(Guid noteId, string name, CancellationToken cancellationToken = default);

    Task RemoveFromNoteAsync(Guid noteId, Guid tagId, CancellationToken cancellationToken = default);

    Task<TagSummary> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes the tag from every note and deletes it. The notes stay.</summary>
    Task DeleteAsync(Guid tagId, CancellationToken cancellationToken = default);
}
