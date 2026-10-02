using Microsoft.EntityFrameworkCore;

namespace WorriorNotes.Infrastructure.Persistence;

internal static class TreeQueries
{
    /// <summary>
    /// The ids of a node and everything below it, in the trash or not. Reads only ids,
    /// so it stays cheap for large notebooks.
    /// </summary>
    public static async Task<HashSet<Guid>> GetSubtreeIdsAsync(
        WorriorNotesDbContext context,
        Guid notebookId,
        Guid rootId,
        CancellationToken cancellationToken)
    {
        var pairs = await context.Nodes
            .AsNoTracking()
            .Where(n => n.NotebookId == notebookId && n.ParentId != null)
            .Select(n => new { n.Id, ParentId = n.ParentId!.Value })
            .ToListAsync(cancellationToken);
        var childrenByParent = pairs.ToLookup(p => p.ParentId, p => p.Id);

        var subtree = new HashSet<Guid> { rootId };
        var pending = new Queue<Guid>();
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            foreach (var child in childrenByParent[pending.Dequeue()])
            {
                if (subtree.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return subtree;
    }
}
