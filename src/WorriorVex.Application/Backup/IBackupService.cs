namespace WorriorVex.Application.Backup;

/// <summary>What a backup file says about itself.</summary>
public sealed record BackupManifest(
    string Format,
    int FormatVersion,
    string AppVersion,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    int Notebooks,
    int Notes,
    int Attachments);

/// <summary>A backup file that was looked at. <see cref="Problems"/> is empty when it can be restored.</summary>
public sealed record BackupInfo(string Path, long Size, BackupManifest? Manifest, IReadOnlyList<string> Problems)
{
    public bool CanRestore => Manifest is not null && Problems.Count == 0;
}

/// <summary>Whole-data backups: the database, the attachments and a manifest, in one zip file.</summary>
public interface IBackupService
{
    /// <summary>
    /// Writes a backup. Without a destination it goes to the backups folder under a timestamped name.
    /// The database is copied with SQLite's own backup, so a backup taken while the app runs is consistent.
    /// Returns the path of the file written.
    /// </summary>
    Task<string> CreateBackupAsync(string? destinationPath = null, CancellationToken cancellationToken = default);

    /// <summary>Backups in the backups folder, newest first.</summary>
    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a backup file and checks it without restoring anything.</summary>
    Task<BackupInfo> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the current data with the backup. The data being replaced is first written to a safety
    /// backup in the backups folder, whose path is returned. The caller must have no unsaved edits.
    /// </summary>
    Task<string> RestoreAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>The file is not a backup WorriorVex can restore.</summary>
public sealed class BackupException(string message) : Exception(message);

/// <summary>
/// Takes backups on a schedule (Settings → Your data) while the app is open: when one is due, a backup
/// named <c>auto-…</c> goes to the backups folder and the oldest automatic backups beyond the number
/// to keep are deleted. Backups the user took by hand are never touched.
/// </summary>
public interface IBackupScheduler : IAsyncDisposable
{
    /// <summary>Starts watching the clock. Safe to call more than once.</summary>
    void Start();

    /// <summary>Raised after an automatic backup was written (with its path) or failed (with the error).</summary>
    event Action<string?, Exception?>? Completed;

    /// <summary>Runs the check now instead of waiting for the next tick; a backup is taken only when one is due.</summary>
    Task RunNowAsync(CancellationToken cancellationToken = default);
}
