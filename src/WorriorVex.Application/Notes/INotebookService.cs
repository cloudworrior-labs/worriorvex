namespace WorriorVex.Application.Notes;

public interface INotebookService
{
    /// <summary>Returns the Inbox, creating it on first use.</summary>
    Task<NotebookSummary> GetInboxAsync(CancellationToken cancellationToken = default);

    /// <summary>Notebooks that are not in the trash, Inbox first, then in the user's order.</summary>
    Task<IReadOnlyList<NotebookSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a notebook after the existing ones.</summary>
    Task<NotebookSummary> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<NotebookSummary> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Moves a notebook to a position among the user's notebooks (0 is first, after the Inbox).</summary>
    Task ReorderAsync(Guid id, int index, CancellationToken cancellationToken = default);
}
