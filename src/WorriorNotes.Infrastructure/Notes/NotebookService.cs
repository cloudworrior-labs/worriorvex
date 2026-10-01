using Microsoft.EntityFrameworkCore;
using WorriorNotes.Application.Notes;
using WorriorNotes.Domain;
using WorriorNotes.Infrastructure.Persistence;

namespace WorriorNotes.Infrastructure.Notes;

public sealed class NotebookService(
    IDbContextFactory<WorriorNotesDbContext> contextFactory,
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
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name)
            .Select(n => new NotebookSummary(n.Id, n.Name, n.Kind == NotebookKind.Inbox))
            .ToListAsync(cancellationToken);
    }

    internal static async Task<Notebook> GetOrCreateInboxAsync(
        WorriorNotesDbContext context,
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

    private static Task<Notebook?> FindInboxAsync(WorriorNotesDbContext context, CancellationToken cancellationToken) =>
        context.Notebooks.FirstOrDefaultAsync(n => n.Kind == NotebookKind.Inbox, cancellationToken);

    private static NotebookSummary ToSummary(Notebook notebook) =>
        new(notebook.Id, notebook.Name, notebook.Kind == NotebookKind.Inbox);
}
