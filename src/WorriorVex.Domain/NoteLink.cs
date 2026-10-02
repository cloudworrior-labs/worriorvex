namespace WorriorVex.Domain;

/// <summary>A link from one note to another. Read in reverse it is a backlink.</summary>
public sealed class NoteLink
{
    private NoteLink() { }

    public Guid Id { get; private set; }
    public Guid SourceNoteId { get; private set; }
    public Guid TargetNoteId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static NoteLink Create(Guid sourceNoteId, Guid targetNoteId, DateTimeOffset now)
    {
        if (sourceNoteId == targetNoteId)
        {
            throw new DomainException("A note cannot link to itself.");
        }

        return new NoteLink
        {
            Id = Guid.NewGuid(),
            SourceNoteId = sourceNoteId,
            TargetNoteId = targetNoteId,
            CreatedAt = now,
        };
    }
}
