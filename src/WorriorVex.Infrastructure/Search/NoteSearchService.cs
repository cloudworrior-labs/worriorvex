using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Search;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Search;

/// <summary>
/// Searches the FTS5 index. SQLite does the matching, ranking and cutting of snippets; only the rows
/// that are shown are read into memory.
/// </summary>
public sealed class NoteSearchService(IDbContextFactory<WorriorVexDbContext> contextFactory) : INoteSearchService
{
    private const int MaxLimit = 200;
    private const char MatchStart = '\u0001';
    private const char MatchEnd = '\u0002';

    // bm25 weights per column of NoteSearch: NoteId (not searched), Title, Body, Tags.
    private const string Rank = "bm25(NoteSearch, 0.0, 10.0, 1.0, 5.0)";

    public async Task<IReadOnlyList<NoteSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var parsed = SearchQuery.Parse(query);
        if (parsed.IsEmpty)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        var match = MatchExpression(parsed);
        var sql = new StringBuilder();
        if (match is not null)
        {
            sql.Append(
                $"""
                 SELECT n.Id, n.NotebookId, n.ParentId, n.Name, n.UpdatedAt,
                        highlight(NoteSearch, 1, char(1), char(2)),
                        snippet(NoteSearch, 2, char(1), char(2), '…', 18)
                 FROM NoteSearch
                 JOIN Nodes n ON n.Id = NoteSearch.NoteId
                 """);
            command.Parameters.AddWithValue("$match", match);
        }
        else
        {
            // Filters only: nothing to rank or highlight, so the notes themselves are listed.
            sql.Append(
                """
                SELECT n.Id, n.NotebookId, n.ParentId, n.Name, n.UpdatedAt, n.Name, substr(s.Body, 1, 160)
                FROM Nodes n
                LEFT JOIN NoteSearch s ON s.NoteId = n.Id
                """);
        }

        sql.Append(
            """

            JOIN Notebooks nb ON nb.Id = n.NotebookId
            LEFT JOIN Nodes p ON p.Id = n.ParentId
            WHERE n.DeletedAt IS NULL AND n.Type = $note
            """);
        command.Parameters.AddWithValue("$note", (int)NodeType.Note);

        if (match is not null)
        {
            sql.Append(" AND NoteSearch MATCH $match");
        }

        if (parsed.FavoritesOnly)
        {
            sql.Append(" AND n.IsFavorite = 1");
        }

        if (parsed.PinnedOnly)
        {
            sql.Append(" AND n.IsPinned = 1");
        }

        for (var i = 0; i < parsed.Places.Count; i++)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND (nb.Name = $place{i} COLLATE NOCASE OR p.Name = $place{i} COLLATE NOCASE)");
            command.Parameters.AddWithValue($"$place{i}", parsed.Places[i]);
        }

        sql.Append(match is not null ? $" ORDER BY {Rank}, n.UpdatedAt DESC" : " ORDER BY n.UpdatedAt DESC");
        sql.Append(" LIMIT $limit");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, MaxLimit));
        command.CommandText = sql.ToString();

        var rows = new List<Row>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new Row(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
            }
        }

        var locations = await LocationsAsync(context, rows, cancellationToken);
        return [.. rows.Select(r => new NoteSearchResult(
            r.Id,
            r.NotebookId,
            r.Title,
            r.UpdatedAt,
            locations[r.Id],
            Segments(r.MarkedTitle),
            Segments(r.MarkedSnippet)))];
    }

    /// <summary>
    /// The FTS5 expression: every word must occur, as the start of a word; phrases as they stand; tags in
    /// the tags column. Each part is quoted, so nothing the user types is read as FTS syntax.
    /// </summary>
    private static string? MatchExpression(SearchQuery query)
    {
        var parts = query.Terms.Select(term => Quote(term) + "*")
            .Concat(query.Tags.Select(tag => "Tags : " + Quote(tag)))
            .ToList();
        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    /// <summary>"Notebook / Folder / Subfolder" for each row, reading only the folders on the way up.</summary>
    private static async Task<Dictionary<Guid, string>> LocationsAsync(WorriorVexDbContext context, List<Row> rows, CancellationToken cancellationToken)
    {
        var notebookIds = rows.Select(r => r.NotebookId).Distinct().ToList();
        var notebooks = await context.Notebooks
            .AsNoTracking()
            .Where(n => notebookIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Name, cancellationToken);

        var folders = new Dictionary<Guid, (string Name, Guid? ParentId)>();
        var wanted = rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value).ToHashSet();
        while (wanted.Count > 0)
        {
            var batch = wanted.ToList();
            var found = await context.Nodes
                .AsNoTracking()
                .Where(n => batch.Contains(n.Id))
                .Select(n => new { n.Id, n.Name, n.ParentId })
                .ToListAsync(cancellationToken);
            wanted.Clear();
            foreach (var folder in found)
            {
                folders[folder.Id] = (folder.Name, folder.ParentId);
                if (folder.ParentId is { } parent && !folders.ContainsKey(parent))
                {
                    wanted.Add(parent);
                }
            }
        }

        var locations = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            var path = new List<string>();
            var at = row.ParentId;
            while (at is { } id && folders.TryGetValue(id, out var folder) && path.Count < 64)
            {
                path.Add(folder.Name);
                at = folder.ParentId;
            }

            path.Add(notebooks.GetValueOrDefault(row.NotebookId, string.Empty));
            path.Reverse();
            locations[row.Id] = string.Join(" / ", path);
        }

        return locations;
    }

    /// <summary>Turns text with match markers into segments the UI can highlight without handling markup.</summary>
    private static List<TextSegment> Segments(string marked)
    {
        var segments = new List<TextSegment>();
        var position = 0;
        while (position < marked.Length)
        {
            var start = marked.IndexOf(MatchStart, position);
            if (start < 0)
            {
                break;
            }

            var end = marked.IndexOf(MatchEnd, start + 1);
            if (end < 0)
            {
                break;
            }

            if (start > position)
            {
                segments.Add(new TextSegment(marked[position..start], false));
            }

            segments.Add(new TextSegment(marked[(start + 1)..end], true));
            position = end + 1;
        }

        if (position < marked.Length)
        {
            segments.Add(new TextSegment(marked[position..].Replace(MatchStart.ToString(), string.Empty).Replace(MatchEnd.ToString(), string.Empty), false));
        }

        return segments;
    }

    private sealed record Row(Guid Id, Guid NotebookId, Guid? ParentId, string Title, DateTimeOffset UpdatedAt, string MarkedTitle, string MarkedSnippet);
}
