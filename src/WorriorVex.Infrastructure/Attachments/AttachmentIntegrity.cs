using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Attachments;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Attachments;

public sealed class AttachmentIntegrity(IDbContextFactory<WorriorVexDbContext> contextFactory, AttachmentFileStore files, ILogger<AttachmentIntegrity> logger) : IAttachmentIntegrity
{
    public async Task<AttachmentCheck> CheckAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attachments = await context.Attachments.AsNoTracking()
            .Join(context.Nodes.AsNoTracking(), a => a.NoteId, n => n.Id, (a, n) => new { a.StoredFileName, a.OriginalFileName, a.Hash, Note = n.Name })
            .ToListAsync(cancellationToken);

        var directory = files.EnsureDirectory();
        var onDisk = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var missing = new List<(string, string)>();
        var damaged = new List<(string, string)>();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedCount = 0;
        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            referenced.Add(attachment.StoredFileName);
            if (!onDisk.TryGetValue(attachment.StoredFileName, out var path))
            {
                missing.Add((attachment.Note, attachment.OriginalFileName));
                continue;
            }

            checkedCount++;
            if (checkedCount % 25 == 0)
            {
                progress?.Report($"Checked {checkedCount} of {attachments.Count} files…");
            }

            if (attachment.Hash.Length > 0)
            {
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!string.Equals(hash, attachment.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    damaged.Add((attachment.Note, attachment.OriginalFileName));
                }
            }
        }

        var orphans = onDisk.Keys.Where(name => !referenced.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var orphanBytes = orphans.Sum(name => new FileInfo(onDisk[name]).Length);
        logger.LogInformation("Attachment check: {Checked} ok, {Missing} missing, {Damaged} damaged, {Orphans} orphan files", checkedCount, missing.Count, damaged.Count, orphans.Count);
        return new AttachmentCheck(checkedCount, missing, orphans, damaged, orphanBytes);
    }

    public async Task<int> DeleteOrphansAsync(IReadOnlyList<string> storedFileNames, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var referenced = await context.Attachments.AsNoTracking().Select(a => a.StoredFileName).ToListAsync(cancellationToken);
        var keep = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);
        var deleted = 0;
        foreach (var name in storedFileNames)
        {
            // Checked again right before deleting: a file attached since the check must stay.
            if (keep.Contains(name) || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            {
                continue;
            }

            var path = Path.Combine(files.EnsureDirectory(), name);
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted++;
            }
        }

        logger.LogInformation("Deleted {Count} orphan attachment files", deleted);
        return deleted;
    }
}
