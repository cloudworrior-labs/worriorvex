using System.Net;
using System.Text.RegularExpressions;

namespace WorriorVex.UI;

/// <summary>Word counts for the status bar, from the note's HTML. Good enough for a glance, not for a contract.</summary>
public static partial class TextStats
{
    private const int WordsPerMinute = 220;

    public static int CountWords(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return 0;
        }

        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        return Words().Matches(text).Count;
    }

    /// <summary>Reading time in whole minutes, at least one when there is anything to read.</summary>
    public static int ReadingMinutes(int words) => words == 0 ? 0 : Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute));

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")]
    private static partial Regex Words();
}
