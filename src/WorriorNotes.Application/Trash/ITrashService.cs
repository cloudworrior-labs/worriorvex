namespace WorriorNotes.Application.Trash;

public enum TrashItemKind
{
    Note,
    Folder,
    Notebook,
}

/// <summary>
/// Something the user deleted. A deleted folder or notebook is one item; what was inside it at
/// that moment comes back with it and is not listed separately.
/// </summary>
public sealed record TrashItem(Guid Id, TrashItemKind Kind, string Name, DateTimeOffset DeletedAt, string OriginalLocation);

/// <param name="Relocated">
/// <c>true</c> when the original place no longer exists, so the item was put at the top of
/// its notebook, or in the Inbox when the notebook is gone too.
/// </param>
public sealed record RestoreResult(Guid Id, TrashItemKind Kind, Guid NotebookId, Guid? ParentId, bool Relocated);

/// <summary>Deleting is moving to the trash. Only this service removes anything for good.</summary>
public interface ITrashService
{
    /// <summary>Moves a note, or a folder with everything in it, to the trash.</summary>
    Task MoveToTrashAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>Moves a notebook with everything in it to the trash. The Inbox cannot be deleted.</summary>
    Task MoveNotebookToTrashAsync(Guid notebookId, CancellationToken cancellationToken = default);

    /// <summary>What is in the trash, most recently deleted first.</summary>
    Task<IReadOnlyList<TrashItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Takes an item, and what was deleted along with it, out of the trash.</summary>
    Task<RestoreResult> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes an item in the trash for good, including its attachment files.</summary>
    Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes everything in the trash for good. Returns how many items were removed.</summary>
    Task<int> EmptyAsync(CancellationToken cancellationToken = default);
}
