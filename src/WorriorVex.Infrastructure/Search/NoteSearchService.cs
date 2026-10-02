using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Search;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Search;

/// <summary>
/// Plain substring search run by SQLite. It matches the stored HTML, so a word that only occurs as
/// markup can match too; the full-text index planned for the search phase replaces this.
/// </summary>
public sealed class NoteSearchService(IDbContextFactory<WorriorVexDbContext> contextFactory) : INoteSearchService
{
    private const int MaxTerms = 8;
    private const int MaxLimit = 200;
    private const string Escape = "\\";

    public async Task<IReadOnlyList<NoteSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var terms = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
        if (terms.Count == 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var matches = context.Nodes
            .AsNoTracking()
            .Where(n => n.Type == NodeType.Note && n.DeletedAt == null)
            .Join(context.Notes, n => n.Id, t => t.NodeId, (n, t) => new { Node = n, t.Content });

        foreach (var term in terms)
        {
            var pattern = "%" + EscapeLike(term) + "%";
            matches = matches.Where(m => EF.Functions.Like(m.Node.Name, pattern, Escape) || EF.Functions.Like(m.Content, pattern, Escape));
        }

        var found = await matches
            .OrderByDescending(m => m.Node.UpdatedAt)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(m => new NoteSearchResult(m.Node.Id, m.Node.NotebookId, m.Node.Name, m.Node.UpdatedAt))
            .ToListAsync(cancellationToken);

        return [.. found
            .OrderByDescending(r => terms.All(t => r.Title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(r => r.UpdatedAt)];
    }

    private static string EscapeLike(string term) =>
        term.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_");
}
