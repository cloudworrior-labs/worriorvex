using System.Text.RegularExpressions;

namespace WorriorVex.Application.Content;

/// <summary>What note content may refer to. Shared by the sanitiser, the editor host and the importer.</summary>
public static partial class NoteContentRules
{
    /// <summary>Path under which the app serves attachment files to its own window.</summary>
    public const string AttachmentPathPrefix = "attachments/";

    /// <summary>Image types that may be shown inside a note.</summary>
    public static readonly IReadOnlyDictionary<string, string> ImageContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>
    /// The file name under which an image should be attached so that it can be shown in a note, or
    /// <c>null</c> when the content type is not an image a note may contain. A pasted image often
    /// arrives with no extension or the wrong one; the content type decides.
    /// </summary>
    public static string? ImageFileName(string? fileName, string? contentType)
    {
        var type = contentType?.Trim().ToLowerInvariant();
        var extension = ImageContentTypes.FirstOrDefault(pair => pair.Value == type).Key;
        if (extension is null)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(fileName) ? "image" : fileName.Trim();
        var current = Path.GetExtension(name);
        return ImageContentTypes.TryGetValue(current, out var currentType) && currentType == type ? name : name + extension;
    }

    /// <summary>The content type of an image file a note may contain, judged by its extension, or <c>null</c>.</summary>
    public static string? ImageContentType(string? fileName) =>
        ImageContentTypes.GetValueOrDefault(Path.GetExtension(fileName ?? string.Empty));

    /// <summary>The address an image in a note uses for an attachment stored under the given file name.</summary>
    public static string AttachmentSource(string storedFileName) => AttachmentPathPrefix + storedFileName;

    /// <summary>True for the address of an attachment image and nothing else: no other path, no web address.</summary>
    public static bool IsAttachmentImageSource(string? source) =>
        source is not null && AttachmentImageSource().IsMatch(source);

    /// <summary>True for an absolute http or https address, the only kind a link in a note may have.</summary>
    public static bool IsWebLink(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex(@"^attachments/[0-9a-f]{32}\.(png|jpg|jpeg|gif|webp)$")]
    private static partial Regex AttachmentImageSource();
}
