using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Common;
using WorriorVex.Application.Notes;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Notes;

public sealed class NotebookService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    TimeProvider timeProvider) : INotebookService
{
    public async Task<NotebookSummary> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var inbox = await GetOrCreateInboxAsync(context, timeProvider, cancellationToken);
        return ToSummary(inbox);
    }

    public async Task<IReadOnlyList<NotebookSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await GetOrCreateInboxAsync(context, timeProvider, cancellationToken);

        return await context.Notebooks
            .AsNoTracking()
            .Where(n => n.DeletedAt == null)
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name)
            .Select(n => new NotebookSummary(n.Id, n.Name, n.Kind == NotebookKind.Inbox, n.Kind == NotebookKind.Calendar))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotebookSummary> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var highest = await context.Notebooks
            .Where(n => n.Kind == NotebookKind.User)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync(cancellationToken);

        var notebook = Notebook.Create(name, timeProvider.GetUtcNow(), highest is { } value ? value + 1 : 0);
        context.Notebooks.Add(notebook);
        await context.SaveChangesAsync(cancellationToken);
        return ToSummary(notebook);
    }

    public async Task<NotebookSummary> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebook = await FindLiveAsync(context, id, cancellationToken);
        notebook.Rename(name, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return ToSummary(notebook);
    }

    public async Task ReorderAsync(Guid id, int index, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebooks = await context.Notebooks
            .Where(n => n.Kind == NotebookKind.User && n.DeletedAt == null)
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name)
            .ToListAsync(cancellationToken);

        var moving = notebooks.FirstOrDefault(n => n.Id == id)
            ?? throw new EntityNotFoundException("Notebook", id);
        notebooks.Remove(moving);
        notebooks.Insert(Math.Clamp(index, 0, notebooks.Count), moving);
        for (var position = 0; position < notebooks.Count; position++)
        {
            notebooks[position].SetSortOrder(position);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<Notebook> GetOrCreateInboxAsync(
        WorriorVexDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var inbox = await FindInboxAsync(context, cancellationToken);
        if (inbox is not null)
        {
            return inbox;
        }

        inbox = Notebook.CreateInbox(timeProvider.GetUtcNow());
        context.Notebooks.Add(inbox);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return inbox;
        }
        catch (DbUpdateException)
        {
            // Another caller created the Inbox first (the unique index rejected ours). Use theirs.
            context.Entry(inbox).State = EntityState.Detached;
            var existing = await FindInboxAsync(context, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    private static Task<Notebook?> FindInboxAsync(WorriorVexDbContext context, CancellationToken cancellationToken) =>
        context.Notebooks.FirstOrDefaultAsync(n => n.Kind == NotebookKind.Inbox, cancellationToken);

    private static async Task<Notebook> FindLiveAsync(WorriorVexDbContext context, Guid id, CancellationToken cancellationToken) =>
        await context.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null, cancellationToken)
        ?? throw new EntityNotFoundException("Notebook", id);

    private static NotebookSummary ToSummary(Notebook notebook) =>
        new(notebook.Id, notebook.Name, notebook.Kind == NotebookKind.Inbox);
}
