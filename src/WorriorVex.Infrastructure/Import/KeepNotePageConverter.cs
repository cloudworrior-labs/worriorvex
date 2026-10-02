using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WorriorVex.Application.Content;

namespace WorriorVex.Infrastructure.Import;

/// <summary>
/// Turns a KeepNote page.html into note HTML. KeepNote writes line-based text with &lt;br&gt; between
/// lines and a small set of inline tags; WorriorVex notes are made of paragraphs and blocks.
/// Images and files referenced by the page are reported back so the importer can attach them.
/// </summary>
internal static partial class KeepNotePageConverter
{
    private static readonly HashSet<string> KeptTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "u", "s", "code", "pre", "blockquote",
        "ul", "ol", "li", "a", "img", "table", "thead", "tbody", "tr", "th", "td", "div", "span",
    };

    private static readonly Dictionary<string, string> RenamedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "strong",
        ["i"] = "em",
        ["strike"] = "s",
        ["del"] = "s",
        ["tt"] = "code",
        ["center"] = "div",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "blockquote", "pre", "table",
    };

    /// <summary>An image the page refers to by a file name relative to the page's folder.</summary>
    public sealed record ImageReference(string FileName, IElement Element);

    /// <summary>A converted page. Set each image's <c>src</c> through its element, then read <see cref="Html"/>.</summary>
    public sealed class ConvertedPage(IElement container, IReadOnlyList<ImageReference> images)
    {
        public IReadOnlyList<ImageReference> Images { get; } = images;

        public string Html => container.InnerHtml;
    }

    public static ConvertedPage Convert(string pageHtml)
    {
        var document = new HtmlParser().ParseDocument(pageHtml);
        var body = document.Body ?? document.DocumentElement;

        Normalize(body);
        var container = Paragraphize(document, body);

        var images = new List<ImageReference>();
        foreach (var img in container.QuerySelectorAll("img").ToList())
        {
            var source = Uri.UnescapeDataString(img.GetAttribute("src") ?? string.Empty).Trim();
            if (source.Length == 0 || source.Contains("://", StringComparison.Ordinal) || source.Contains("..", StringComparison.Ordinal) || source.Contains('/') || source.Contains('\\'))
            {
                img.Remove();
                continue;
            }

            if (string.IsNullOrEmpty(img.GetAttribute("alt")))
            {
                img.SetAttribute("alt", Path.GetFileNameWithoutExtension(source));
            }

            images.Add(new ImageReference(source, img));
        }

        return new ConvertedPage(container, images);
    }

    /// <summary>Renames or unwraps tags WorriorVex does not keep, so no text is lost to the sanitiser.</summary>
    private static void Normalize(IElement element)
    {
        foreach (var child in element.Children.ToList())
        {
            Normalize(child);
            var name = child.LocalName;
            if (RenamedTags.TryGetValue(name, out var renamed))
            {
                var replacement = element.Owner!.CreateElement(renamed);
                foreach (var grandchild in child.ChildNodes.ToList())
                {
                    replacement.AppendChild(grandchild);
                }

                child.Replace(replacement);
            }
            else if (name is "script" or "style" or "head" or "title" or "meta" or "link")
            {
                child.Remove();
            }
            else if (!KeptTags.Contains(name))
            {
                // Unknown wrapper: keep what it contains, drop the wrapper.
                var parent = child.ParentElement!;
                foreach (var grandchild in child.ChildNodes.ToList())
                {
                    parent.InsertBefore(grandchild, child);
                }

                child.Remove();
            }
        }
    }

    /// <summary>
    /// KeepNote puts text and &lt;br&gt;s straight into the body. Here each line becomes a paragraph,
    /// and blocks that already are blocks stay as they are.
    /// </summary>
    private static IElement Paragraphize(IDocument document, IElement body)
    {
        var container = document.CreateElement("div");
        container.SetAttribute("data-wv-root", "1");
        IElement? paragraph = null;

        void Flush()
        {
            if (paragraph is not null)
            {
                if (paragraph.ChildNodes.Length == 0 || string.IsNullOrWhiteSpace(paragraph.TextContent) && !paragraph.QuerySelectorAll("img").Any())
                {
                    paragraph.TextContent = string.Empty;
                }

                container.AppendChild(paragraph);
                paragraph = null;
            }
        }

        foreach (var node in body.ChildNodes.ToList())
        {
            switch (node)
            {
                case IElement { LocalName: "br" }:
                    paragraph ??= document.CreateElement("p");
                    Flush();
                    break;
                case IElement element when BlockTags.Contains(element.LocalName):
                    Flush();
                    container.AppendChild(element);
                    break;
                case IText text when text.Data.Trim().Length == 0 && paragraph is null:
                    break;
                default:
                    paragraph ??= document.CreateElement("p");
                    paragraph.AppendChild(node);
                    break;
            }
        }

        Flush();
        document.Body!.AppendChild(container);
        return container;
    }

    /// <summary>Rewrites KeepNote's nbk:// links to WorriorVex note links; links to unknown nodes keep their words only.</summary>
    public static string RewriteLinks(string html, IReadOnlyDictionary<string, Guid> noteIdsByKeepNoteId, out int resolved, out int unresolved)
    {
        var found = 0;
        var missing = 0;
        var rewritten = NbkLink().Replace(html, match =>
        {
            var nodeId = match.Groups[2].Value;
            if (noteIdsByKeepNoteId.TryGetValue(nodeId, out var noteId))
            {
                found++;
                return $"href=\"{NoteContentRules.NoteLinkAddress(noteId)}\"";
            }

            missing++;
            return string.Empty;
        });
        resolved = found;
        unresolved = missing;
        return rewritten;
    }

    [GeneratedRegex(@"href=""nbk://([^/""]*)/([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex NbkLink();
}
