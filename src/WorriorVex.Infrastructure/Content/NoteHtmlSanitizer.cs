using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using WorriorVex.Application.Content;

namespace WorriorVex.Infrastructure.Content;

/// <summary>
/// Allow-list sanitiser for note HTML. The list is what the editor itself produces; anything else
/// is dropped. Links may only point to http(s) addresses and images only to the note's attachments,
/// so opening a note never makes the app fetch anything from the network.
/// </summary>
public sealed partial class NoteHtmlSanitizer : INoteHtmlSanitizer
{
    private static readonly string[] Tags =
    [
        "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
        "strong", "b", "em", "i", "u", "s", "strike", "del", "code", "pre", "blockquote", "span", "div",
        "ul", "ol", "li", "label", "input",
        "a", "img",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "colgroup", "col",
    ];

    private static readonly string[] Attributes =
    [
        "href", "target", "rel", "src", "alt", "title", "width", "height",
        "colspan", "rowspan", "colwidth", "start", "type", "checked", "disabled", "class",
    ];

    private readonly HtmlSanitizer _sanitizer;

    public NoteHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(Attributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(["http", "https", "note"], StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href", "src"], StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        })
        {
            // data-type and data-checked carry the check lists; data attributes do nothing by themselves.
            AllowDataAttributes = true,
            KeepChildNodes = false,
        };
        _sanitizer.PostProcessNode += (_, e) => Restrict(e.Node);
    }

    public string Sanitize(string? html) =>
        string.IsNullOrEmpty(html) ? string.Empty : _sanitizer.Sanitize(html);

    /// <summary>Rules that depend on the element, applied after the general allow-list.</summary>
    private static void Restrict(INode node)
    {
        if (node is not IElement element)
        {
            return;
        }

        switch (element.LocalName)
        {
            case "img":
                if (!NoteContentRules.IsAttachmentImageSource(element.GetAttribute("src")))
                {
                    element.Remove();
                    return;
                }

                break;

            case "a":
                var href = element.GetAttribute("href");
                if (NoteContentRules.IsWebLink(href))
                {
                    element.SetAttribute("target", "_blank");
                    element.SetAttribute("rel", "noopener noreferrer nofollow");
                }
                else if (NoteContentRules.NoteLinkTarget(href) is { } target)
                {
                    // A link to another note: stored in one canonical form, opened by the app, never by the browser.
                    element.SetAttribute("href", NoteContentRules.NoteLinkAddress(target));
                    element.RemoveAttribute("target");
                    element.RemoveAttribute("rel");
                }
                else
                {
                    // Not a web address: keep the words, drop the link.
                    element.RemoveAttribute("href");
                    element.RemoveAttribute("target");
                    element.RemoveAttribute("rel");
                }

                break;

            case "input":
                // Only the tick box of a check list item; it is never a way to enter anything.
                if (!string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                {
                    element.Remove();
                    return;
                }

                break;
        }

        if (element.LocalName != "a")
        {
            element.RemoveAttribute("href");
            element.RemoveAttribute("target");
            element.RemoveAttribute("rel");
        }

        if (element.LocalName != "img")
        {
            element.RemoveAttribute("src");
        }

        if (element.GetAttribute("class") is { } classes)
        {
            // The only class with meaning is the language of a code block.
            var kept = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(c => CodeLanguageClass().IsMatch(c)).ToArray();
            if (kept.Length > 0 && element.LocalName == "code")
            {
                element.SetAttribute("class", string.Join(' ', kept));
            }
            else
            {
                element.RemoveAttribute("class");
            }
        }
    }

    [GeneratedRegex(@"^language-[A-Za-z0-9+#.\-]{1,30}$")]
    private static partial Regex CodeLanguageClass();
}
