using WorriorVex.Application.Notes;

namespace WorriorVex.Application.Revisions;

public sealed record RevisionSummary(Guid Id, Guid NoteId, string Title, DateTimeOffset CreatedAt, string ChangeReason);

public sealed record RevisionDetail(Guid Id, Guid NoteId, string Title, string Content, DateTimeOffset CreatedAt, string ChangeReason);

/// <summary>When earlier versions of a note are kept.</summary>
public static class RevisionPolicy
{
    /// <summary>
    /// While a note is being edited, its state before the edit is kept at most this often,
    /// so a long writing session leaves a handful of revisions rather than one per autosave.
    /// </summary>
    public static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(10);

    public const string EditReason = "Before edit";
    public const string RestoreReason = "Before restoring an earlier version";
}

public interface IRevisionService
{
    /// <summary>Earlier versions of a note, newest first.</summary>
    Task<IReadOnlyList<RevisionSummary>> ListAsync(Guid noteId, CancellationToken cancellationToken = default);

    Task<RevisionDetail?> GetAsync(Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes an earlier version the current one. The version it replaces is itself kept as a
    /// revision first, so restoring never destroys anything.
    /// </summary>
    Task<NoteDetail> RestoreAsync(Guid revisionId, CancellationToken cancellationToken = default);
}
