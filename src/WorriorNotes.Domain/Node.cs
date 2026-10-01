namespace WorriorNotes.Domain;

/// <summary>
/// One item in a notebook tree: a folder or a note. The title, position and
/// lifecycle live here; a note's body lives in <see cref="Note"/>.
/// </summary>
public sealed class Node
{
    public const int MaxNameLength = 500;
    public const string DefaultNoteName = "Untitled";

    private Node() { }

    public Guid Id { get; private set; }
    public Guid NotebookId { get; private set; }
    public Guid? ParentId { get; private set; }
    public NodeType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public Note? Note { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Node CreateFolder(Guid notebookId, Guid? parentId, string name, DateTimeOffset now, int sortOrder = 0)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainException("A folder needs a name.");
        }

        return Create(notebookId, parentId, NodeType.Folder, trimmed, now, sortOrder);
    }

    public static Node CreateNote(Guid notebookId, Guid? parentId, string? title, string? content, DateTimeOffset now, int sortOrder = 0)
    {
        var node = Create(notebookId, parentId, NodeType.Note, NormalizeNoteName(title), now, sortOrder);
        node.Note = Note.Create(node.Id, content);
        return node;
    }

    public void Rename(string? name, DateTimeOffset now)
    {
        var normalized = Type == NodeType.Note ? NormalizeNoteName(name) : name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new DomainException("A folder needs a name.");
        }

        EnsureLength(normalized);
        if (normalized == Name)
        {
            return;
        }

        Name = normalized;
        UpdatedAt = now;
    }

    /// <summary>Replaces a note's title and body. Does nothing to timestamps if nothing changed.</summary>
    public void Edit(string? title, string? content, DateTimeOffset now)
    {
        if (Type != NodeType.Note || Note is null)
        {
            throw new DomainException("Only a note has content to edit.");
        }

        if (IsDeleted)
        {
            throw new DomainException("A note in the trash cannot be edited. Restore it first.");
        }

        var normalized = NormalizeNoteName(title);
        EnsureLength(normalized);
        var contentChanged = Note.SetContent(content);
        if (!contentChanged && normalized == Name)
        {
            return;
        }

        Name = normalized;
        UpdatedAt = now;
    }

    public void MoveToTrash(DateTimeOffset now)
    {
        DeletedAt ??= now;
    }

    public void Restore()
    {
        DeletedAt = null;
    }

    private static Node Create(Guid notebookId, Guid? parentId, NodeType type, string name, DateTimeOffset now, int sortOrder)
    {
        if (notebookId == Guid.Empty)
        {
            throw new DomainException("A node must belong to a notebook.");
        }

        EnsureLength(name);
        return new Node
        {
            Id = Guid.NewGuid(),
            NotebookId = notebookId,
            ParentId = parentId,
            Type = type,
            Name = name,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static string NormalizeNoteName(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? DefaultNoteName : trimmed;
    }

    private static void EnsureLength(string name)
    {
        if (name.Length > MaxNameLength)
        {
            throw new DomainException($"A name can be at most {MaxNameLength} characters.");
        }
    }
}
