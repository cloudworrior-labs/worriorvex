using System.Diagnostics;
using System.Text.RegularExpressions;
using Markdig;
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

public sealed partial class MarkdownImporter(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    IAttachmentService attachments,
    ITagService tags,
    INoteHtmlSanitizer sanitizer,
    TimeProvider timeProvider,
    ILogger<MarkdownImporter> logger) : IMarkdownImporter
{
    private static readonly string[] NoteExtensions = [".md", ".markdown", ".txt"];
    private static readonly string[] SkippedDirectories = [".obsidian", ".trash", ".git", "_resources", ".resources"];
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    public async Task<ImportReport> ImportAsync(string directory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        if (!Directory.Exists(directory))
        {
            throw new ImportException("The folder does not exist.");
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => NoteExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(directory, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => SkippedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            throw new ImportException("The folder holds no Markdown (.md) or text (.txt) files.");
        }

        var problems = new List<string>();
        var now = timeProvider.GetUtcNow();
        var counts = new int[6];
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        var notebook = Notebook.Create(name.Length > 0 ? name : "Imported notes", now, await PackageImporter.NextNotebookOrderAsync(context, cancellationToken));
        context.Notebooks.Add(notebook);
        await context.SaveChangesAsync(cancellationToken);
        counts[0] = 1;

        // Pass 1: folders and empty notes, so every file has an id before links are rewritten.
        var folderIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var noteByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var noteByTitle = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var sort = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentId = await FolderForAsync(Path.GetDirectoryName(Path.GetRelativePath(directory, file)) ?? string.Empty);
            var title = Path.GetFileNameWithoutExtension(file);
            var note = Node.CreateNote(notebook.Id, parentId, title, null, File.GetCreationTimeUtc(file), sort++);
            context.Nodes.Add(note);
            noteByPath[Path.GetFullPath(file)] = note.Id;
            noteByTitle.TryAdd(title, note.Id);
            counts[2]++;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Pass 2: the text, with images attached and links pointed at the right notes.
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var noteId = noteByPath[Path.GetFullPath(file)];
            var title = Path.GetFileNameWithoutExtension(file);
            progress?.Report($"Importing \"{title}\"…");
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (IOException ex)
            {
                problems.Add($"\"{title}\": the file could not be read ({ex.Message}).");
                continue;
            }

            var (body, frontTags) = StripFrontMatter(text);
            var isMarkdown = !string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase);

            // [[Wiki links]] and ![[embedded images]] before Markdown, since Markdig does not know them.
            var fileDirectory = Path.GetDirectoryName(file)!;
            body = WikiLink().Replace(body, m =>
            {
                var target = m.Groups["target"].Value.Trim();
                var label = m.Groups["label"].Success ? m.Groups["label"].Value : target;
                if (m.Value.StartsWith('!'))
                {
                    return $"![{label}]({Uri.EscapeDataString(target)})";
                }

                var noteTarget = ResolveNote(target, fileDirectory);
                return noteTarget is { } id ? $"[{label}](note:{id:D})" : label;
            });

            var html = isMarkdown ? Markdown.ToHtml(body, Pipeline) : "<p>" + string.Join("</p><p>", body.Split("\n\n").Select(p => System.Net.WebUtility.HtmlEncode(p).Replace("\n", "<br>"))) + "</p>";

            // Images: files next to the note (or anywhere under the folder) become attachments.
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match image in ImageSource().Matches(html))
            {
                var source = image.Groups[1].Value;
                if (replacements.ContainsKey(source) || source.StartsWith("http", StringComparison.OrdinalIgnoreCase) || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var imagePath = ResolveFile(Uri.UnescapeDataString(source), fileDirectory, directory);
                if (imagePath is null)
                {
                    problems.Add($"\"{title}\": the image \"{source}\" was not found.");
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(imagePath);
                    var added = await attachments.AddAsync(noteId, Path.GetFileName(imagePath), null, stream, cancellationToken);
                    replacements[source] = "attachments/" + added.StoredFileName;
                    counts[3]++;
                }
                catch (Exception ex) when (ex is DomainException or IOException)
                {
                    problems.Add($"\"{title}\": the image \"{source}\" was not added: {ex.Message}");
                }
            }

            foreach (var (from, to) in replacements)
            {
                html = html.Replace($"src=\"{from}\"", $"src=\"{to}\"", StringComparison.Ordinal);
            }

            // Relative links to other imported files become note links.
            html = RelativeLink().Replace(html, m =>
            {
                var href = Uri.UnescapeDataString(m.Groups[1].Value);
                var target = ResolveNote(href, fileDirectory);
                return target is { } id ? $"href=\"note:{id:D}\"" : m.Value;
            });

            var node = await context.Nodes.Include(n => n.Note).FirstAsync(n => n.Id == noteId, cancellationToken);
            node.Edit(node.Name, sanitizer.Sanitize(html), File.GetLastWriteTimeUtc(file));
            await context.SaveChangesAsync(cancellationToken);
            await SearchIndex.IndexNoteAsync(context, node.Id, node.Name, node.Note!.Content, cancellationToken);
            counts[5] += await NoteLinkSync.SyncAsync(context, node.Id, node.Note.Content, now, cancellationToken);

            foreach (var tag in frontTags.Concat(InlineTags(body)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await tags.AddToNoteAsync(noteId, tag, cancellationToken);
                    counts[4]++;
                }
                catch (DomainException)
                {
                    // A tag that is not a valid tag name is skipped quietly; the text still holds it.
                }
            }
        }

        watch.Stop();
        logger.LogInformation("Markdown folder {Directory} imported: {Notes} notes in {Duration}", directory, counts[2], watch.Elapsed);
        return new ImportReport([notebook.Id], counts[0], counts[1], counts[2], counts[3], counts[4], counts[5], problems, watch.Elapsed);

        async Task<Guid?> FolderForAsync(string relative)
        {
            if (relative.Length == 0 || relative == ".")
            {
                return null;
            }

            if (folderIds.TryGetValue(relative, out var known))
            {
                return known;
            }

            var parent = await FolderForAsync(Path.GetDirectoryName(relative) ?? string.Empty);
            var folder = Node.CreateFolder(notebook.Id, parent, Path.GetFileName(relative), now, folderIds.Count);
            context.Nodes.Add(folder);
            await context.SaveChangesAsync(cancellationToken);
            folderIds[relative] = folder.Id;
            counts[1]++;
            return folder.Id;
        }

        Guid? ResolveNote(string target, string fromDirectory)
        {
            var clean = target.Split('#')[0].Split('|')[0].Trim();
            if (clean.Length == 0)
            {
                return null;
            }

            var candidates = new List<string> { clean };
            if (!NoteExtensions.Contains(Path.GetExtension(clean), StringComparer.OrdinalIgnoreCase))
            {
                candidates.AddRange(NoteExtensions.Select(e => clean + e));
            }

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(Path.Combine(fromDirectory, candidate));
                if (noteByPath.TryGetValue(full, out var id))
                {
                    return id;
                }
            }

            return noteByTitle.TryGetValue(Path.GetFileNameWithoutExtension(clean), out var byTitle) ? byTitle : null;
        }
    }

    private static string? ResolveFile(string source, string fromDirectory, string root)
    {
        var direct = Path.GetFullPath(Path.Combine(fromDirectory, source));
        if (File.Exists(direct) && direct.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        // Obsidian writes just the file name and finds it anywhere in the vault.
        var name = Path.GetFileName(source);
        return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>Takes a YAML front matter block off the top and reads its tags, if any.</summary>
    private static (string Body, List<string> Tags) StripFrontMatter(string text)
    {
        var tags = new List<string>();
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return (text, tags);
        }

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return (text, tags);
        }

        var front = text[3..end];
        var tagsMatch = FrontMatterTags().Match(front);
        if (tagsMatch.Success)
        {
            tags.AddRange(tagsMatch.Groups[1].Value.Split([',', '[', ']', '\n', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length > 0));
        }

        var rest = text[(end + 4)..];
        return (rest.TrimStart('-').TrimStart('\r', '\n'), tags);
    }

    private static IEnumerable<string> InlineTags(string body) => InlineTag().Matches(body).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"!?\[\[(?<target>[^\]\|#]+)(?:#[^\]\|]*)?(?:\|(?<label>[^\]]+))?\]\]")]
    private static partial Regex WikiLink();

    [GeneratedRegex(@"<img[^>]*\ssrc=""([^""]+)""")]
    private static partial Regex ImageSource();

    [GeneratedRegex(@"href=""((?!https?:|mailto:|note:|#)[^""]+\.(?:md|markdown|txt))(?:#[^""]*)?""", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeLink();

    [GeneratedRegex(@"(?m)^tags?:\s*(.+?)(?=^\w+:|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterTags();

    [GeneratedRegex(@"(?<![\w/&])#([\p{L}\p{N}_][\p{L}\p{N}_\-/]{1,40})(?![\p{L}\p{N}])")]
    private static partial Regex InlineTag();
}
