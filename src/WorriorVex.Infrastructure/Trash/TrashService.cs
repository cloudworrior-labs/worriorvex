using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Common;
using WorriorVex.Application.Trash;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Attachments;
using WorriorVex.Infrastructure.Notes;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Search;

namespace WorriorVex.Infrastructure.Trash;

/// <summary>
/// Deleting a folder or notebook stamps everything inside it with the same deletion time. That shared
/// time is what marks them as one item in the trash and brings them back together on restore; things
/// deleted earlier keep their own time and stay separate items.
/// </summary>
public sealed class TrashService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    AttachmentFileStore files,
    TimeProvider timeProvider,
    ILogger<TrashService> logger) : ITrashService
{
    public async Task MoveToTrashAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            ?? throw new EntityNotFoundException("Item", nodeId);
        if (node.IsDeleted)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        node.MoveToTrash(now);
        if (node.Type == NodeType.Folder)
        {
            var subtree = await TreeQueries.GetSubtreeIdsAsync(context, node.NotebookId, node.Id, cancellationToken);
            var inside = await context.Nodes
                .Where(n => n.NotebookId == node.NotebookId && n.DeletedAt == null && n.Id != node.Id)
                .ToListAsync(cancellationToken);
            foreach (var descendant in inside.Where(d => subtree.Contains(d.Id)))
            {
                descendant.MoveToTrash(now);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MoveNotebookToTrashAsync(Guid notebookId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebook = await context.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId, cancellationToken)
            ?? throw new EntityNotFoundException("Notebook", notebookId);
        if (notebook.IsDeleted)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        notebook.MoveToTrash(now);
        var inside = await context.Nodes
            .Where(n => n.NotebookId == notebookId && n.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var node in inside)
        {
            node.MoveToTrash(now);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TrashItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebooks = await context.Notebooks.AsNoTracking().ToDictionaryAsync(n => n.Id, cancellationToken);
        var trashed = (await context.Nodes
                .AsNoTracking()
                .Where(n => n.DeletedAt != null)
                .Select(n => new { n.Id, n.NotebookId, n.ParentId, n.Type, n.Name, n.DeletedAt })
                .ToListAsync(cancellationToken))
            .Select(n => new TrashedNode(n.Id, n.NotebookId, n.ParentId, n.Type, n.Name, n.DeletedAt!.Value))
            .ToList();
        var trashedById = trashed.ToDictionary(n => n.Id);

        var parentIds = trashed.Where(n => n.ParentId is not null).Select(n => n.ParentId!.Value).Distinct().ToList();
        var parentNames = await context.Nodes
            .AsNoTracking()
            .Where(n => parentIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Name })
            .ToDictionaryAsync(n => n.Id, n => n.Name, cancellationToken);

        var items = new List<TrashItem>();
        foreach (var notebook in notebooks.Values.Where(n => n.DeletedAt is not null))
        {
            items.Add(new TrashItem(notebook.Id, TrashItemKind.Notebook, notebook.Name, notebook.DeletedAt!.Value, string.Empty));
        }

        foreach (var node in trashed)
        {
            // Deleted together with what contains it: part of that item, not an item of its own.
            var containerDeletedAt = node.ParentId is { } parentId
                ? trashedById.GetValueOrDefault(parentId)?.DeletedAt
                : notebooks[node.NotebookId].DeletedAt;
            if (containerDeletedAt == node.DeletedAt)
            {
                continue;
            }

            var location = notebooks[node.NotebookId].Name;
            if (node.ParentId is { } parent && parentNames.TryGetValue(parent, out var parentName))
            {
                location += " / " + parentName;
            }

            items.Add(new TrashItem(
                node.Id,
                node.Type == NodeType.Folder ? TrashItemKind.Folder : TrashItemKind.Note,
                node.Name,
                node.DeletedAt,
                location));
        }

        return [.. items.OrderByDescending(i => i.DeletedAt).ThenBy(i => i.Name)];
    }

    public async Task<RestoreResult> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var notebook = await context.Notebooks.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notebook is not null)
        {
            if (notebook.DeletedAt is { } notebookDeletedAt)
            {
                var deletedWithIt = await context.Nodes
                    .Where(n => n.NotebookId == id && n.DeletedAt == notebookDeletedAt)
                    .ToListAsync(cancellationToken);
                deletedWithIt.ForEach(n => n.Restore());
                notebook.Restore();
                await context.SaveChangesAsync(cancellationToken);
            }

            return new RestoreResult(id, TrashItemKind.Notebook, id, null, Relocated: false);
        }

        var node = await context.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Item", id);
        var kind = node.Type == NodeType.Folder ? TrashItemKind.Folder : TrashItemKind.Note;
        if (node.DeletedAt is not { } deletedAt)
        {
            return new RestoreResult(id, kind, node.NotebookId, node.ParentId, Relocated: false);
        }

        var subtree = await TreeQueries.GetSubtreeIdsAsync(context, node.NotebookId, node.Id, cancellationToken);
        var descendants = (await context.Nodes
                .Where(n => n.NotebookId == node.NotebookId && n.Id != node.Id)
                .ToListAsync(cancellationToken))
            .Where(n => subtree.Contains(n.Id))
            .ToList();

        // Where it goes back to: its old place if that still exists, otherwise the nearest place that does.
        var targetNotebookId = node.NotebookId;
        var targetParentId = node.ParentId;
        var owner = await context.Notebooks.AsNoTracking().FirstAsync(n => n.Id == node.NotebookId, cancellationToken);
        if (owner.IsDeleted)
        {
            targetNotebookId = (await NotebookService.GetOrCreateInboxAsync(context, timeProvider, cancellationToken)).Id;
            targetParentId = null;
        }
        else if (targetParentId is { } parentId
            && !await context.Nodes.AnyAsync(n => n.Id == parentId && n.DeletedAt == null, cancellationToken))
        {
            targetParentId = null;
        }

        var relocated = targetNotebookId != node.NotebookId || targetParentId != node.ParentId;
        if (relocated)
        {
            var sortOrder = await NoteService.NextSortOrderAsync(context, targetNotebookId, targetParentId, cancellationToken);
            node.MoveTo(targetNotebookId, targetParentId, sortOrder);
            foreach (var descendant in descendants)
            {
                descendant.MoveTo(targetNotebookId, descendant.ParentId, descendant.SortOrder);
            }
        }

        node.Restore();
        foreach (var descendant in descendants.Where(d => d.DeletedAt == deletedAt))
        {
            descendant.Restore();
        }

        await context.SaveChangesAsync(cancellationToken);
        return new RestoreResult(id, kind, targetNotebookId, targetParentId, relocated);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var notebook = await context.Notebooks.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notebook is not null)
        {
            EnsureInTrash(notebook.IsDeleted);
            var notebookFiles = await context.Attachments
                .Where(a => context.Nodes.Any(n => n.Id == a.NoteId && n.NotebookId == id))
                .Select(a => a.StoredFileName)
                .ToListAsync(cancellationToken);
            await context.Notebooks.Where(n => n.Id == id).ExecuteDeleteAsync(cancellationToken);
            await SearchIndex.RemoveOrphansAsync(context, cancellationToken);
            files.Delete(notebookFiles);
            logger.LogInformation("Notebook {NotebookId} deleted permanently with {FileCount} attachment file(s)", id, notebookFiles.Count);
            return;
        }

        var node = await context.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null)
        {
            return;
        }

        EnsureInTrash(node.IsDeleted);
        var subtree = await TreeQueries.GetSubtreeIdsAsync(context, node.NotebookId, node.Id, cancellationToken);
        var storedFiles = await context.Attachments
            .Where(a => subtree.Contains(a.NoteId))
            .Select(a => a.StoredFileName)
            .ToListAsync(cancellationToken);

        // The database removes everything below the node, and all that hangs on those notes, with it.
        await context.Nodes.Where(n => n.Id == id).ExecuteDeleteAsync(cancellationToken);
        await SearchIndex.RemoveOrphansAsync(context, cancellationToken);
        files.Delete(storedFiles);
        logger.LogInformation("Item {NodeId} deleted permanently with {FileCount} attachment file(s)", id, storedFiles.Count);
    }

    public async Task<int> EmptyAsync(CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken);
        foreach (var item in items)
        {
            await DeletePermanentlyAsync(item.Id, cancellationToken);
        }

        logger.LogInformation("Trash emptied: {Count} item(s)", items.Count);
        return items.Count;
    }

    private static void EnsureInTrash(bool isDeleted)
    {
        if (!isDeleted)
        {
            throw new DomainException("Only something in the trash can be deleted permanently.");
        }
    }

    private sealed record TrashedNode(Guid Id, Guid NotebookId, Guid? ParentId, NodeType Type, string Name, DateTimeOffset DeletedAt);
}
