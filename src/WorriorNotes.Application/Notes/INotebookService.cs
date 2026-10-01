namespace WorriorNotes.Application.Notes;

public interface INotebookService
{
    /// <summary>Returns the Inbox, creating it on first use.</summary>
    Task<NotebookSummary> GetInboxAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotebookSummary>> ListAsync(CancellationToken cancellationToken = default);
}
