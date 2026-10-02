namespace WorriorVex.Application.Search;

public sealed record NoteSearchResult(Guid NoteId, Guid NotebookId, string Title, DateTimeOffset UpdatedAt);

public interface INoteSearchService
{
    /// <summary>
    /// Notes outside the trash in which every word of the query occurs, in the title or the body.
    /// Title matches come first, then the most recently updated.
    /// </summary>
    Task<IReadOnlyList<NoteSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default);
}
