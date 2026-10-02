using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Common;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Revisions;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Notes;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Search;

namespace WorriorVex.Infrastructure.Revisions;

public sealed class RevisionService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : IRevisionService
{
    public async Task<IReadOnlyList<RevisionSummary>> ListAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.NoteId == noteId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RevisionSummary(r.Id, r.NoteId, r.Title, r.CreatedAt, r.ChangeReason))
            .ToListAsync(cancellationToken);
    }

    public async Task<RevisionDetail?> GetAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.Id == revisionId)
            .Select(r => new RevisionDetail(r.Id, r.NoteId, r.Title, r.Content, r.CreatedAt, r.ChangeReason))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NoteDetail> RestoreAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var revision = await context.NoteRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken)
            ?? throw new EntityNotFoundException("Revision", revisionId);
        var node = await context.Nodes
            .Include(n => n.Note)
            .FirstOrDefaultAsync(n => n.Id == revision.NoteId && n.Type == NodeType.Note, cancellationToken)
            ?? throw new NoteNotFoundException(revision.NoteId);

        var currentTitle = node.Name;
        var currentContent = node.Note!.Content;
        var now = timeProvider.GetUtcNow();
        node.Edit(revision.Title, revision.Content, now);

        if (node.Name != currentTitle || node.Note.Content != currentContent)
        {
            // What is being replaced is kept, whatever the snapshot interval says.
            context.NoteRevisions.Add(NoteRevision.Create(node.Id, currentTitle, currentContent, now, RevisionPolicy.RestoreReason));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await SearchIndex.IndexNoteAsync(context, node.Id, node.Name, node.Note.Content, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoteService.ToDetail(node);
    }
}
