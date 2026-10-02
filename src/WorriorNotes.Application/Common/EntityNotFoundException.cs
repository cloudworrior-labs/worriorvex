namespace WorriorNotes.Application.Common;

/// <summary>Something the caller referred to by id does not exist (any more).</summary>
public class EntityNotFoundException(string kind, Guid id) : Exception($"{kind} {id} was not found.")
{
    public string Kind { get; } = kind;
    public Guid Id { get; } = id;
}
