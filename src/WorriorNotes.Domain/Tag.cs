namespace WorriorNotes.Domain;

/// <summary>A label that can be put on any number of notes. Names are unique without regard to case.</summary>
public sealed class Tag
{
    public const int MaxNameLength = 50;

    private Tag() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Lower-case form of <see cref="Name"/>, used to find and compare tags.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tag Create(string name, DateTimeOffset now)
    {
        var clean = CleanName(name);
        return new Tag
        {
            Id = Guid.NewGuid(),
            Name = clean,
            NormalizedName = Normalize(clean),
            CreatedAt = now,
        };
    }

    public void Rename(string name)
    {
        var clean = CleanName(name);
        Name = clean;
        NormalizedName = Normalize(clean);
    }

    /// <summary>The form under which a tag name is looked up: trimmed, without a leading #, lower case.</summary>
    public static string NormalizeName(string name) => Normalize(CleanName(name));

    private static string CleanName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim().TrimStart('#').Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("A tag needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainException($"A tag can be at most {MaxNameLength} characters.");
        }

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or '#'))
        {
            throw new DomainException("A tag cannot contain spaces, commas or #.");
        }

        return trimmed;
    }

    private static string Normalize(string cleanName) => cleanName.ToLowerInvariant();
}
