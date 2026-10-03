namespace WorriorVex.Application.Attachments;

/// <param name="FileName">The name the user knows the file by.</param>
/// <param name="StoredFileName">The generated name of the file in the attachments folder.</param>
public sealed record AttachmentInfo(Guid Id, Guid NoteId, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, string StoredFileName);

public interface IAttachmentService
{
    /// <summary>
    /// Copies a file into the attachments folder and attaches it to a note. The file name is only
    /// kept for display; it never decides where the file is stored. Files over the size limit are refused.
    /// </summary>
    Task<AttachmentInfo> AddAsync(Guid noteId, string fileName, string? contentType, Stream content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttachmentInfo>> ListAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored file for reading. The caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Where the stored file lies on disk, for handing it to another program. The file is read-only data of the app; it must not be changed in place.</summary>
    Task<string> GetFilePathAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Writes a copy of the stored file to a path the user chose.</summary>
    Task SaveCopyAsync(Guid attachmentId, string destinationPath, CancellationToken cancellationToken = default);

    Task<AttachmentInfo> RenameAsync(Guid attachmentId, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Removes the attachment and its file.</summary>
    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}

/// <summary>What a look through the attachments folder found.</summary>
/// <param name="Missing">Attachments whose file is not on disk (note title and file name).</param>
/// <param name="Orphans">Files in the folder that no attachment refers to.</param>
/// <param name="Damaged">Files whose contents no longer match the hash recorded when they were added.</param>
public sealed record AttachmentCheck(int Checked, IReadOnlyList<(string Note, string FileName)> Missing, IReadOnlyList<string> Orphans, IReadOnlyList<(string Note, string FileName)> Damaged, long OrphanBytes);

public interface IAttachmentIntegrity
{
    /// <summary>Compares the attachments table with the files on disk. Changes nothing.</summary>
    Task<AttachmentCheck> CheckAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes files that no attachment refers to. Returns how many were deleted.</summary>
    Task<int> DeleteOrphansAsync(IReadOnlyList<string> storedFileNames, CancellationToken cancellationToken = default);
}
