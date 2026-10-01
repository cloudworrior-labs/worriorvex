namespace WorriorNotes.Domain;

/// <summary>The body of a note. Shares its identity with the <see cref="Node"/> that places it in the tree.</summary>
public sealed class Note
{
    private Note() { }

    public Guid NodeId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public ContentFormat ContentFormat { get; private set; }

    internal static Note Create(Guid nodeId, string? content) => new()
    {
        NodeId = nodeId,
        Content = content ?? string.Empty,
        ContentFormat = ContentFormat.Html,
    };

    /// <returns><c>true</c> when the content actually changed.</returns>
    internal bool SetContent(string? content)
    {
        var value = content ?? string.Empty;
        if (value == Content)
        {
            return false;
        }

        Content = value;
        return true;
    }
}
