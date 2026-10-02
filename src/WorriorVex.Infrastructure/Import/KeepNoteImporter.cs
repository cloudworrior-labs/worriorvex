using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Attachments;
using WorriorVex.Application.Content;
using WorriorVex.Application.Import;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Links;
using WorriorVex.Infrastructure.Persistence;
using WorriorVex.Infrastructure.Search;
using static WorriorVex.Infrastructure.Import.KeepNoteReader;

namespace WorriorVex.Infrastructure.Import;

/// <summary>
/// Imports a KeepNote notebook as a new WorriorVex notebook. Folders become folders, pages become
/// notes (a page with children becomes a folder holding the page's note), attached files become
/// attachments, the Trash becomes items in the trash, and nbk:// links become links between notes.
/// </summary>
public sealed class KeepNoteImporter(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    IAttachmentService attachments,
    INoteHtmlSanitizer sanitizer,
    TimeProvider timeProvider,
    ILogger<KeepNoteImporter> logger) : IKeepNoteImporter
{
    public Task<KeepNoteScan> ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        var notebook = Read(path);
        var counts = new Counts();
        var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<string>(notebook.Problems);

        void Count(KeepNode node, bool inTrash)
        {
            foreach (var key in node.UnsupportedAttributes)
            {
                unsupported[key] = unsupported.GetValueOrDefault(key) + 1;
            }

            if (inTrash)
            {
                counts.InTrash++;
            }

            if (node.IsPage)
            {
                counts.Pages++;
                if (node.Children.Any(c => !c.IsAttachment))
                {
                    counts.PagesWithChildren++;
                }

                var pageFile = Path.Combine(node.Directory, PageFile);
                if (File.Exists(pageFile))
                {
                    try
                    {
                        var html = File.ReadAllText(pageFile);
                        counts.Images += KeepNotePageConverter.Convert(html).Images.Count;
                        counts.Links += html.Split("nbk://", StringSplitOptions.None).Length - 1;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        problems.Add($"\"{node.Title}\": page.html could not be read ({ex.Message}).");
                    }
                }
                else
                {
                    problems.Add($"\"{node.Title}\": has no page.html; it will be imported as an empty note.");
                }
            }
            else if (node.IsAttachment)
            {
                counts.Attachments++;
                if (node.PayloadFileName is null || !File.Exists(Path.Combine(node.Directory, node.PayloadFileName)))
                {
                    problems.Add($"\"{node.Title}\": the attached file is missing from its folder.");
                }
            }
            else if (node.IsFolder)
            {
                counts.Folders++;
            }

            foreach (var child in node.Children)
            {
                Count(child, inTrash || node.IsTrash);
            }
        }

        foreach (var child in notebook.Root.Children)
        {
            Count(child, inTrash: false);
        }

        return Task.FromResult(new KeepNoteScan(
            path, notebook.Name, notebook.Version, counts.Folders, counts.Pages, counts.PagesWithChildren,
            counts.Attachments, counts.Images, counts.InTrash, counts.Links, unsupported, problems));
    }

    public async Task<KeepNoteImportReport> ImportAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var notebook = Read(path);
        var problems = new List<string>(notebook.Problems);
        var ignored = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new Counts();
        var now = timeProvider.GetUtcNow();
        var noteIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var pending = new List<PendingNote>();

        progress?.Report($"Reading \"{notebook.Name}\"…");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var target = Notebook.Create(notebook.Name, now, await NextNotebookOrderAsync(context, cancellationToken));
        context.Notebooks.Add(target);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("KeepNote import of {Path} into notebook {NotebookId} started", path, target.Id);

        // Pass 1: the tree, notes with their text as written, attachments and images.
        var position = 0;
        foreach (var child in notebook.Root.Children)
        {
            await ImportNodeAsync(child, parentId: null, deletedAt: null, position++);
        }

        // Pass 2: now that every note has its id, links can point at the right notes; then index.
        progress?.Report("Linking notes…");
        var links = 0;
        var unresolved = 0;
        foreach (var note in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = KeepNotePageConverter.RewriteLinks(note.Html, noteIds, out var found, out var missing);
            links += found;
            unresolved += missing;
            var node = await context.Nodes.Include(n => n.Note).FirstAsync(n => n.Id == note.NodeId, cancellationToken);
            node.Edit(node.Name, sanitizer.Sanitize(html), note.Modified);
            if (note.DeletedAt is { } deletedAt)
            {
                // Text first, then into the trash: a note in the trash cannot be edited.
                node.MoveToTrash(deletedAt);
            }

            await context.SaveChangesAsync(cancellationToken);
            await SearchIndex.IndexNoteAsync(context, node.Id, node.Name, node.Note!.Content, cancellationToken);
            await NoteLinkSync.SyncAsync(context, node.Id, node.Note.Content, note.Modified, cancellationToken);
        }

        watch.Stop();
        logger.LogInformation(
            "KeepNote import finished: {Notes} notes, {Folders} folders, {Attachments} attachments, {Images} images in {Duration}",
            counts.Pages, counts.Folders, counts.Attachments, counts.Images, watch.Elapsed);
        return new KeepNoteImportReport(
            target.Id, target.Name, counts.Folders, counts.Pages, counts.Attachments, counts.Images, counts.InTrash,
            links, unresolved, ignored, problems, watch.Elapsed);

        async Task ImportNodeAsync(KeepNode node, Guid? parentId, DateTimeOffset? deletedAt, int sortOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in node.UnsupportedAttributes)
            {
                ignored[key] = ignored.GetValueOrDefault(key) + 1;
            }

            if (node.IsTrash)
            {
                // The Trash folder itself is not imported; what it holds goes straight to the WorriorVex trash.
                var trashPosition = 0;
                foreach (var child in node.Children)
                {
                    await ImportNodeAsync(child, parentId, deletedAt ?? now, trashPosition++);
                }

                return;
            }

            if (deletedAt is not null)
            {
                counts.InTrash++;
            }

            var created = node.Created ?? now;
            var modified = node.Modified ?? created;
            var folderChildren = node.Children.Where(c => !c.IsAttachment).ToList();
            var attachmentChildren = node.Children.Where(c => c.IsAttachment).ToList();

            if (node.IsPage)
            {
                var noteParentId = parentId;
                if (folderChildren.Count > 0)
                {
                    // A page with pages or folders below it: a folder of the same name, with the page's own note first.
                    var folder = Node.CreateFolder(target.Id, parentId, node.Title, created, sortOrder);
                    if (deletedAt is { } folderDeletedAt)
                    {
                        folder.MoveToTrash(folderDeletedAt);
                    }

                    context.Nodes.Add(folder);
                    await context.SaveChangesAsync(cancellationToken);
                    counts.Folders++;
                    noteParentId = folder.Id;
                }

                var note = Node.CreateNote(target.Id, noteParentId, node.Title, null, created, folderChildren.Count > 0 ? -1 : sortOrder);
                context.Nodes.Add(note);
                await context.SaveChangesAsync(cancellationToken);
                counts.Pages++;
                if (node.NodeId.Length > 0)
                {
                    noteIds[node.NodeId] = note.Id;
                }

                progress?.Report($"Importing \"{node.Title}\"…");
                var html = await ConvertPageAsync(node, note.Id);
                pending.Add(new PendingNote(note.Id, html, modified, deletedAt));

                foreach (var file in attachmentChildren)
                {
                    await AttachAsync(file, note.Id);
                }

                var childPosition = 0;
                foreach (var child in folderChildren)
                {
                    await ImportNodeAsync(child, noteParentId, deletedAt, childPosition++);
                }
            }
            else if (node.IsFolder)
            {
                var folder = Node.CreateFolder(target.Id, parentId, node.Title, created, sortOrder);
                if (deletedAt is { } folderDeletedAt)
                {
                    folder.MoveToTrash(folderDeletedAt);
                }

                context.Nodes.Add(folder);
                await context.SaveChangesAsync(cancellationToken);
                counts.Folders++;

                if (attachmentChildren.Count > 0)
                {
                    // Files directly in a folder: a note named "Files" holds them.
                    var holder = Node.CreateNote(target.Id, folder.Id, "Files", null, created, -1);
                    context.Nodes.Add(holder);
                    await context.SaveChangesAsync(cancellationToken);
                    counts.Pages++;
                    pending.Add(new PendingNote(holder.Id, string.Empty, created, deletedAt));
                    foreach (var file in attachmentChildren)
                    {
                        await AttachAsync(file, holder.Id);
                    }
                }

                var childPosition = 0;
                foreach (var child in folderChildren)
                {
                    await ImportNodeAsync(child, folder.Id, deletedAt, childPosition++);
                }
            }
            else
            {
                // A file node outside any page: a note named after the file carries it.
                var holder = Node.CreateNote(target.Id, parentId, node.Title, null, created, sortOrder);
                context.Nodes.Add(holder);
                await context.SaveChangesAsync(cancellationToken);
                counts.Pages++;
                pending.Add(new PendingNote(holder.Id, string.Empty, modified, deletedAt));
                await AttachAsync(node, holder.Id);
            }
        }

        async Task<string> ConvertPageAsync(KeepNode node, Guid noteId)
        {
            var pageFile = Path.Combine(node.Directory, PageFile);
            if (!File.Exists(pageFile))
            {
                problems.Add($"\"{node.Title}\": had no page.html, so the note is empty.");
                return string.Empty;
            }

            string html;
            try
            {
                html = await File.ReadAllTextAsync(pageFile, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"\"{node.Title}\": page.html could not be read ({ex.Message}), so the note is empty.");
                return string.Empty;
            }

            var page = KeepNotePageConverter.Convert(html);
            foreach (var image in page.Images)
            {
                var file = Path.Combine(node.Directory, image.FileName);
                var contentType = NoteContentRules.ImageContentType(image.FileName);
                if (!File.Exists(file) || contentType is null)
                {
                    problems.Add($"\"{node.Title}\": the picture \"{image.FileName}\" is {(File.Exists(file) ? "not a kind WorriorVex shows" : "missing")}; it was left out.");
                    image.Element.Remove();
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(file);
                    var attachment = await attachments.AddAsync(noteId, image.FileName, contentType, stream, cancellationToken);
                    image.Element.SetAttribute("src", NoteContentRules.AttachmentSource(attachment.StoredFileName));
                    counts.Images++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DomainException)
                {
                    problems.Add($"\"{node.Title}\": the picture \"{image.FileName}\" could not be copied ({ex.Message}).");
                    image.Element.Remove();
                }
            }

            return page.Html;
        }

        async Task AttachAsync(KeepNode fileNode, Guid noteId)
        {
            var name = fileNode.PayloadFileName ?? fileNode.Title;
            var file = Path.Combine(fileNode.Directory, name);
            if (!File.Exists(file))
            {
                problems.Add($"\"{fileNode.Title}\": the attached file is missing from its folder; nothing was attached.");
                return;
            }

            try
            {
                await using var stream = File.OpenRead(file);
                var contentType = fileNode.ContentType.Contains('/') ? fileNode.ContentType : null;
                await attachments.AddAsync(noteId, fileNode.Title.Length > 0 ? fileNode.Title : name, contentType, stream, cancellationToken);
                counts.Attachments++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DomainException)
            {
                problems.Add($"\"{fileNode.Title}\": the file could not be attached ({ex.Message}).");
            }
        }
    }

    private static async Task<int> NextNotebookOrderAsync(WorriorVexDbContext context, CancellationToken cancellationToken)
    {
        var highest = await context.Notebooks.Where(n => n.Kind == NotebookKind.User).Select(n => (int?)n.SortOrder).MaxAsync(cancellationToken);
        return highest is { } value ? value + 1 : 0;
    }

    private sealed class Counts
    {
        public int Folders;
        public int Pages;
        public int PagesWithChildren;
        public int Attachments;
        public int Images;
        public int InTrash;
        public int Links;
    }

    private sealed record PendingNote(Guid NodeId, string Html, DateTimeOffset Modified, DateTimeOffset? DeletedAt);
}
