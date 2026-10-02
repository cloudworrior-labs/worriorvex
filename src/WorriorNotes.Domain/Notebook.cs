namespace WorriorNotes.Domain;

/// <summary>A top-level container for a tree of folders and notes.</summary>
public sealed class Notebook
{
    public const int MaxNameLength = 200;
    public const string InboxName = "Inbox";

    private Notebook() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public NotebookKind Kind { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Notebook Create(string name, DateTimeOffset now, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = ValidateName(name),
        Kind = NotebookKind.User,
        SortOrder = sortOrder,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static Notebook CreateInbox(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = InboxName,
        Kind = NotebookKind.Inbox,
        SortOrder = int.MinValue,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string name, DateTimeOffset now)
    {
        if (Kind == NotebookKind.Inbox)
        {
            throw new DomainException("The Inbox cannot be renamed.");
        }

        Name = ValidateName(name);
        UpdatedAt = now;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void MoveToTrash(DateTimeOffset now)
    {
        if (Kind == NotebookKind.Inbox)
        {
            throw new DomainException("The Inbox cannot be deleted.");
        }

        DeletedAt ??= now;
    }

    public void Restore()
    {
        DeletedAt = null;
    }

    private static string ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainException("A notebook needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"A notebook name can be at most {MaxNameLength} characters.");
        }

        return trimmed;
    }
}
