namespace WorriorVex.Application.Export;

public enum ExportFormat
{
    /// <summary>A self-contained web page per note, pictures embedded.</summary>
    Html,

    /// <summary>Markdown per note, pictures and files in a folder beside it.</summary>
    Markdown,

    /// <summary>JSON with everything needed to rebuild the note: text, tags, links, attachments, history.</summary>
    Json,
}

public sealed record ExportResult(string Path, int Notes, int Files, IReadOnlyList<string> Problems);

/// <summary>Takes notes out of WorriorVex in formats other programs read. Never changes anything inside.</summary>
public interface IExportService
{
    /// <summary>Writes one note to a file in the given format. Pictures and files go beside it where the format needs that.</summary>
    Task<ExportResult> ExportNoteAsync(Guid noteId, ExportFormat format, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every note outside the trash into a folder, one file per note, in the notebook and folder
    /// structure, with links between notes pointing at the exported files.
    /// </summary>
    Task<ExportResult> ExportAllAsync(ExportFormat format, string destinationDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a portable <c>.worriorvex</c> package: a zip with a manifest, every notebook, folder, note,
    /// tag, link, revision and attachment file. Enough to rebuild the whole workspace elsewhere.
    /// </summary>
    Task<ExportResult> ExportPackageAsync(string destinationPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>The file extension for a format, with its dot.</summary>
    static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Html => ".html",
        ExportFormat.Markdown => ".md",
        _ => ".json",
    };
}
