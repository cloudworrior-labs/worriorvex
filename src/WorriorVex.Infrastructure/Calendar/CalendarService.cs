using Microsoft.EntityFrameworkCore;
using WorriorVex.Application.Calendar;
using WorriorVex.Application.Notes;
using WorriorVex.Domain;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Calendar;

public sealed class CalendarService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    INoteService notes,
    INotebookService notebooks,
    TimeProvider timeProvider) : ICalendarService
{
    public async Task<CalendarRange> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Nodes.AsNoTracking()
            .Where(n => n.Type == NodeType.Note && n.DeletedAt == null && n.CalendarDate != null && n.CalendarDate >= from && n.CalendarDate <= to)
            .OrderBy(n => n.CalendarDate).ThenBy(n => n.CreatedAt)
            .Select(n => new { n.Id, n.CalendarDate, n.Name, HasBody = n.Note!.Content != "" })
            .ToListAsync(cancellationToken);
        var entries = rows.Select(r => new CalendarEntry(r.Id, r.CalendarDate!.Value, r.Name, r.HasBody)).ToList();

        // Other notes changed on each day, by the local calendar. A day wider on both sides covers every time zone.
        var zone = timeProvider.LocalTimeZone;
        var start = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var changed = await context.Nodes.AsNoTracking()
            .Where(n => n.Type == NodeType.Note && n.DeletedAt == null && n.CalendarDate == null && n.UpdatedAt >= start && n.UpdatedAt < end)
            .Select(n => n.UpdatedAt)
            .ToListAsync(cancellationToken);
        var activity = changed
            .Select(at => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime))
            .Where(day => day >= from && day <= to)
            .GroupBy(day => day)
            .ToDictionary(g => g.Key, g => g.Count());
        return new CalendarRange(entries, activity);
    }

    public async Task<CalendarEntry> AddAsync(DateOnly date, string text, CancellationToken cancellationToken = default)
    {
        var notebook = await CalendarNotebookAsync(cancellationToken);
        var note = await notes.CreateAsync(notebook.Id, null, text, null, cancellationToken);
        await SetDateAsync(note.Id, date, cancellationToken);
        return new CalendarEntry(note.Id, date, note.Title, HasBody: false);
    }

    public Task MoveAsync(Guid noteId, DateOnly date, CancellationToken cancellationToken = default) => SetDateAsync(noteId, date, cancellationToken);

    private async Task SetDateAsync(Guid noteId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.Id == noteId && n.Type == NodeType.Note, cancellationToken)
            ?? throw new NoteNotFoundException(noteId);
        node.SetCalendarDate(date, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<NotebookSummary> CalendarNotebookAsync(CancellationToken cancellationToken)
    {
        var existing = (await notebooks.ListAsync(cancellationToken))
            .FirstOrDefault(n => string.Equals(n.Name, ICalendarService.NotebookName, StringComparison.OrdinalIgnoreCase));
        return existing ?? await notebooks.CreateAsync(ICalendarService.NotebookName, cancellationToken);
    }
}
