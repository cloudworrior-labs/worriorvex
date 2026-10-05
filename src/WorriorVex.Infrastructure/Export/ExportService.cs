using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Content;
using WorriorVex.Application.Export;
using WorriorVex.Application.Notes;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Attachments;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Export;

public sealed class ExportService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    AttachmentFileStore files,
    TimeProvider timeProvider,
    ILogger<ExportService> logger) : IExportService
{
    public const string PackageFormat = "worriorvex-package";
    public const int PackageFormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ExportResult> ExportNoteAsync(Guid noteId, ExportFormat format, string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var note = await LoadAsync(context, noteId, cancellationToken) ?? throw new NoteNotFoundException(noteId);
        var problems = new List<string>();
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? ".";
        Directory.CreateDirectory(directory);

        var written = format switch
        {
            ExportFormat.Html => await WriteHtmlAsync(note, destinationPath, _ => null, problems, cancellationToken),
            ExportFormat.Markdown => await WriteMarkdownAsync(note, destinationPath, Path.GetFileNameWithoutExtension(destinationPath) + "_files", _ => null, problems, cancellationToken),
            _ => await WriteJsonAsync(context, note, destinationPath, includeFiles: true, cancellationToken),
        };
        return new ExportResult(destinationPath, 1, written, problems);
    }

    public async Task<ExportResult> ExportAllAsync(ExportFormat format, string destinationDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var notebooks = await context.Notebooks.AsNoTracking().Where(n => n.DeletedAt == null).OrderBy(n => n.SortOrder).ThenBy(n => n.Name).ToListAsync(cancellationToken);
        var nodes = await context.Nodes.AsNoTracking().Where(n => n.DeletedAt == null).OrderBy(n => n.SortOrder).ThenBy(n => n.Name).ToListAsync(cancellationToken);
        var problems = new List<string>();
        Directory.CreateDirectory(destinationDirectory);

        // Every note gets its path first, so links between notes can point at files that will exist.
        var paths = new Dictionary<Guid, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderPaths = new Dictionary<Guid, string>();
        foreach (var notebook in notebooks)
        {
            var notebookPath = Unique(taken, SafeName(notebook.Name), string.Empty);
            PlaceNodes(nodes, notebook.Id, null, notebookPath, taken, paths, folderPaths, IExportService.Extension(format));
        }

        var count = 0;
        var fileCount = 0;
        foreach (var (noteId, relativePath) in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var note = await LoadAsync(context, noteId, cancellationToken);
            if (note is null)
            {
                continue;
            }

            progress?.Report($"Exporting \"{note.Node.Name}\"…");
            var destination = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string? LinkTo(Guid target) => paths.TryGetValue(target, out var targetPath) ? RelativeLink(relativePath, targetPath) : null;

            fileCount += format switch
            {
                ExportFormat.Html => await WriteHtmlAsync(note, destination, LinkTo, problems, cancellationToken),
                ExportFormat.Markdown => await WriteMarkdownAsync(note, destination, Path.GetFileNameWithoutExtension(destination) + "_files", LinkTo, problems, cancellationToken),
                _ => await WriteJsonAsync(context, note, destination, includeFiles: true, cancellationToken),
            };
            count++;
        }

        logger.LogInformation("Exported {Count} notes as {Format} to {Directory}", count, format, destinationDirectory);
        return new ExportResult(destinationDirectory, count, fileCount, problems);
    }

    public async Task<ExportResult> ExportPackageAsync(string destinationPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var problems = new List<string>();
        var temporary = destinationPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? ".");

        var notebooks = await context.Notebooks.AsNoTracking().ToListAsync(cancellationToken);
        var nodes = await context.Nodes.AsNoTracking().ToListAsync(cancellationToken);
        var tags = await context.Tags.AsNoTracking().ToListAsync(cancellationToken);
        var noteIds = nodes.Where(n => n.Type == NodeType.Note).Select(n => n.Id).ToList();
        var fileCount = 0;
        try
        {
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "manifest.json", new
                {
                    format = PackageFormat,
                    formatVersion = PackageFormatVersion,
                    createdAt = timeProvider.GetUtcNow(),
                    notebooks = notebooks.Count,
                    notes = noteIds.Count,
                    tags = tags.Count,
                }, cancellationToken);
                await WriteEntryAsync(zip, "notebooks.json", notebooks.Select(n => new { n.Id, n.Name, kind = n.Kind.ToString(), n.SortOrder, n.CreatedAt, n.UpdatedAt, n.DeletedAt }), cancellationToken);
                await WriteEntryAsync(zip, "nodes.json", nodes.Select(n => new { n.Id, n.NotebookId, n.ParentId, type = n.Type.ToString(), n.Name, n.SortOrder, n.CreatedAt, n.UpdatedAt, n.DeletedAt, n.IsFavorite, n.IsPinned, n.LastOpenedAt, n.CalendarDate }), cancellationToken);
                await WriteEntryAsync(zip, "tags.json", tags.Select(t => new { t.Id, t.Name, t.CreatedAt }), cancellationToken);

                var storedFiles = new HashSet<string>();
                foreach (var noteId in noteIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var note = await LoadAsync(context, noteId, cancellationToken);
                    if (note is null)
                    {
                        continue;
                    }

                    progress?.Report($"Packing \"{note.Node.Name}\"…");
                    await WriteEntryAsync(zip, $"notes/{noteId:D}.json", await NoteDocumentAsync(context, note, includeFiles: false, cancellationToken), cancellationToken);
                    foreach (var attachment in note.Attachments)
                    {
                        if (!storedFiles.Add(attachment.StoredFileName))
                        {
                            continue;
                        }

                        var path = files.GetPath(attachment.StoredFileName);
                        if (File.Exists(path))
                        {
                            zip.CreateEntryFromFile(path, "attachments/" + attachment.StoredFileName, CompressionLevel.Fastest);
                            fileCount++;
                        }
                        else
                        {
                            problems.Add($"\"{note.Node.Name}\": the file for \"{attachment.OriginalFileName}\" is missing and was left out.");
                        }
                    }
                }
            }

            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        logger.LogInformation("Package written to {Path}: {Notes} notes, {Files} files", destinationPath, noteIds.Count, fileCount);
        return new ExportResult(destinationPath, noteIds.Count, fileCount, problems);
    }

    // ---- Loading -------------------------------------------------------------------------------

    private sealed record LoadedNote(Node Node, string Content, List<Attachment> Attachments, List<string> Tags, List<Guid> Links);

    private static async Task<LoadedNote?> LoadAsync(WorriorVexDbContext context, Guid noteId, CancellationToken cancellationToken)
    {
        var node = await context.Nodes.AsNoTracking().Include(n => n.Note).FirstOrDefaultAsync(n => n.Id == noteId && n.Type == NodeType.Note, cancellationToken);
        if (node is null)
        {
            return null;
        }

        var attachments = await context.Attachments.AsNoTracking().Where(a => a.NoteId == noteId).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var tags = await context.NoteTags.Where(nt => nt.NoteId == noteId).Join(context.Tags, nt => nt.TagId, t => t.Id, (nt, t) => t.Name).OrderBy(n => n).ToListAsync(cancellationToken);
        var links = await context.NoteLinks.Where(l => l.SourceNoteId == noteId).Select(l => l.TargetNoteId).ToListAsync(cancellationToken);
        return new LoadedNote(node, node.Note?.Content ?? string.Empty, attachments, tags, links);
    }

    // ---- Formats -------------------------------------------------------------------------------

    private async Task<int> WriteHtmlAsync(LoadedNote note, string destination, Func<Guid, string?> linkTo, List<string> problems, CancellationToken cancellationToken)
    {
        var byStoredName = note.Attachments.ToDictionary(a => NoteContentRules.AttachmentSource(a.StoredFileName), a => a);
        var html = note.Content;
        foreach (var (source, attachment) in byStoredName)
        {
            var path = files.GetPath(attachment.StoredFileName);
            if (File.Exists(path) && html.Contains(source, StringComparison.Ordinal))
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                html = html.Replace("\"" + source + "\"", $"\"data:{attachment.ContentType};base64,{Convert.ToBase64String(bytes)}\"", StringComparison.Ordinal);
            }
            else if (html.Contains(source, StringComparison.Ordinal))
            {
                problems.Add($"\"{note.Node.Name}\": the picture \"{attachment.OriginalFileName}\" is missing and was left out.");
            }
        }

        html = RewriteNoteLinks(html, linkTo);
        var title = WebUtility.HtmlEncode(note.Node.Name);
        var tags = note.Tags.Count == 0 ? string.Empty : $"<p class=\"tags\">{string.Join(" ", note.Tags.Select(t => "#" + WebUtility.HtmlEncode(t)))}</p>";
        var document = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{title}}</title>
            <style>
            body { max-width: 760px; margin: 2rem auto; padding: 0 1rem; font: 16px/1.6 -apple-system, "Segoe UI", Roboto, sans-serif; color: #1c2126; }
            h1.title { font-size: 1.8rem; margin-bottom: 0.2rem; }
            .meta, .tags { color: #5a646e; font-size: 0.9rem; }
            img { max-width: 100%; height: auto; }
            table { border-collapse: collapse; } th, td { border: 1px solid #d5d9dd; padding: 0.25rem 0.5rem; text-align: left; }
            pre, code { font-family: ui-monospace, Menlo, Consolas, monospace; background: #f0f2f4; border-radius: 4px; } pre { padding: 0.75rem; overflow-x: auto; } code { padding: 0.1em 0.3em; }
            blockquote { margin: 0; padding-left: 0.75rem; border-left: 3px solid #d5d9dd; color: #5a646e; }
            ul[data-type="taskList"] { list-style: none; padding-left: 0.2em; } ul[data-type="taskList"] li { display: flex; gap: 0.5em; } ul[data-type="taskList"] li > div { flex: 1; } ul[data-type="taskList"] p { margin: 0; }
            </style>
            </head>
            <body>
            <h1 class="title">{{title}}</h1>
            <p class="meta">Created {{note.Node.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}} · Updated {{note.Node.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}} · Exported from WorriorVex</p>
            {{tags}}
            <main>
            {{html}}
            </main>
            </body>
            </html>
            """;
        await File.WriteAllTextAsync(destination, document, new UTF8Encoding(false), cancellationToken);
        return 1;
    }

    private async Task<int> WriteMarkdownAsync(LoadedNote note, string destination, string filesFolderName, Func<Guid, string?> linkTo, List<string> problems, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)!;
        var filesFolder = Path.Combine(directory, filesFolderName);
        var written = 1;
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in note.Attachments)
        {
            var path = files.GetPath(attachment.StoredFileName);
            if (!File.Exists(path))
            {
                problems.Add($"\"{note.Node.Name}\": the file \"{attachment.OriginalFileName}\" is missing and was left out.");
                continue;
            }

            Directory.CreateDirectory(filesFolder);
            var name = Unique(usedNames, SafeName(Path.GetFileNameWithoutExtension(attachment.OriginalFileName)), Path.GetExtension(attachment.OriginalFileName));
            File.Copy(path, Path.Combine(filesFolder, name), overwrite: true);
            copied[NoteContentRules.AttachmentSource(attachment.StoredFileName)] = filesFolderName + "/" + Uri.EscapeDataString(name);
            written++;
        }

        var markdown = HtmlToMarkdown.Convert(
            note.Content,
            href => NoteContentRules.NoteLinkTarget(href) is { } target ? linkTo(target) : NoteContentRules.IsWebLink(href) ? href : null,
            src => copied.GetValueOrDefault(src));

        var header = new StringBuilder();
        header.Append("# ").Append(note.Node.Name).Append("\n\n");
        if (note.Tags.Count > 0)
        {
            header.Append("Tags: ").Append(string.Join(", ", note.Tags)).Append("  \n");
        }

        header.Append("Created: ").Append(note.Node.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("  \n");
        header.Append("Updated: ").Append(note.Node.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("\n\n");
        var extra = note.Attachments.Where(a => copied.ContainsKey(NoteContentRules.AttachmentSource(a.StoredFileName)) && !note.Content.Contains(NoteContentRules.AttachmentSource(a.StoredFileName), StringComparison.Ordinal)).ToList();
        var footer = extra.Count == 0 ? string.Empty : "\n## Attachments\n\n" + string.Join("\n", extra.Select(a => $"- [{a.OriginalFileName}]({copied[NoteContentRules.AttachmentSource(a.StoredFileName)]})")) + "\n";

        await File.WriteAllTextAsync(destination, header + markdown + footer, new UTF8Encoding(false), cancellationToken);
        return written;
    }

    private async Task<int> WriteJsonAsync(WorriorVexDbContext context, LoadedNote note, string destination, bool includeFiles, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, await NoteDocumentAsync(context, note, includeFiles, cancellationToken), Json, cancellationToken);
        return 1;
    }

    /// <summary>Everything about one note. With <paramref name="includeFiles"/>, attachment bytes travel inside as base64.</summary>
    private async Task<object> NoteDocumentAsync(WorriorVexDbContext context, LoadedNote note, bool includeFiles, CancellationToken cancellationToken)
    {
        var revisions = await context.NoteRevisions.AsNoTracking().Where(r => r.NoteId == note.Node.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Title, r.Content, r.CreatedAt, r.ChangeReason }).ToListAsync(cancellationToken);
        var attachments = new List<object>();
        foreach (var attachment in note.Attachments)
        {
            string? data = null;
            if (includeFiles)
            {
                var path = files.GetPath(attachment.StoredFileName);
                data = File.Exists(path) ? Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken)) : null;
            }

            attachments.Add(new
            {
                attachment.Id,
                fileName = attachment.OriginalFileName,
                attachment.StoredFileName,
                attachment.ContentType,
                attachment.Size,
                sha256 = attachment.Hash,
                attachment.CreatedAt,
                contentBase64 = data,
            });
        }

        return new
        {
            format = "worriorvex-note",
            formatVersion = 1,
            id = note.Node.Id,
            note.Node.NotebookId,
            note.Node.ParentId,
            title = note.Node.Name,
            contentFormat = "html",
            content = note.Content,
            note.Node.CreatedAt,
            note.Node.UpdatedAt,
            note.Node.DeletedAt,
            note.Node.IsFavorite,
            note.Node.IsPinned,
            tags = note.Tags,
            linksTo = note.Links,
            attachments,
            revisions,
        };
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static async Task WriteEntryAsync(ZipArchive zip, string name, object value, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken);
    }

    private static void PlaceNodes(List<Node> nodes, Guid notebookId, Guid? parentId, string directory, HashSet<string> taken, Dictionary<Guid, string> paths, Dictionary<Guid, string> folderPaths, string extension)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Where(n => n.NotebookId == notebookId && n.ParentId == parentId))
        {
            if (node.Type == NodeType.Folder)
            {
                var folder = Path.Combine(directory, Unique(names, SafeName(node.Name), string.Empty));
                folderPaths[node.Id] = folder;
                PlaceNodes(nodes, notebookId, node.Id, folder, taken, paths, folderPaths, extension);
            }
            else
            {
                paths[node.Id] = Path.Combine(directory, Unique(names, SafeName(node.Name), extension));
            }
        }
    }

    private static string RewriteNoteLinks(string html, Func<Guid, string?> linkTo)
    {
        foreach (var target in NoteContentRules.NoteLinkTargets(html))
        {
            var address = NoteContentRules.NoteLinkAddress(target);
            var replacement = linkTo(target);
            html = replacement is null
                ? html.Replace($" href=\"{address}\"", string.Empty, StringComparison.Ordinal)
                : html.Replace($"href=\"{address}\"", $"href=\"{WebUtility.HtmlEncode(replacement)}\"", StringComparison.Ordinal);
        }

        return html;
    }

    private static string RelativeLink(string fromFile, string toFile)
    {
        var fromDirectory = Path.GetDirectoryName(fromFile) ?? string.Empty;
        var relative = Path.GetRelativePath(fromDirectory.Length == 0 ? "." : fromDirectory, toFile).Replace('\\', '/');
        return string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>A file or folder name from a title: no path characters, nothing the file systems refuse.</summary>
    internal static string SafeName(string title)
    {
        var cleaned = Attachment.CleanFileName(title.Replace('/', '-').Replace('\\', '-'));
        return cleaned.Length > 120 ? cleaned[..120].TrimEnd() : cleaned;
    }

    private static string Unique(HashSet<string> taken, string stem, string extension)
    {
        var candidate = stem + extension;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }

        return candidate;
    }
}
