using Microsoft.EntityFrameworkCore;
using WorriorVex.Infrastructure.Content;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Search;

/// <summary>
/// Keeps the full-text index (the FTS5 table <c>NoteSearch</c>) in step with the notes. The index holds
/// each note's title, its text without markup, and its tags. It is derived data: it can always be
/// rebuilt from the notes, and is, at startup, whenever it is found incomplete.
/// </summary>
internal static class SearchIndex
{
    /// <summary>Writes one note to the index, replacing what was there. Call inside the transaction that saves the note.</summary>
    public static async Task IndexNoteAsync(WorriorVexDbContext context, Guid noteId, string title, string html, CancellationToken cancellationToken)
    {
        var body = NoteText.FromHtml(html);
        var tags = await TagsOfAsync(context, noteId, cancellationToken);
        await context.Database.ExecuteSqlAsync($"DELETE FROM NoteSearch WHERE NoteId = {noteId}", cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO NoteSearch (NoteId, Title, Body, Tags) VALUES ({noteId}, {title}, {body}, {tags})",
            cancellationToken);
    }

    /// <summary>Brings the tags of the given notes in the index up to date.</summary>
    public static async Task RefreshTagsAsync(WorriorVexDbContext context, IEnumerable<Guid> noteIds, CancellationToken cancellationToken)
    {
        foreach (var noteId in noteIds)
        {
            var tags = await TagsOfAsync(context, noteId, cancellationToken);
            await context.Database.ExecuteSqlAsync($"UPDATE NoteSearch SET Tags = {tags} WHERE NoteId = {noteId}", cancellationToken);
        }
    }

    /// <summary>Drops index entries of notes that no longer exist.</summary>
    public static Task RemoveOrphansAsync(WorriorVexDbContext context, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlAsync($"DELETE FROM NoteSearch WHERE NoteId NOT IN (SELECT NodeId FROM Notes)", cancellationToken);

    /// <summary>
    /// Indexes every note that is missing from the index: all of them after an upgrade from a version
    /// without search, none on a normal start. Returns how many were added.
    /// </summary>
    public static async Task<int> CompleteAsync(WorriorVexDbContext context, CancellationToken cancellationToken)
    {
        await RemoveOrphansAsync(context, cancellationToken);

        var missing = await context.Database
            .SqlQuery<Guid>($"SELECT NodeId AS Value FROM Notes WHERE NodeId NOT IN (SELECT NoteId FROM NoteSearch)")
            .ToListAsync(cancellationToken);
        foreach (var noteId in missing)
        {
            // One at a time, so a large notebook is never held in memory as a whole.
            var note = await context.Nodes
                .AsNoTracking()
                .Where(n => n.Id == noteId)
                .Select(n => new { n.Name, n.Note!.Content })
                .FirstOrDefaultAsync(cancellationToken);
            if (note is not null)
            {
                await IndexNoteAsync(context, noteId, note.Name, note.Content, cancellationToken);
            }
        }

        return missing.Count;
    }

    private static async Task<string> TagsOfAsync(WorriorVexDbContext context, Guid noteId, CancellationToken cancellationToken)
    {
        var names = await context.NoteTags
            .Where(nt => nt.NoteId == noteId)
            .Join(context.Tags, nt => nt.TagId, t => t.Id, (nt, t) => t.Name)
            .ToListAsync(cancellationToken);
        return string.Join(' ', names);
    }
}
