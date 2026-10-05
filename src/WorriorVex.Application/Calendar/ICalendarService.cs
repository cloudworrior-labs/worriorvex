using WorriorVex.Application.Notes;

namespace WorriorVex.Application.Calendar;

/// <summary>A note written against a day. Its title is the short text shown in the day's cell.</summary>
/// <param name="HasBody">The note has more than its title: text, pictures or files in its body.</param>
public sealed record CalendarEntry(Guid NoteId, DateOnly Date, string Title, bool HasBody);

/// <summary>What a month view shows: the entries of the days in range, and how many other notes were changed on each day.</summary>
public sealed record CalendarRange(IReadOnlyList<CalendarEntry> Entries, IReadOnlyDictionary<DateOnly, int> Activity);

/// <summary>
/// The calendar: any number of short notes per day. An entry is an ordinary note with a date, kept
/// in the "Calendar" notebook (created when first needed), so it is searched, tagged, linked,
/// backed up and exported like every other note, and can be opened in the editor to say more.
/// </summary>
public interface ICalendarService
{
    const string NotebookName = "Calendar";

    /// <summary>Entries dated from <paramref name="from"/> to <paramref name="to"/> inclusive, in the order added within a day.</summary>
    Task<CalendarRange> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Adds an entry to a day. The text becomes the note's title.</summary>
    Task<CalendarEntry> AddAsync(DateOnly date, string text, CancellationToken cancellationToken = default);

    /// <summary>Moves an entry to another day.</summary>
    Task MoveAsync(Guid noteId, DateOnly date, CancellationToken cancellationToken = default);
}
