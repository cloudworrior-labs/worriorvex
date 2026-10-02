namespace WorriorNotes.Domain;

/// <summary>A note's title and body as they were at one moment. Never changed after it is written.</summary>
public sealed class NoteRevision
{
    public const int MaxChangeReasonLength = 200;

    private NoteRevision() { }

    public Guid Id { get; private set; }
    public Guid NoteId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public string ChangeReason { get; private set; } = string.Empty;

    public static NoteRevision Create(Guid noteId, string title, string content, DateTimeOffset now, string changeReason)
    {
        var reason = changeReason.Trim();
        return new NoteRevision
        {
            Id = Guid.NewGuid(),
            NoteId = noteId,
            Title = title,
            Content = content,
            CreatedAt = now,
            ChangeReason = reason.Length > MaxChangeReasonLength ? reason[..MaxChangeReasonLength] : reason,
        };
    }
}
