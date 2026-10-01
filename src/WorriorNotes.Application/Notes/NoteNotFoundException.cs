namespace WorriorNotes.Application.Notes;

public sealed class NoteNotFoundException(Guid id) : Exception($"Note {id} was not found.")
{
    public Guid NoteId { get; } = id;
}
