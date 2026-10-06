using WorriorVex.Application.Calendar;

namespace WorriorVex.IntegrationTests;

public sealed class CalendarTests
{
    [Fact]
    public async Task A_day_holds_several_notes_and_they_are_ordinary_notes()
    {
        using var data = new TempDataDirectory();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(data, clock);
        var calendar = app.Get<ICalendarService>();
        var day = new DateOnly(2026, 10, 5);

        var first = await calendar.AddAsync(day, "Dentist 10:00");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await calendar.AddAsync(day, "Call supplier");
        await calendar.AddAsync(day.AddDays(40), "Outside the range");
        var other = await app.Notes.CreateAsync(title: "Unrelated", content: "<p>x</p>");

        var range = await calendar.GetRangeAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8));

        Assert.Equal(["Dentist 10:00", "Call supplier"], range.Entries.Where(e => e.Date == day).Select(e => e.Title));
        Assert.DoesNotContain(range.Entries, e => e.Title == "Outside the range");
        Assert.All(range.Entries, e => Assert.False(e.HasBody));
        // The unrelated note was changed today: it shows as activity, not as an entry.
        Assert.Equal(1, range.Activity.Values.Sum());

        // Entries live in the Calendar notebook and are found by search like any note.
        var notebook = Assert.Single(await app.Notebooks.ListAsync(), n => n.IsCalendar);
        Assert.Equal(ICalendarService.NotebookName, notebook.Name);
        Assert.Equal(notebook.Id, (await app.Notes.GetAsync(first.NoteId))!.NotebookId);
        Assert.Contains(await app.Search.SearchAsync("dentist"), r => r.NoteId == first.NoteId);

        // Writing more in the editor marks it; moving changes the day; the trash takes it off the calendar.
        await app.Notes.UpdateAsync(second.NoteId, "Call supplier", "<p>ask about the invoice</p>");
        await calendar.MoveAsync(second.NoteId, day.AddDays(1));
        await app.Trash.MoveToTrashAsync(first.NoteId);
        range = await calendar.GetRangeAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8));
        var moved = Assert.Single(range.Entries);
        Assert.Equal((day.AddDays(1), true), (moved.Date, moved.HasBody));
        Assert.NotNull(other);
    }
}
