namespace WorriorVex.Application.Import;

/// <summary>What an import of a package or a folder of files did.</summary>
public sealed record ImportReport(
    IReadOnlyList<Guid> NotebookIds,
    int Notebooks,
    int Folders,
    int Notes,
    int Attachments,
    int Tags,
    int Links,
    IReadOnlyList<string> Problems,
    TimeSpan Duration);

/// <summary>The file or folder is not something this importer can read.</summary>
public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Imports a <c>.worriorvex</c> package (Export → "Everything, as a package") made by this or another
/// copy of WorriorVex. Notebooks, folders, notes, tags, attachments and links between notes come
/// in as new items; nothing already here is changed. Trashed items are left out.
/// </summary>
public interface IPackageImporter
{
    Task<ImportReport> ImportAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Imports a folder of Markdown (and plain text) files as a new notebook: subfolders become folders,
/// each file a note, images next to the files become attachments, and <c>[[wiki links]]</c> or
/// relative links to other files become links between notes. Obsidian and Joplin folders work.
/// </summary>
public interface IMarkdownImporter
{
    Task<ImportReport> ImportAsync(string directory, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
