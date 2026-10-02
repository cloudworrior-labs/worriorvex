namespace WorriorVex.Domain;

/// <summary>Says that a note carries a tag.</summary>
public sealed class NoteTag
{
    private NoteTag() { }

    public Guid NoteId { get; private set; }
    public Guid TagId { get; private set; }

    public static NoteTag Create(Guid noteId, Guid tagId) => new() { NoteId = noteId, TagId = tagId };
}
