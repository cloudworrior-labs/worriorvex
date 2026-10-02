namespace WorriorNotes.Domain;

/// <summary>
/// Describes a file attached to a note. The file itself lies in the attachments folder under
/// <see cref="StoredFileName"/>, a name the application generated; the name the user gave is only data.
/// </summary>
public sealed class Attachment
{
    public const long MaxSizeBytes = 100L * 1024 * 1024;
    public const int MaxFileNameLength = 255;
    public const string DefaultFileName = "file";
    public const string DefaultContentType = "application/octet-stream";

    private Attachment() { }

    public Guid Id { get; private set; }
    public Guid NoteId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = DefaultContentType;
    public long Size { get; private set; }

    /// <summary>SHA-256 of the file, lower-case hex.</summary>
    public string Hash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Attachment Create(
        Guid id,
        Guid noteId,
        string? originalFileName,
        string? contentType,
        long size,
        string hash,
        DateTimeOffset now)
    {
        if (size < 0)
        {
            throw new DomainException("An attachment cannot have a negative size.");
        }

        if (size > MaxSizeBytes)
        {
            throw new DomainException($"An attachment can be at most {MaxSizeBytes / (1024 * 1024)} MB.");
        }

        var name = CleanFileName(originalFileName);
        return new Attachment
        {
            Id = id,
            NoteId = noteId,
            OriginalFileName = name,
            StoredFileName = id.ToString("N") + SafeExtension(name),
            ContentType = CleanContentType(contentType),
            Size = size,
            Hash = hash,
            CreatedAt = now,
        };
    }

    /// <summary>Changes the name shown to the user. The stored file keeps its generated name.</summary>
    public void Rename(string? fileName) => OriginalFileName = CleanFileName(fileName);

    /// <summary>
    /// Reduces whatever the user or another program supplied to a plain file name:
    /// no directories, no control or reserved characters, not empty, not too long.
    /// </summary>
    public static string CleanFileName(string? fileName)
    {
        var name = fileName ?? string.Empty;
        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        var kept = name.Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*')).ToArray();
        name = new string(kept).Trim().Trim('.').Trim();
        if (name.Length == 0)
        {
            return DefaultFileName;
        }

        if (name.Length > MaxFileNameLength)
        {
            var extension = SafeExtension(name);
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return name;
    }

    /// <summary>A short, purely alphanumeric extension (with its dot) or nothing.</summary>
    private static string SafeExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1)
        {
            return string.Empty;
        }

        var extension = fileName[(dot + 1)..].ToLowerInvariant();
        return extension.Length <= 10 && extension.All(char.IsAsciiLetterOrDigit) ? "." + extension : string.Empty;
    }

    private static string CleanContentType(string? contentType)
    {
        var value = contentType?.Trim() ?? string.Empty;
        var valid = value.Length is > 0 and <= 127
            && value.Count(c => c == '/') == 1
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '+' or '.' or '_');
        return valid ? value.ToLowerInvariant() : DefaultContentType;
    }
}
