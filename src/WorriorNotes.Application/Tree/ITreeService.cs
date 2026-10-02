namespace WorriorNotes.Application.Tree;

/// <summary>A folder in a notebook tree.</summary>
public sealed record FolderSummary(Guid Id, Guid NotebookId, Guid? ParentId, string Name, int SortOrder);

/// <summary>Folders and the placement of folders and notes in the notebook trees.</summary>
public interface ITreeService
{
    /// <summary>Folders that are not in the trash, of one notebook or of all, in display order.</summary>
    Task<IReadOnlyList<FolderSummary>> ListFoldersAsync(Guid? notebookId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a folder after its siblings, at the top of the notebook or inside another folder.</summary>
    Task<FolderSummary> CreateFolderAsync(Guid notebookId, Guid? parentId, string name, CancellationToken cancellationToken = default);

    Task<FolderSummary> RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a folder (with everything in it) or a note to another folder or notebook.
    /// <paramref name="index"/> is the position among the new siblings; omitted means last.
    /// </summary>
    Task MoveAsync(Guid nodeId, Guid targetNotebookId, Guid? targetParentId, int? index = null, CancellationToken cancellationToken = default);
}
