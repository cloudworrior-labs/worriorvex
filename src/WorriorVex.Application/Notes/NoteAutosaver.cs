namespace WorriorVex.Application.Notes;

public enum SaveState
{
    /// <summary>Everything the user typed is on disk.</summary>
    Saved,

    /// <summary>There are edits waiting for the autosave delay.</summary>
    Unsaved,

    Saving,

    /// <summary>The last save failed. The edits are still held in memory and can be retried.</summary>
    Failed,
}

/// <summary>
/// Saves note edits after a short pause in typing. Edits are held in memory until a save
/// succeeds, so a failed save never loses them: the state becomes <see cref="SaveState.Failed"/>
/// and <see cref="FlushAsync"/> retries.
/// </summary>
public sealed class NoteAutosaver(INoteService notes, TimeProvider timeProvider, TimeSpan? delay = null) : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(700);

    private readonly TimeSpan _delay = delay ?? DefaultDelay;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _saving = new(1, 1);
    private PendingEdit? _pending;
    private CancellationTokenSource? _timer;
    private SaveState _state = SaveState.Saved;

    /// <summary>Raised when <see cref="State"/> changes. May be raised on any thread.</summary>
    public event Action? StateChanged;

    /// <summary>Raised after a note was written. May be raised on any thread.</summary>
    public event Action<NoteDetail>? NoteSaved;

    public SaveState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool HasUnsavedEdits
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>Records the latest title and body for a note and (re)starts the autosave delay.</summary>
    public void Edit(Guid noteId, string? title, string? content)
    {
        CancellationTokenSource timer;
        lock (_gate)
        {
            _pending = new PendingEdit(noteId, title, content);
            _timer?.Cancel();
            _timer = timer = new CancellationTokenSource();
            if (_state != SaveState.Saving)
            {
                _state = SaveState.Unsaved;
            }
        }

        StateChanged?.Invoke();
        _ = SaveAfterDelayAsync(timer.Token);
    }

    /// <summary>
    /// Saves any waiting edits now. Returns <c>true</c> when nothing is left unsaved.
    /// Never throws for a failed save; it reports through <see cref="State"/> instead.
    /// </summary>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        await _saving.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PendingEdit? edit;
            lock (_gate)
            {
                edit = _pending;
                if (edit is null)
                {
                    return _state != SaveState.Failed;
                }

                _timer?.Cancel();
                _state = SaveState.Saving;
            }

            StateChanged?.Invoke();

            NoteDetail? saved = null;
            var succeeded = false;
            try
            {
                saved = await notes.UpdateAsync(edit.NoteId, edit.Title, edit.Content, cancellationToken).ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception)
            {
                // Reported through State; the edit stays in _pending for a retry.
            }

            bool nothingLeft;
            lock (_gate)
            {
                if (succeeded && ReferenceEquals(_pending, edit))
                {
                    _pending = null;
                }

                nothingLeft = _pending is null;
                _state = !succeeded ? SaveState.Failed : nothingLeft ? SaveState.Saved : SaveState.Unsaved;
            }

            if (saved is not null)
            {
                NoteSaved?.Invoke(saved);
            }

            StateChanged?.Invoke();
            return succeeded && nothingLeft;
        }
        finally
        {
            _saving.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Cancel();
        }
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private sealed record PendingEdit(Guid NoteId, string? Title, string? Content);
}
