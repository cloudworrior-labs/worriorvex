namespace WorriorVex.Application.Content;

/// <summary>
/// Reduces HTML to what a note may contain. Everything that is stored passes through it, whatever
/// its source: the editor, a paste, an import or a restored backup.
/// </summary>
public interface INoteHtmlSanitizer
{
    /// <summary>
    /// Returns the HTML with scripts, event handlers, styles, embedded content, unsafe addresses and
    /// images that are not the note's own attachments removed. Sanitising the result again changes nothing.
    /// </summary>
    string Sanitize(string? html);
}
