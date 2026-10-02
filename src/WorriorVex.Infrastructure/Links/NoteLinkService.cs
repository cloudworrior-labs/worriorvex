using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Links;
using WorriorVex.Application.Notes;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Links;

public sealed class NoteLinkService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : INoteLinkService
{
    public async Task LinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default)
    {
        var link = NoteLink.Create(sourceNoteId, targetNoteId, timeProvider.GetUtcNow());

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var noteId in new[] { sourceNoteId, targetNoteId })
        {
            if (!await context.Nodes.AnyAsync(n => n.Id == noteId && n.Type == NodeType.Note, cancellationToken))
            {
                throw new NoteNotFoundException(noteId);
            }
        }

        if (await context.NoteLinks.AnyAsync(l => l.SourceNoteId == sourceNoteId && l.TargetNoteId == targetNoteId, cancellationToken))
        {
            return;
        }

        context.NoteLinks.Add(link);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (context.NoteLinks.AsNoTracking().Any(l => l.SourceNoteId == sourceNoteId && l.TargetNoteId == targetNoteId))
        {
            // Created by someone else between the check and the save; the link exists, which is all that was asked.
        }
    }

    public async Task UnlinkAsync(Guid sourceNoteId, Guid targetNoteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.NoteLinks
            .Where(l => l.SourceNoteId == sourceNoteId && l.TargetNoteId == targetNoteId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LinkedNote>> GetLinksAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NoteLinks
            .AsNoTracking()
            .Where(l => l.SourceNoteId == noteId)
            .Join(context.Nodes.Where(n => n.DeletedAt == null), l => l.TargetNoteId, n => n.Id, (l, n) => n)
            .OrderBy(n => n.Name)
            .Select(n => new LinkedNote(n.Id, n.NotebookId, n.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LinkedNote>> GetBacklinksAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NoteLinks
            .AsNoTracking()
            .Where(l => l.TargetNoteId == noteId)
            .Join(context.Nodes.Where(n => n.DeletedAt == null), l => l.SourceNoteId, n => n.Id, (l, n) => n)
            .OrderBy(n => n.Name)
            .Select(n => new LinkedNote(n.Id, n.NotebookId, n.Name))
            .ToListAsync(cancellationToken);
    }
}
