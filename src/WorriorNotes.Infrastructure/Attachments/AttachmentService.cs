using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WorriorNotes.Application.Attachments;
using WorriorNotes.Application.Common;
using WorriorNotes.Application.Notes;
using WorriorNotes.Domain;
using WorriorNotes.Infrastructure.Persistence;

namespace WorriorNotes.Infrastructure.Attachments;

public sealed class AttachmentService(
    IDbContextFactory<WorriorNotesDbContext> contextFactory,
    AttachmentFileStore files,
    TimeProvider timeProvider) : IAttachmentService
{
    private const int BufferSize = 81920;

    public async Task<AttachmentInfo> AddAsync(Guid noteId, string fileName, string? contentType, Stream content, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var note = await context.Nodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.Type == NodeType.Note, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);
        if (note.IsDeleted)
        {
            throw new DomainException("A note in the trash cannot take attachments. Restore it first.");
        }

        var id = Guid.NewGuid();
        var directory = files.EnsureDirectory();
        var incoming = Path.Combine(directory, id.ToString("N") + ".incoming");
        try
        {
            var (size, hash) = await CopyAsync(content, incoming, cancellationToken);
            var attachment = Attachment.Create(id, noteId, fileName, contentType, size, hash, timeProvider.GetUtcNow());
            var stored = files.GetPath(attachment.StoredFileName);
            File.Move(incoming, stored);
            try
            {
                context.Attachments.Add(attachment);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                files.Delete([attachment.StoredFileName]);
                throw;
            }

            return ToInfo(attachment);
        }
        finally
        {
            File.Delete(incoming);
        }
    }

    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Attachments
            .AsNoTracking()
            .Where(a => a.NoteId == noteId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AttachmentInfo(a.Id, a.NoteId, a.OriginalFileName, a.ContentType, a.Size, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachment = await context.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Attachment", attachmentId);

        var path = files.GetPath(attachment.StoredFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The file for attachment \"{attachment.OriginalFileName}\" is missing from the attachments folder.", path);
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
    }

    public async Task<AttachmentInfo> RenameAsync(Guid attachmentId, string fileName, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken)
            ?? throw new EntityNotFoundException("Attachment", attachmentId);

        attachment.Rename(fileName);
        await context.SaveChangesAsync(cancellationToken);
        return ToInfo(attachment);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachment = await context.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return;
        }

        // Row first: a leftover file is harmless, a row without its file is a broken attachment.
        context.Attachments.Remove(attachment);
        await context.SaveChangesAsync(cancellationToken);
        files.Delete([attachment.StoredFileName]);
    }

    /// <summary>Writes the stream to a file, stopping as soon as it grows past the size limit.</summary>
    private static async Task<(long Size, string Hash)> CopyAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long size = 0;

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            size += read;
            if (size > Attachment.MaxSizeBytes)
            {
                throw new DomainException($"An attachment can be at most {Attachment.MaxSizeBytes / (1024 * 1024)} MB.");
            }

            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await file.FlushAsync(cancellationToken);
        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static AttachmentInfo ToInfo(Attachment attachment) =>
        new(attachment.Id, attachment.NoteId, attachment.OriginalFileName, attachment.ContentType, attachment.Size, attachment.CreatedAt);
}
