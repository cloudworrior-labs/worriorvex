using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace WorriorVex.Infrastructure.Content;

/// <summary>The words of a note without its markup, for searching and previews.</summary>
public static class NoteText
{
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "hr", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "table", "tr", "td", "th",
    };

    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var text = new StringBuilder(html.Length);
        Append(document.Body!, text);
        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Append(INode node, StringBuilder text)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText words:
                    text.Append(words.Data);
                    break;
                case IElement element when element.LocalName is "script" or "style":
                    break;
                case IElement element:
                    var isBlock = Blocks.Contains(element.LocalName);
                    if (isBlock)
                    {
                        text.Append(' ');
                    }

                    if (element.LocalName == "img" && element.GetAttribute("alt") is { Length: > 0 } alt)
                    {
                        text.Append(' ').Append(alt).Append(' ');
                    }

                    Append(element, text);
                    if (isBlock)
                    {
                        text.Append(' ');
                    }

                    break;
            }
        }
    }
}
