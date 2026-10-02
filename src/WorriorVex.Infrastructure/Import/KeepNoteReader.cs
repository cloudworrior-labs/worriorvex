using System.Globalization;
using System.Xml.Linq;
using WorriorVex.Application.Import;

namespace WorriorVex.Infrastructure.Import;

/// <summary>
/// Reads the on-disk structure of a KeepNote notebook (format versions 5 and 6): a tree of folders,
/// each with a node.xml. See docs/keepnote-analysis.md for the format.
/// </summary>
internal static class KeepNoteReader
{
    public const string NodeFile = "node.xml";
    public const string PageFile = "page.html";
    public const string PreferencesFile = "notebook.nbk";
    public const string ContentTypePage = "text/xhtml+xml";
    public const string ContentTypeFolder = "application/x-notebook-dir";
    public const string ContentTypeTrash = "application/x-notebook-trash";

    /// <summary>Attributes KeepNote defines that WorriorVex has a place for.</summary>
    private static readonly HashSet<string> UsedAttributes =
    [
        "nodeid", "content_type", "title", "order", "created_time", "modified_time", "payload_filename", "version",
    ];

    public sealed class KeepNode
    {
        public required string Directory { get; init; }
        public required Dictionary<string, string> Attributes { get; init; }
        public List<KeepNode> Children { get; } = [];

        public string NodeId => Attributes.GetValueOrDefault("nodeid", string.Empty);
        public string ContentType => Attributes.GetValueOrDefault("content_type", ContentTypeFolder);
        public string Title => Attributes.GetValueOrDefault("title") is { Length: > 0 } title ? title : Path.GetFileName(Directory);
        public bool IsPage => ContentType == ContentTypePage;
        public bool IsFolder => ContentType == ContentTypeFolder;
        public bool IsTrash => ContentType == ContentTypeTrash;
        public bool IsAttachment => !IsPage && !IsFolder && !IsTrash;
        public string? PayloadFileName => Attributes.GetValueOrDefault("payload_filename");
        public int Order => int.TryParse(Attributes.GetValueOrDefault("order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ? order : int.MaxValue;
        public DateTimeOffset? Created => Timestamp("created_time");
        public DateTimeOffset? Modified => Timestamp("modified_time");

        public IEnumerable<string> UnsupportedAttributes => Attributes.Keys.Where(k => !UsedAttributes.Contains(k));

        private DateTimeOffset? Timestamp(string key) =>
            long.TryParse(Attributes.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }

    public sealed record KeepNotebook(KeepNode Root, int Version, string Name, IReadOnlyList<string> Problems);

    public static KeepNotebook Read(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new KeepNoteImportException("That folder does not exist.");
        }

        var rootFile = Path.Combine(path, NodeFile);
        if (!File.Exists(rootFile))
        {
            throw new KeepNoteImportException($"That folder is not a KeepNote notebook: it has no {NodeFile}. Choose the notebook's own folder, the one that also holds {PreferencesFile}.");
        }

        var problems = new List<string>();
        var version = ReadVersion(path, rootFile);
        if (version is < 5 or > 6)
        {
            throw new KeepNoteImportException(version < 5
                ? $"This notebook uses KeepNote format version {version}. Open it once in KeepNote, which upgrades it, then import it again."
                : $"This notebook uses KeepNote format version {version}, which is newer than this importer knows.");
        }

        var root = ReadNode(path, problems)
            ?? throw new KeepNoteImportException($"The notebook's {NodeFile} could not be read.");
        var name = root.Attributes.GetValueOrDefault("title") is { Length: > 0 } title ? title : Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
        return new KeepNotebook(root, version, name, problems);
    }

    private static int ReadVersion(string path, string rootFile)
    {
        var preferences = Path.Combine(path, PreferencesFile);
        foreach (var file in new[] { preferences, rootFile })
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var version = XDocument.Load(file).Root?.Element("version")?.Value;
                if (int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    return number;
                }
            }
            catch (System.Xml.XmlException)
            {
                // Try the other file.
            }
        }

        return 6;
    }

    /// <summary>Reads a node directory and, recursively, every subdirectory that has a node.xml.</summary>
    private static KeepNode? ReadNode(string directory, List<string> problems)
    {
        Dictionary<string, string> attributes;
        try
        {
            attributes = ReadAttributes(Path.Combine(directory, NodeFile));
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"\"{Path.GetFileName(directory)}\": its {NodeFile} could not be read ({ex.Message}). Skipped with everything inside it.");
            return null;
        }

        var node = new KeepNode { Directory = directory, Attributes = attributes };
        IEnumerable<string> subdirectories;
        try
        {
            subdirectories = Directory.EnumerateDirectories(directory).Where(d => File.Exists(Path.Combine(d, NodeFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"\"{node.Title}\": its folder could not be listed ({ex.Message}).");
            return node;
        }

        foreach (var child in subdirectories.Select(d => ReadNode(d, problems)).Where(c => c is not null).OrderBy(c => c!.Order).ThenBy(c => c!.Title, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(child!);
        }

        return node;
    }

    /// <summary>Version 6 keeps a property-list dictionary; version 5 flat &lt;attr key="…"&gt; elements.</summary>
    private static Dictionary<string, string> ReadAttributes(string file)
    {
        var root = XDocument.Load(file).Root ?? throw new System.Xml.XmlException("Empty document");
        if (root.Name.LocalName != "node")
        {
            throw new System.Xml.XmlException("The root element is not <node>");
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.Element("version")?.Value is { } version)
        {
            attributes["version"] = version;
        }

        var dict = root.Element("dict");
        if (dict is not null)
        {
            string? key = null;
            foreach (var element in dict.Elements())
            {
                if (element.Name.LocalName == "key")
                {
                    key = element.Value;
                }
                else if (key is not null)
                {
                    attributes[key] = element.Name.LocalName switch
                    {
                        "true" => "1",
                        "false" => "0",
                        _ => element.Value,
                    };
                    key = null;
                }
            }
        }

        foreach (var attr in root.Elements("attr"))
        {
            if (attr.Attribute("key")?.Value is { Length: > 0 } key)
            {
                attributes[key] = attr.Value;
            }
        }

        return attributes;
    }
}
