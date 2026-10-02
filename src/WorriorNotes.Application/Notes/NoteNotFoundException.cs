using WorriorNotes.Application.Common;

namespace WorriorNotes.Application.Notes;

public sealed class NoteNotFoundException(Guid id) : EntityNotFoundException("Note", id)
{
    public Guid NoteId => Id;
}
