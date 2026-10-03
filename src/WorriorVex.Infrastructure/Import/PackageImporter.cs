using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Attachments;
using WorriorVex.Application.Content;
using WorriorVex.Application.Import;
using WorriorVex.Application.Tags;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Links;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Search;

namespace WorriorVex.Infrastructure.Import;

public sealed partial class PackageImporter(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    IAttachmentService attachments,
    ITagService tags,
    INoteHtmlSanitizer sanitizer,
    TimeProvider timeProvider,
    ILogger<PackageImporter> logger) : IPackageImporter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ImportReport> ImportAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        if (!File.Exists(path))
        {
            throw new ImportException("The file does not exist.");
        }

        using var zip = OpenZip(path);
        var manifest = ReadEntry<Manifest>(zip, "manifest.json") ?? throw new ImportException("The file has no manifest; it is not a WorriorVex package.");
        if (manifest.Format != "worriorvex-package")
        {
            throw new ImportException($"The file is a \"{manifest.Format}\" file, not a WorriorVex package.");
        }

        if (manifest.FormatVersion > 1)
        {
            throw new ImportException($"The package was written by a newer WorriorVex (format {manifest.FormatVersion}). Update the app to import it.");
        }

        var notebooks = ReadEntry<List<PackedNotebook>>(zip, "notebooks.json") ?? [];
        var nodes = ReadEntry<List<PackedNode>>(zip, "nodes.json") ?? [];
        var problems = new List<string>();
        var now = timeProvider.GetUtcNow();
        var notebookIds = new Dictionary<Guid, Guid>();
        var nodeIds = new Dictionary<Guid, Guid>();
        var counts = new int[6]; // notebooks, folders, notes, attachments, tags, links

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await NextNotebookOrderAsync(context, cancellationToken);
        var existingNames = new HashSet<string>(await context.Notebooks.Where(n => n.DeletedAt == null).Select(n => n.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);

        // Notebooks (the package's Inbox becomes an ordinary notebook, since this copy has its own).
        foreach (var packed in notebooks.Where(n => n.DeletedAt is null).OrderBy(n => n.SortOrder))
        {
            var name = packed.Name;
            if (string.Equals(packed.Kind, "Inbox", StringComparison.OrdinalIgnoreCase) || existingNames.Contains(name))
            {
                name = UniqueName(existingNames, string.Equals(packed.Kind, "Inbox", StringComparison.OrdinalIgnoreCase) ? "Imported Inbox" : name);
            }

            existingNames.Add(name);
            var notebook = Notebook.Create(name, now, order++);
            context.Notebooks.Add(notebook);
            notebookIds[packed.Id] = notebook.Id;
            counts[0]++;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Folders and notes, parents before children. Trashed nodes and anything below them are left out.
        var live = nodes.Where(n => n.DeletedAt is null && notebookIds.ContainsKey(n.NotebookId)).ToList();
        var byParent = live.ToLookup(n => n.ParentId);
        var pendingNotes = new List<(Guid NewId, PackedNode Node)>();

        void Place(Guid? oldParent, Guid? newParent, Guid notebookId, Guid oldNotebookId)
        {
            foreach (var packed in byParent[oldParent].Where(n => n.NotebookId == oldNotebookId).OrderBy(n => n.SortOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (packed.Type == "Folder")
                {
                    var folder = Node.CreateFolder(notebookId, newParent, packed.Name, packed.CreatedAt ?? now, packed.SortOrder);
                    context.Nodes.Add(folder);
                    nodeIds[packed.Id] = folder.Id;
                    counts[1]++;
                    Place(packed.Id, folder.Id, notebookId, oldNotebookId);
                }
                else
                {
                    var note = Node.CreateNote(notebookId, newParent, packed.Name, null, packed.CreatedAt ?? now, packed.SortOrder);
                    note.SetFavorite(packed.IsFavorite);
                    note.SetPinned(packed.IsPinned);
                    context.Nodes.Add(note);
                    nodeIds[packed.Id] = note.Id;
                    pendingNotes.Add((note.Id, packed));
                    counts[2]++;
                }
            }
        }

        foreach (var (oldNotebookId, newNotebookId) in notebookIds)
        {
            Place(null, null, newNotebookId, oldNotebookId);
        }

        await context.SaveChangesAsync(cancellationToken);

        // Note bodies, attachments and tags. Images and links are rewritten to the new ids.
        foreach (var (newId, packed) in pendingNotes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Importing \"{packed.Name}\"…");
            var document = ReadEntry<PackedNote>(zip, $"notes/{packed.Id:D}.json");
            if (document is null)
            {
                problems.Add($"\"{packed.Name}\": the note's text is missing from the package.");
                continue;
            }

            var html = document.Content ?? string.Empty;
            foreach (var attachment in document.Attachments ?? [])
            {
                var entry = zip.GetEntry("attachments/" + attachment.StoredFileName);
                if (entry is null)
                {
                    problems.Add($"\"{packed.Name}\": the file \"{attachment.FileName}\" is missing from the package.");
                    continue;
                }

                await using var stream = entry.Open();
                await using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                try
                {
                    var added = await attachments.AddAsync(newId, attachment.FileName, attachment.ContentType, buffer, cancellationToken);
                    html = html.Replace("attachments/" + attachment.StoredFileName, "attachments/" + added.StoredFileName, StringComparison.OrdinalIgnoreCase);
                    counts[3]++;
                }
                catch (DomainException ex)
                {
                    problems.Add($"\"{packed.Name}\": \"{attachment.FileName}\" was not added: {ex.Message}");
                }
            }

            html = NoteLinkPattern().Replace(html, m => nodeIds.TryGetValue(Guid.Parse(m.Groups[1].Value), out var mapped) ? "note:" + mapped.ToString("D") : m.Value);
            var node = await context.Nodes.Include(n => n.Note).FirstAsync(n => n.Id == newId, cancellationToken);
            node.Edit(node.Name, sanitizer.Sanitize(html), packed.UpdatedAt ?? now);
            await context.SaveChangesAsync(cancellationToken);
            await SearchIndex.IndexNoteAsync(context, node.Id, node.Name, node.Note!.Content, cancellationToken);
            counts[5] += await NoteLinkSync.SyncAsync(context, node.Id, node.Note.Content, packed.UpdatedAt ?? now, cancellationToken);

            foreach (var tag in document.Tags ?? [])
            {
                try
                {
                    await tags.AddToNoteAsync(newId, tag, cancellationToken);
                    counts[4]++;
                }
                catch (DomainException ex)
                {
                    problems.Add($"\"{packed.Name}\": the tag \"{tag}\" was not added: {ex.Message}");
                }
            }
        }

        watch.Stop();
        logger.LogInformation("Package {Path} imported: {Notebooks} notebooks, {Notes} notes in {Duration}", path, counts[0], counts[2], watch.Elapsed);
        return new ImportReport([.. notebookIds.Values], counts[0], counts[1], counts[2], counts[3], counts[4], counts[5], problems, watch.Elapsed);
    }

    private static ZipArchive OpenZip(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new ImportException("The file is not a zip file, so it is not a WorriorVex package.");
        }
    }

    private static T? ReadEntry<T>(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
        {
            return default;
        }

        using var stream = entry.Open();
        try
        {
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (JsonException)
        {
            throw new ImportException($"\"{name}\" in the package could not be read.");
        }
    }

    private static string UniqueName(HashSet<string> taken, string name)
    {
        var candidate = name;
        for (var i = 2; taken.Contains(candidate); i++)
        {
            candidate = $"{name} ({i})";
        }

        return candidate;
    }

    internal static async Task<int> NextNotebookOrderAsync(WorriorVexDbContext context, CancellationToken cancellationToken)
    {
        var highest = await context.Notebooks.Where(n => n.Kind == NotebookKind.User).Select(n => (int?)n.SortOrder).MaxAsync(cancellationToken);
        return highest is { } value ? value + 1 : 0;
    }

    [GeneratedRegex(@"note:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex NoteLinkPattern();

    private sealed record Manifest(string Format, int FormatVersion);
    private sealed record PackedNotebook(Guid Id, string Name, string Kind, int SortOrder, DateTimeOffset? DeletedAt);
    private sealed record PackedNode(Guid Id, Guid NotebookId, Guid? ParentId, string Type, string Name, int SortOrder, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? DeletedAt, bool IsFavorite, bool IsPinned);
    private sealed record PackedAttachment(string FileName, string StoredFileName, string? ContentType);
    private sealed record PackedNote(string? Content, List<string>? Tags, List<PackedAttachment>? Attachments);
}
