using Microsoft.Extensions.Time.Testing;
using WorriorVex.Application.Notes;

namespace WorriorVex.Application.Tests;

public class NoteAutosaverTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(700);
    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteService _notes = new();
    private readonly Guid _noteId = Guid.NewGuid();

    private NoteAutosaver CreateAutosaver() => new(_notes, _time, Delay);

    [Fact]
    public void Starts_saved_with_nothing_pending()
    {
        using var autosaver = CreateAutosaver();

        Assert.Equal(SaveState.Saved, autosaver.State);
        Assert.False(autosaver.HasUnsavedEdits);
    }

    [Fact]
    public async Task An_edit_is_saved_after_the_delay_not_before()
    {
        using var autosaver = CreateAutosaver();
        var saved = WaitForState(autosaver, SaveState.Saved);

        autosaver.Edit(_noteId, "Title", "<p>body</p>");
        Assert.Equal(SaveState.Unsaved, autosaver.State);

        _time.Advance(Delay - TimeSpan.FromMilliseconds(1));
        Assert.Empty(_notes.Saves);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await saved;

        Assert.Equal((_noteId, "Title", "<p>body</p>"), Assert.Single(_notes.Saves));
        Assert.False(autosaver.HasUnsavedEdits);
    }

    [Fact]
    public async Task Typing_again_restarts_the_delay_and_saves_only_the_latest()
    {
        using var autosaver = CreateAutosaver();
        var saved = WaitForState(autosaver, SaveState.Saved);

        autosaver.Edit(_noteId, "T", "<p>a</p>");
        _time.Advance(Delay - TimeSpan.FromMilliseconds(100));
        autosaver.Edit(_noteId, "T", "<p>ab</p>");
        _time.Advance(Delay - TimeSpan.FromMilliseconds(100));
        Assert.Empty(_notes.Saves);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        await saved;

        Assert.Equal((_noteId, "T", "<p>ab</p>"), Assert.Single(_notes.Saves));
    }

    [Fact]
    public async Task Flush_saves_immediately()
    {
        using var autosaver = CreateAutosaver();
        autosaver.Edit(_noteId, "Now", "<p>now</p>");

        var clean = await autosaver.FlushAsync();

        Assert.True(clean);
        Assert.Equal(SaveState.Saved, autosaver.State);
        Assert.Single(_notes.Saves);

        _time.Advance(Delay * 2);
        Assert.Single(_notes.Saves);
    }

    [Fact]
    public async Task Flush_with_nothing_pending_does_not_write()
    {
        using var autosaver = CreateAutosaver();

        Assert.True(await autosaver.FlushAsync());
        Assert.Empty(_notes.Saves);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_edit_and_a_retry_writes_it()
    {
        using var autosaver = CreateAutosaver();
        _notes.FailWith = new IOException("disk full");
        autosaver.Edit(_noteId, "Keep", "<p>do not lose me</p>");

        var clean = await autosaver.FlushAsync();

        Assert.False(clean);
        Assert.Equal(SaveState.Failed, autosaver.State);
        Assert.True(autosaver.HasUnsavedEdits);
        Assert.Empty(_notes.Saves);

        _notes.FailWith = null;
        clean = await autosaver.FlushAsync();

        Assert.True(clean);
        Assert.Equal(SaveState.Saved, autosaver.State);
        Assert.Equal((_noteId, "Keep", "<p>do not lose me</p>"), Assert.Single(_notes.Saves));
    }

    [Fact]
    public async Task An_edit_made_while_a_save_is_running_is_not_dropped()
    {
        using var autosaver = CreateAutosaver();
        _notes.Gate = new TaskCompletionSource();
        autosaver.Edit(_noteId, "T", "<p>first</p>");

        var firstFlush = autosaver.FlushAsync();
        await _notes.Started.Task;
        autosaver.Edit(_noteId, "T", "<p>second</p>");
        _notes.Gate.SetResult();

        Assert.False(await firstFlush);
        Assert.Equal(SaveState.Unsaved, autosaver.State);
        Assert.True(autosaver.HasUnsavedEdits);

        _notes.Gate = null;
        Assert.True(await autosaver.FlushAsync());
        Assert.Equal(["<p>first</p>", "<p>second</p>"], _notes.Saves.Select(s => s.Content));
    }

    [Fact]
    public async Task NoteSaved_reports_what_was_written()
    {
        using var autosaver = CreateAutosaver();
        NoteDetail? reported = null;
        autosaver.NoteSaved += note => reported = note;
        autosaver.Edit(_noteId, "Reported", "<p>x</p>");

        await autosaver.FlushAsync();

        Assert.NotNull(reported);
        Assert.Equal(_noteId, reported.Id);
        Assert.Equal("Reported", reported.Title);
    }

    private static Task WaitForState(NoteAutosaver autosaver, SaveState wanted)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawOther = false;
        autosaver.StateChanged += () =>
        {
            var state = autosaver.State;
            if (state != wanted)
            {
                sawOther = true;
            }
            else if (sawOther)
            {
                reached.TrySetResult();
            }
        };
        return reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeNoteService : INoteService
    {
        public List<(Guid Id, string? Title, string? Content)> Saves { get; } = [];
        public Exception? FailWith { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NoteDetail> UpdateAsync(Guid id, string? title, string? content, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            if (FailWith is not null)
            {
                throw FailWith;
            }

            Saves.Add((id, title, content));
            return new NoteDetail(id, Guid.Empty, title ?? string.Empty, content ?? string.Empty, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        }

        public Task<NoteDetail> CreateAsync(Guid? notebookId = null, Guid? parentId = null, string? title = null, string? content = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NoteDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListAsync(Guid notebookId, Guid? parentId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListFavoritesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListByTagAsync(Guid tagId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NoteSummary>> ListLooseEndsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NoteSummary>>([]);

        public Task RecordOpenedAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NoteDetail> DuplicateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
