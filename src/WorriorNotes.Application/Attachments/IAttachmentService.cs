namespace WorriorNotes.Application.Attachments;

public sealed record AttachmentInfo(Guid Id, Guid NoteId, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt);

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

    Task<AttachmentInfo> RenameAsync(Guid attachmentId, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Removes the attachment and its file.</summary>
    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
