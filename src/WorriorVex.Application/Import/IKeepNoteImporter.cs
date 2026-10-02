namespace WorriorVex.Application.Import;

/// <summary>What a look at a KeepNote notebook folder found, before anything is imported.</summary>
public sealed record KeepNoteScan(
    string Path,
    string NotebookName,
    int FormatVersion,
    int Folders,
    int Pages,
    int PagesWithChildren,
    int Attachments,
    int Images,
    int ItemsInTrash,
    int Links,
    IReadOnlyDictionary<string, int> UnsupportedAttributes,
    IReadOnlyList<string> Problems);

/// <summary>What an import did. Everything counted here now exists in WorriorVex.</summary>
public sealed record KeepNoteImportReport(
    Guid NotebookId,
    string NotebookName,
    int Folders,
    int Notes,
    int Attachments,
    int Images,
    int ItemsInTrash,
    int Links,
    int UnresolvedLinks,
    IReadOnlyDictionary<string, int> IgnoredAttributes,
    IReadOnlyList<string> Problems,
    TimeSpan Duration);

/// <summary>The folder is not a KeepNote notebook this importer can read.</summary>
public sealed class KeepNoteImportException(string message) : Exception(message);

/// <summary>
/// Brings a KeepNote notebook (a folder with node.xml files) into WorriorVex as a new notebook.
/// The source folder is only ever read.
/// </summary>
public interface IKeepNoteImporter
{
    /// <summary>Reads the notebook and counts what is in it, without changing anything.</summary>
    Task<KeepNoteScan> ScanAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Imports the notebook. <paramref name="progress"/> receives a line now and then saying what is being done.</summary>
    Task<KeepNoteImportReport> ImportAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
