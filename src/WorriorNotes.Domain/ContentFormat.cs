namespace WorriorNotes.Domain;

/// <summary>How <see cref="Note.Content"/> is encoded. HTML first; others can be added later.</summary>
public enum ContentFormat
{
    Html = 0,
    Markdown = 1,
}
