using Microsoft.EntityFrameworkCore;
using WorriorNotes.Application.Notes;
using WorriorNotes.Domain;
using WorriorNotes.Infrastructure.Persistence;

namespace WorriorNotes.Infrastructure.Notes;

public sealed class NoteService(
    IDbContextFactory<WorriorNotesDbContext> contextFactory,
    TimeProvider timeProvider) : INoteService
{
    public async Task<NoteDetail> CreateAsync(Guid? notebookId = null, string? title = null, string? content = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Guid targetNotebookId;
        if (notebookId is { } requested)
        {
            if (!await context.Notebooks.AnyAsync(n => n.Id == requested, cancellationToken))
            {
                throw new DomainException("The notebook for this note no longer exists.");
            }

            targetNotebookId = requested;
        }
        else
        {
            targetNotebookId = (await NotebookService.GetOrCreateInboxAsync(context, timeProvider, cancellationToken)).Id;
        }

        var node = Node.CreateNote(targetNotebookId, parentId: null, title, content, timeProvider.GetUtcNow());
        context.Nodes.Add(node);
        await context.SaveChangesAsync(cancellationToken);
        return ToDetail(node);
    }

    public async Task<NoteDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes
            .AsNoTracking()
            .Include(n => n.Note)
            .FirstOrDefaultAsync(n => n.Id == id && n.Type == NodeType.Note, cancellationToken);
        return node is null ? null : ToDetail(node);
    }

    public async Task<IReadOnlyList<NoteSummary>> ListAsync(Guid notebookId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Nodes
            .AsNoTracking()
            .Where(n => n.NotebookId == notebookId && n.Type == NodeType.Note && n.DeletedAt == null)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new NoteSummary(n.Id, n.NotebookId, n.Name, n.CreatedAt, n.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<NoteDetail> UpdateAsync(Guid id, string? title, string? content, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes
            .Include(n => n.Note)
            .FirstOrDefaultAsync(n => n.Id == id && n.Type == NodeType.Note, cancellationToken)
            ?? throw new NoteNotFoundException(id);

        node.Edit(title, content, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return ToDetail(node);
    }

    private static NoteDetail ToDetail(Node node) =>
        new(node.Id, node.NotebookId, node.Name, node.Note?.Content ?? string.Empty, node.CreatedAt, node.UpdatedAt);
}
