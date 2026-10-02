namespace WorriorVex.Application.Search;

/// <summary>
/// What the user typed in the search box, taken apart: words and phrases to look for, and filters.
/// Deliberately small: no boolean operators, no nesting.
/// </summary>
public sealed record SearchQuery(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Places,
    bool FavoritesOnly,
    bool PinnedOnly)
{
    private const int MaxParts = 12;

    /// <summary>True when there is nothing to search for or filter on.</summary>
    public bool IsEmpty => Terms.Count == 0 && Tags.Count == 0 && Places.Count == 0 && !FavoritesOnly && !PinnedOnly;

    public static SearchQuery Parse(string? text)
    {
        var terms = new List<string>();
        var tags = new List<string>();
        var places = new List<string>();
        var favorites = false;
        var pinned = false;

        foreach (var (prefix, value) in Split(text ?? string.Empty).Take(MaxParts))
        {
            switch (prefix)
            {
                case "tag":
                    if (value.Length > 0)
                    {
                        tags.Add(value.TrimStart('#'));
                    }

                    break;
                case "in":
                    if (value.Length > 0)
                    {
                        places.Add(value);
                    }

                    break;
                case "is" when value.Equals("favorite", StringComparison.OrdinalIgnoreCase) || value.Equals("favourite", StringComparison.OrdinalIgnoreCase):
                    favorites = true;
                    break;
                case "is" when value.Equals("pinned", StringComparison.OrdinalIgnoreCase):
                    pinned = true;
                    break;
                case null:
                    if (value.Any(char.IsLetterOrDigit))
                    {
                        terms.Add(value);
                    }

                    break;
                default:
                    // Not a filter we know, so it is just something to look for, colon and all.
                    if (value.Any(char.IsLetterOrDigit))
                    {
                        terms.Add(prefix + ":" + value);
                    }

                    break;
            }
        }

        return new SearchQuery(terms, [.. tags.Where(t => t.Any(char.IsLetterOrDigit))], places, favorites, pinned);
    }

    /// <summary>Cuts the text at spaces, keeping "quoted phrases" together, and separates a leading <c>word:</c>.</summary>
    private static IEnumerable<(string? Prefix, string Value)> Split(string text)
    {
        var position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            string? prefix = null;
            var start = position;
            while (position < text.Length && char.IsLetter(text[position]))
            {
                position++;
            }

            if (position > start && position < text.Length && text[position] == ':')
            {
                prefix = text[start..position].ToLowerInvariant();
                position++;
            }
            else
            {
                position = start;
            }

            string value;
            if (position < text.Length && text[position] == '"')
            {
                var close = text.IndexOf('"', position + 1);
                var end = close < 0 ? text.Length : close;
                value = text[(position + 1)..end];
                position = Math.Min(end + 1, text.Length);
            }
            else
            {
                var end = position;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                value = text[position..end];
                position = end;
            }

            yield return (prefix, value.Trim());
        }
    }
}
