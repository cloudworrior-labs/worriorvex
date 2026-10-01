namespace WorriorNotes.Application.Notes;

public interface INoteService
{
    /// <summary>Creates a note. Without a notebook it goes to the Inbox.</summary>
    Task<NoteDetail> CreateAsync(Guid? notebookId = null, string? title = null, string? content = null, CancellationToken cancellationToken = default);

    Task<NoteDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Notes in a notebook that are not in the trash, most recently updated first.</summary>
    Task<IReadOnlyList<NoteSummary>> ListAsync(Guid notebookId, CancellationToken cancellationToken = default);

    /// <summary>Saves a note's title and body. <see cref="NoteDetail.UpdatedAt"/> only moves when something changed.</summary>
    Task<NoteDetail> UpdateAsync(Guid id, string? title, string? content, CancellationToken cancellationToken = default);
}
