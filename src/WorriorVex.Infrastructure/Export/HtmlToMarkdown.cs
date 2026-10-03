using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace WorriorVex.Infrastructure.Export;

/// <summary>
/// Turns note HTML (the sanitised subset the editor writes) into Markdown. Links and image sources
/// are passed through a function so the caller decides where they point in the export.
/// </summary>
internal static class HtmlToMarkdown
{
    public static string Convert(string html, Func<string, string?> rewriteHref, Func<string, string?> rewriteSrc)
    {
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var writer = new Writer(rewriteHref, rewriteSrc);
        writer.Blocks(document.Body!, 0);
        return writer.ToString().Trim() + "\n";
    }

    private sealed class Writer(Func<string, string?> rewriteHref, Func<string, string?> rewriteSrc)
    {
        private readonly StringBuilder _text = new();

        public override string ToString() => _text.ToString();

        public void Blocks(IElement parent, int indent)
        {
            foreach (var node in parent.ChildNodes)
            {
                switch (node)
                {
                    case IElement element:
                        Block(element, indent);
                        break;
                    case IText text when text.Data.Trim().Length > 0:
                        Paragraph(Inline(text), indent);
                        break;
                }
            }
        }

        private void Block(IElement element, int indent)
        {
            var prefix = new string(' ', indent);
            switch (element.LocalName)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Paragraph(new string('#', element.LocalName[1] - '0') + " " + InlineChildren(element), indent);
                    break;
                case "p":
                    Paragraph(InlineChildren(element), indent);
                    break;
                case "blockquote":
                    var inner = new Writer(rewriteHref, rewriteSrc);
                    inner.Blocks(element, 0);
                    var quoted = string.Join("\n", inner.ToString().Trim().Split('\n').Select(line => prefix + "> " + line));
                    _text.Append(quoted).Append("\n\n");
                    break;
                case "pre":
                    var code = element.QuerySelector("code");
                    var language = code?.GetAttribute("class")?.Split(' ').FirstOrDefault(c => c.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..] ?? string.Empty;
                    _text.Append(prefix).Append("```").Append(language).Append('\n')
                        .Append((code ?? element).TextContent.TrimEnd('\n')).Append('\n')
                        .Append(prefix).Append("```\n\n");
                    break;
                case "ul" or "ol":
                    List(element, indent);
                    _text.Append('\n');
                    break;
                case "hr":
                    _text.Append(prefix).Append("---\n\n");
                    break;
                case "table":
                    Table(element, indent);
                    break;
                case "img":
                    Paragraph(Image(element), indent);
                    break;
                case "div" when element.GetAttribute("data-callout") is { Length: > 0 } kind:
                    // A callout becomes a quote that starts with its kind, the way Obsidian and GitHub write them.
                    var callout = new Writer(rewriteHref, rewriteSrc);
                    callout.Blocks(element, 0);
                    var label = char.ToUpperInvariant(kind[0]) + kind[1..];
                    var lines = callout.ToString().Trim().Split('\n').Select(line => prefix + "> " + line);
                    _text.Append(prefix).Append("> **").Append(label).Append("**\n").Append(string.Join("\n", lines)).Append("\n\n");
                    break;
                case "div" or "span" or "label":
                    Blocks(element, indent);
                    break;
                default:
                    Paragraph(Inline(element), indent);
                    break;
            }
        }

        private void Paragraph(string text, int indent)
        {
            if (text.Trim().Length == 0)
            {
                return;
            }

            var prefix = new string(' ', indent);
            _text.Append(string.Join("\n", text.Split('\n').Select(line => prefix + line))).Append("\n\n");
        }

        private void List(IElement list, int indent)
        {
            var ordered = list.LocalName == "ol";
            var number = int.TryParse(list.GetAttribute("start"), out var start) ? start : 1;
            foreach (var item in list.Children.Where(c => c.LocalName == "li"))
            {
                var marker = ordered ? $"{number++}. " : "- ";
                if (item.GetAttribute("data-type") == "taskItem")
                {
                    marker += item.GetAttribute("data-checked") == "true" ? "[x] " : "[ ] ";
                }

                var firstLine = true;
                var nested = new List<IElement>();
                var line = new StringBuilder();
                foreach (var child in item.ChildNodes)
                {
                    switch (child)
                    {
                        case IElement { LocalName: "ul" or "ol" } sublist:
                            nested.Add(sublist);
                            break;
                        case IElement { LocalName: "label" }:
                            break;
                        case IElement { LocalName: "div" or "p" } wrapper when wrapper.Children.Any(c => c.LocalName is "ul" or "ol"):
                            foreach (var part in wrapper.ChildNodes)
                            {
                                if (part is IElement { LocalName: "ul" or "ol" } inner)
                                {
                                    nested.Add(inner);
                                }
                                else if (part is IElement partElement)
                                {
                                    line.Append(InlineChildren(partElement));
                                }
                                else
                                {
                                    line.Append(Inline(part));
                                }
                            }

                            break;
                        case IElement { LocalName: "div" or "p" } wrapper:
                            if (line.Length > 0)
                            {
                                line.Append('\n');
                            }

                            line.Append(InlineChildren(wrapper));
                            break;
                        default:
                            line.Append(Inline(child));
                            break;
                    }
                }

                foreach (var text in line.ToString().Split('\n'))
                {
                    _text.Append(new string(' ', indent)).Append(firstLine ? marker : new string(' ', marker.Length)).Append(text.Trim()).Append('\n');
                    firstLine = false;
                }

                foreach (var sublist in nested)
                {
                    List(sublist, indent + marker.Length);
                }
            }
        }

        private void Table(IElement table, int indent)
        {
            var rows = table.QuerySelectorAll("tr").ToList();
            if (rows.Count == 0)
            {
                return;
            }

            var prefix = new string(' ', indent);
            var cells = rows.Select(r => r.Children.Where(c => c.LocalName is "td" or "th").Select(c => InlineChildren(c).Replace("\n", " ").Replace("|", "\\|").Trim()).ToList()).ToList();
            var width = cells.Max(r => r.Count);
            foreach (var row in cells)
            {
                while (row.Count < width)
                {
                    row.Add(string.Empty);
                }
            }

            _text.Append(prefix).Append("| ").Append(string.Join(" | ", cells[0])).Append(" |\n");
            _text.Append(prefix).Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", width))).Append(" |\n");
            foreach (var row in cells.Skip(1))
            {
                _text.Append(prefix).Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
            }

            _text.Append('\n');
        }

        private string InlineChildren(IElement element) => string.Concat(element.ChildNodes.Select(Inline));

        private string Inline(INode node)
        {
            switch (node)
            {
                case IText text:
                    return Escape(text.Data);
                case IElement element:
                    var inner = InlineChildren(element);
                    return element.LocalName switch
                    {
                        "strong" or "b" => Wrap(inner, "**"),
                        "em" or "i" => Wrap(inner, "*"),
                        "s" or "strike" or "del" => Wrap(inner, "~~"),
                        "mark" => Wrap(inner, "=="),
                        "code" => inner.Length == 0 ? string.Empty : "`" + element.TextContent + "`",
                        "br" => "  \n",
                        "a" => Link(element, inner),
                        "img" => Image(element),
                        "u" or "span" or "label" or "input" => inner,
                        "p" or "div" => inner + "\n",
                        _ => inner,
                    };
                default:
                    return string.Empty;
            }
        }

        private string Link(IElement anchor, string text)
        {
            var target = rewriteHref(anchor.GetAttribute("href") ?? string.Empty);
            return target is null || text.Trim().Length == 0 ? text : $"[{text}]({target})";
        }

        private string Image(IElement img)
        {
            var source = rewriteSrc(img.GetAttribute("src") ?? string.Empty);
            var alt = img.GetAttribute("alt") ?? string.Empty;
            return source is null ? (alt.Length > 0 ? $"[{alt}]" : string.Empty) : $"![{alt}]({source})";
        }

        private static string Wrap(string inner, string marker)
        {
            if (inner.Trim().Length == 0)
            {
                return inner;
            }

            var leading = inner.Length - inner.TrimStart().Length;
            var trailing = inner.Length - inner.TrimEnd().Length;
            return inner[..leading] + marker + inner.Trim() + marker + inner[(inner.Length - trailing)..];
        }

        private static string Escape(string text)
        {
            var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 0 && char.IsWhiteSpace(text[0]))
            {
                collapsed = " " + collapsed;
            }

            if (text.Length > 1 && char.IsWhiteSpace(text[^1]) && collapsed.Length > 0)
            {
                collapsed += " ";
            }

            return collapsed.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("[", "\\[").Replace("]", "\\]").Replace("#", "\\#");
        }
    }
}
