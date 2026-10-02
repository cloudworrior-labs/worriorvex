using WorriorVex.Application.Common;

namespace WorriorVex.Application.Notes;

public sealed class NoteNotFoundException(Guid id) : EntityNotFoundException("Note", id)
{
    public Guid NoteId => Id;
}
