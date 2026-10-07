using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Common;
using WorriorVex.Application.Tree;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Notes;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Tree;

public sealed class TreeService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : ITreeService
{
    public async Task<IReadOnlyList<FolderSummary>> ListFoldersAsync(Guid? notebookId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Nodes
            .AsNoTracking()
            .Where(n => n.Type == NodeType.Folder && n.DeletedAt == null && (notebookId == null || n.NotebookId == notebookId))
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name)
            .Select(n => new FolderSummary(n.Id, n.NotebookId, n.ParentId, n.Name, n.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<FolderSummary> CreateFolderAsync(Guid notebookId, Guid? parentId, string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureDestinationAsync(context, notebookId, parentId, cancellationToken);

        var sortOrder = await NoteService.NextSortOrderAsync(context, notebookId, parentId, cancellationToken);
        var folder = Node.CreateFolder(notebookId, parentId, name, timeProvider.GetUtcNow(), sortOrder);
        context.Nodes.Add(folder);
        await context.SaveChangesAsync(cancellationToken);
        return ToSummary(folder);
    }

    public async Task<FolderSummary> RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var folder = await context.Nodes
            .FirstOrDefaultAsync(n => n.Id == folderId && n.Type == NodeType.Folder && n.DeletedAt == null, cancellationToken)
            ?? throw new EntityNotFoundException("Folder", folderId);

        folder.Rename(name, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return ToSummary(folder);
    }

    public async Task MoveAsync(Guid nodeId, Guid targetNotebookId, Guid? targetParentId, int? index = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            ?? throw new EntityNotFoundException("Item", nodeId);
        if (node.IsDeleted)
        {
            throw new DomainException("An item in the trash cannot be moved. Restore it first.");
        }

        await EnsureDestinationAsync(context, targetNotebookId, targetParentId, cancellationToken);

        var subtree = node.Type == NodeType.Folder
            ? await TreeQueries.GetSubtreeIdsAsync(context, node.NotebookId, node.Id, cancellationToken)
            : [node.Id];
        if (targetParentId is { } parentId && subtree.Contains(parentId))
        {
            throw new DomainException("A folder cannot be moved into itself or into one of its own folders.");
        }

        if (node.NotebookId != targetNotebookId && subtree.Count > 1)
        {
            // Everything inside travels to the other notebook as well, including what is in the trash.
            var descendants = await context.Nodes
                .Where(n => n.NotebookId == node.NotebookId && n.Id != node.Id)
                .ToListAsync(cancellationToken);
            foreach (var descendant in descendants.Where(d => subtree.Contains(d.Id)))
            {
                descendant.MoveTo(targetNotebookId, descendant.ParentId, descendant.SortOrder);
            }
        }

        if (index is { } position)
        {
            // An explicit position: renumber the siblings of the same kind around it.
            var siblings = await context.Nodes
                .Where(n => n.NotebookId == targetNotebookId
                    && n.ParentId == targetParentId
                    && n.Type == node.Type
                    && n.DeletedAt == null
                    && n.Id != node.Id)
                .OrderBy(n => n.SortOrder)
                .ThenBy(n => n.Name)
                .ToListAsync(cancellationToken);
            siblings.Insert(Math.Clamp(position, 0, siblings.Count), node);
            for (var i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].Id == node.Id)
                {
                    node.MoveTo(targetNotebookId, targetParentId, i);
                }
                else
                {
                    siblings[i].SetSortOrder(i);
                }
            }
        }
        else if (node.NotebookId != targetNotebookId || node.ParentId != targetParentId)
        {
            var sortOrder = await NoteService.NextSortOrderAsync(context, targetNotebookId, targetParentId, cancellationToken);
            node.MoveTo(targetNotebookId, targetParentId, sortOrder);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A destination is a notebook outside the trash and, optionally, a folder outside the trash in it.</summary>
    public async Task<FolderSummary> MoveNotebookIntoAsync(Guid notebookId, Guid targetNotebookId, Guid? targetParentId, CancellationToken cancellationToken = default)
    {
        if (notebookId == targetNotebookId)
        {
            throw new DomainException("A notebook cannot be moved into itself.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebook = await context.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.DeletedAt == null, cancellationToken)
            ?? throw new EntityNotFoundException("Notebook", notebookId);
        if (notebook.Kind != NotebookKind.User)
        {
            throw new DomainException("The Inbox and the Calendar stay where they are.");
        }

        await EnsureDestinationAsync(context, targetNotebookId, targetParentId, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var sortOrder = await NoteService.NextSortOrderAsync(context, targetNotebookId, targetParentId, cancellationToken);
        var folder = Node.CreateFolder(targetNotebookId, targetParentId, notebook.Name, timeProvider.GetUtcNow(), sortOrder);
        context.Nodes.Add(folder);

        // Everything keeps its place relative to the others; what sat at the top of the notebook goes under the new folder.
        var contents = await context.Nodes.Where(n => n.NotebookId == notebookId).ToListAsync(cancellationToken);
        foreach (var node in contents)
        {
            node.MoveTo(targetNotebookId, node.ParentId ?? folder.Id, node.SortOrder);
        }

        await context.SaveChangesAsync(cancellationToken);
        context.Notebooks.Remove(notebook);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(folder);
    }

    private static async Task EnsureDestinationAsync(WorriorVexDbContext context, Guid notebookId, Guid? parentId, CancellationToken cancellationToken)
    {
        if (!await context.Notebooks.AnyAsync(n => n.Id == notebookId && n.DeletedAt == null, cancellationToken))
        {
            throw new DomainException("That notebook no longer exists.");
        }

        if (parentId is { } folderId
            && !await context.Nodes.AnyAsync(
                n => n.Id == folderId && n.NotebookId == notebookId && n.Type == NodeType.Folder && n.DeletedAt == null,
                cancellationToken))
        {
            throw new DomainException("That folder no longer exists in the notebook.");
        }
    }

    private static FolderSummary ToSummary(Node folder) =>
        new(folder.Id, folder.NotebookId, folder.ParentId, folder.Name, folder.SortOrder);
}
