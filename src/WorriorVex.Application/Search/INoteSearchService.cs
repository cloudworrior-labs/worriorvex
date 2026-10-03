namespace WorriorVex.Application.Search;

/// <summary>A piece of text, marked when it is one of the words that were searched for.</summary>
public sealed record TextSegment(string Text, bool IsMatch);

/// <param name="Location">Where the note is: the notebook, then the folders down to the note.</param>
/// <param name="TitleSegments">The title, cut up so the matching words can be highlighted.</param>
/// <param name="Snippet">A short piece of the body around the first match, or its beginning.</param>
public sealed record NoteSearchResult(
    Guid NoteId,
    Guid NotebookId,
    string Title,
    DateTimeOffset UpdatedAt,
    string Location,
    IReadOnlyList<TextSegment> TitleSegments,
    IReadOnlyList<TextSegment> Snippet);

/// <summary>Limits a search to one notebook, or to one folder and everything under it.</summary>
public sealed record SearchScope(Guid NotebookId, Guid? FolderId = null);

public interface INoteSearchService
{
    /// <summary>
    /// Notes outside the trash that contain every word of the query in their title, text or tags.
    /// A word also matches longer words that start with it, and accents are ignored.
    /// The best matches come first; a match in the title counts most.
    /// </summary>
    /// <remarks>
    /// The query may contain <c>"an exact phrase"</c> and the filters <c>tag:name</c>,
    /// <c>in:notebook-or-folder</c>, <c>is:favorite</c> and <c>is:pinned</c>.
    /// </remarks>
    Task<IReadOnlyList<NoteSearchResult>> SearchAsync(string query, SearchScope? scope = null, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// A corrected query when a word of the query occurs nowhere but a word in the notes is close to
    /// it (one or two letters off), otherwise <c>null</c>. Filters and phrases are left as they are.
    /// </summary>
    Task<string?> SuggestAsync(string query, CancellationToken cancellationToken = default);
}
