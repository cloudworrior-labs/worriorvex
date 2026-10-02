using Microsoft.Extensions.Logging;
using WorriorVex.Application.Storage;
using WorriorVex.Domain;

namespace WorriorVex.Infrastructure.Attachments;

/// <summary>The attachments folder. Files in it carry names the application generated.</summary>
public sealed class AttachmentFileStore(IApplicationDataPathProvider paths, ILogger<AttachmentFileStore> logger)
{
    public string EnsureDirectory()
    {
        Directory.CreateDirectory(paths.AttachmentsDirectory);
        return paths.AttachmentsDirectory;
    }

    /// <summary>Full path of a stored file. Refuses any name that would lead outside the attachments folder.</summary>
    public string GetPath(string storedFileName)
    {
        var root = Path.GetFullPath(paths.AttachmentsDirectory);
        var path = Path.GetFullPath(Path.Combine(root, storedFileName));
        if (Path.GetDirectoryName(path) != root || Path.GetFileName(path) != storedFileName)
        {
            throw new InvalidOperationException("The stored file name does not point into the attachments folder.");
        }

        return path;
    }

    /// <summary>
    /// Copies a stored file for another note, under a new generated name. Returns the new attachment
    /// row to add, or <c>null</c> when the original file is missing (the copy then simply has no file).
    /// </summary>
    public Attachment? Copy(Attachment original, Guid noteId, DateTimeOffset now)
    {
        var source = GetPath(original.StoredFileName);
        if (!File.Exists(source))
        {
            logger.LogWarning("Attachment file {StoredFileName} is missing and was not copied", original.StoredFileName);
            return null;
        }

        var copy = Attachment.Create(Guid.NewGuid(), noteId, original.OriginalFileName, original.ContentType, original.Size, original.Hash, now);
        EnsureDirectory();
        File.Copy(source, GetPath(copy.StoredFileName), overwrite: false);
        return copy;
    }

    /// <summary>
    /// Removes stored files whose database rows are already gone. A file that cannot be removed
    /// is logged and left behind; it no longer belongs to any note.
    /// </summary>
    public void Delete(IEnumerable<string> storedFileNames)
    {
        foreach (var name in storedFileNames)
        {
            try
            {
                File.Delete(GetPath(name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Attachment file {StoredFileName} could not be removed", name);
            }
        }
    }
}
