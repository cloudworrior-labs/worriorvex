using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Content;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Links;

/// <summary>Keeps the NoteLinks rows of a note equal to the <c>note:</c> links in its text.</summary>
internal static class NoteLinkSync
{
    /// <summary>Call after the note's content is set, inside the transaction that saves it.</summary>
    public static async Task SyncAsync(WorriorVexDbContext context, Guid sourceNoteId, string html, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var wanted = NoteContentRules.NoteLinkTargets(html).Where(id => id != sourceNoteId).ToHashSet();
        var existing = await context.NoteLinks.Where(l => l.SourceNoteId == sourceNoteId).ToListAsync(cancellationToken);

        context.NoteLinks.RemoveRange(existing.Where(l => !wanted.Contains(l.TargetNoteId)));

        var missing = wanted.Except(existing.Select(l => l.TargetNoteId)).ToList();
        if (missing.Count > 0)
        {
            // Only notes that exist can be linked to; a link to nothing stays text and is not recorded.
            var known = await context.Nodes
                .Where(n => missing.Contains(n.Id) && n.Type == NodeType.Note)
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);
            context.NoteLinks.AddRange(known.Select(target => NoteLink.Create(sourceNoteId, target, now)));
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
