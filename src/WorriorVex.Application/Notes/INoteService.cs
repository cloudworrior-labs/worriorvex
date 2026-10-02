namespace WorriorVex.Application.Notes;

public interface INoteService
{
    /// <summary>
    /// Creates a note. Without a notebook it goes to the Inbox; with a parent it goes into that folder.
    /// </summary>
    Task<NoteDetail> CreateAsync(
        Guid? notebookId = null,
        Guid? parentId = null,
        string? title = null,
        string? content = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the note whether or not it is in the trash; <see cref="NoteDetail.DeletedAt"/> tells which.</summary>
    Task<NoteDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes directly in a folder, or at the top of a notebook when no folder is given.
    /// Notes in the trash are left out. Pinned notes come first, then the most recently updated.
    /// </summary>
    Task<IReadOnlyList<NoteSummary>> ListAsync(Guid notebookId, Guid? parentId = null, CancellationToken cancellationToken = default);

    /// <summary>Every note that is not in the trash, most recently updated first.</summary>
    Task<IReadOnlyList<NoteSummary>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Notes marked as favourites, most recently updated first.</summary>
    Task<IReadOnlyList<NoteSummary>> ListFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>Notes in the order they were last opened, newest first.</summary>
    Task<IReadOnlyList<NoteSummary>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Notes carrying a tag, most recently updated first.</summary>
    Task<IReadOnlyList<NoteSummary>> ListByTagAsync(Guid tagId, CancellationToken cancellationToken = default);

    /// <summary>Remembers that the note was opened now. Does not count as a change to the note.</summary>
    Task RecordOpenedAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);

    Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a copy of a note beside the original, with its tags and its own copies of the images,
    /// titled "Copy of …".
    /// </summary>
    Task<NoteDetail> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Saves a note's title and body. <see cref="NoteDetail.UpdatedAt"/> only moves when something changed.</summary>
    Task<NoteDetail> UpdateAsync(Guid id, string? title, string? content, CancellationToken cancellationToken = default);
}
