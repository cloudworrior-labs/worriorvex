using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Revisions;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Notes;

public sealed class NoteService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : INoteService
{
    public async Task<NoteDetail> CreateAsync(
        Guid? notebookId = null,
        Guid? parentId = null,
        string? title = null,
        string? content = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Guid targetNotebookId;
        if (parentId is { } folderId)
        {
            var folder = await context.Nodes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == folderId && n.Type == NodeType.Folder && n.DeletedAt == null, cancellationToken)
                ?? throw new DomainException("The folder for this note no longer exists.");
            if (notebookId is { } stated && stated != folder.NotebookId)
            {
                throw new DomainException("The folder is not in that notebook.");
            }

            targetNotebookId = folder.NotebookId;
        }
        else if (notebookId is { } requested)
        {
            if (!await context.Notebooks.AnyAsync(n => n.Id == requested && n.DeletedAt == null, cancellationToken))
            {
                throw new DomainException("The notebook for this note no longer exists.");
            }

            targetNotebookId = requested;
        }
        else
        {
            targetNotebookId = (await NotebookService.GetOrCreateInboxAsync(context, timeProvider, cancellationToken)).Id;
        }

        var sortOrder = await NextSortOrderAsync(context, targetNotebookId, parentId, cancellationToken);
        var node = Node.CreateNote(targetNotebookId, parentId, title, content, timeProvider.GetUtcNow(), sortOrder);
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

    public async Task<IReadOnlyList<NoteSummary>> ListAsync(Guid notebookId, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Nodes
            .AsNoTracking()
            .Where(n => n.NotebookId == notebookId && n.ParentId == parentId && n.Type == NodeType.Note && n.DeletedAt == null)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAt)
            .Select(n => new NoteSummary(n.Id, n.NotebookId, n.Name, n.CreatedAt, n.UpdatedAt, n.ParentId, n.IsFavorite, n.IsPinned))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NoteSummary>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Nodes
            .AsNoTracking()
            .Where(n => n.Type == NodeType.Note && n.DeletedAt == null)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new NoteSummary(n.Id, n.NotebookId, n.Name, n.CreatedAt, n.UpdatedAt, n.ParentId, n.IsFavorite, n.IsPinned))
            .ToListAsync(cancellationToken);
    }

    public async Task<NoteDetail> UpdateAsync(Guid id, string? title, string? content, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes
            .Include(n => n.Note)
            .FirstOrDefaultAsync(n => n.Id == id && n.Type == NodeType.Note, cancellationToken)
            ?? throw new NoteNotFoundException(id);

        var previousTitle = node.Name;
        var previousContent = node.Note!.Content;
        var now = timeProvider.GetUtcNow();
        node.Edit(title, content, now);

        var changed = node.Name != previousTitle || node.Note.Content != previousContent;
        if (changed && previousContent.Length > 0 && await RevisionIsDueAsync(context, node, now, cancellationToken))
        {
            context.NoteRevisions.Add(NoteRevision.Create(id, previousTitle, previousContent, now, RevisionPolicy.EditReason));
        }

        await context.SaveChangesAsync(cancellationToken);
        return ToDetail(node);
    }

    /// <summary>The state before an edit is kept when nothing has been kept for the snapshot interval.</summary>
    private static async Task<bool> RevisionIsDueAsync(WorriorVexDbContext context, Node node, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.NoteId == node.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.CreatedAt)
            .Take(1)
            .ToListAsync(cancellationToken);
        var lastKept = latest.Count > 0 ? latest[0] : node.CreatedAt;
        return now - lastKept >= RevisionPolicy.SnapshotInterval;
    }

    internal static async Task<int> NextSortOrderAsync(WorriorVexDbContext context, Guid notebookId, Guid? parentId, CancellationToken cancellationToken)
    {
        var highest = await context.Nodes
            .Where(n => n.NotebookId == notebookId && n.ParentId == parentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync(cancellationToken);
        return highest is { } value ? value + 1 : 0;
    }

    internal static NoteDetail ToDetail(Node node) =>
        new(node.Id, node.NotebookId, node.Name, node.Note?.Content ?? string.Empty, node.CreatedAt, node.UpdatedAt, node.ParentId, node.DeletedAt);
}
