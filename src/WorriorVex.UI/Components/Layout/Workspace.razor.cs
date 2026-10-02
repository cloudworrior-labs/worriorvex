using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Search;
using WorriorVex.Application.Trash;
using WorriorVex.Application.Tree;
using WorriorVex.Domain;
using WorriorVex.UI.Components.Navigation;
using WorriorVex.UI.Components.Notes;
using WorriorVex.UI.Components.Search;

namespace WorriorVex.UI.Components.Layout;

/// <summary>
/// The three-pane workspace. It holds what is selected and asks the application services to do
/// the work; rules about notes, folders and the trash live in those services, not here.
/// </summary>
public partial class Workspace
{
    private readonly string _modifier = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    private List<NotebookSummary> _notebooks = [];
    private List<FolderSummary> _folders = [];
    private List<NoteSummary> _notes = [];
    private List<TrashItem> _trash = [];
    private NavSelection _selection = new NavSelection.AllNotes();
    private NoteDetail? _current;
    private TrashItem? _trashItem;
    private NoteEditor? _editor;

    private SearchBox? _searchBox;
    private List<NoteSearchResult> _searchResults = [];
    private string _searchText = string.Empty;
    private NavSelection? _beforeSearch;
    private int _searchMark = -1;
    private int _searchRun;
    private bool _searching;

    private PromptRequest? _prompt;
    private ConfirmRequest? _confirm;
    private string? _dialogError;
    private string? _error;
    private string? _notice;
    private bool _loading = true;
    private bool _focusNewNote;

    private IJSObjectReference? _shortcutsModule;
    private IJSObjectReference? _shortcuts;
    private DotNetObjectReference<Workspace>? _self;

    [Inject] private INoteService Notes { get; set; } = default!;
    [Inject] private INotebookService Notebooks { get; set; } = default!;
    [Inject] private ITreeService Tree { get; set; } = default!;
    [Inject] private ITrashService Trash { get; set; } = default!;
    [Inject] private INoteSearchService Search { get; set; } = default!;
    [Inject] private NoteAutosaver Autosaver { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<Workspace> Logger { get; set; } = default!;

    private NotebookSummary? Inbox => _notebooks.FirstOrDefault(n => n.IsInbox);

    private Dictionary<Guid, string> NotebookNames => _notebooks.ToDictionary(n => n.Id, n => n.Name);

    private string Heading => _selection switch
    {
        NavSelection.Notebook notebook => _notebooks.FirstOrDefault(n => n.Id == notebook.NotebookId)?.Name ?? "Notebook",
        NavSelection.Folder folder => _folders.FirstOrDefault(f => f.Id == folder.FolderId)?.Name ?? "Folder",
        NavSelection.Trash => "Trash",
        _ => "All Notes",
    };

    private bool IsHelpPage => _selection is NavSelection.Documentation or NavSelection.About;

    /// <summary>Folders can be created in a user's notebook or in a folder, not in the Inbox or a combined list.</summary>
    private bool CanHoldFolders => _selection switch
    {
        NavSelection.Notebook notebook => notebook.NotebookId != Inbox?.Id,
        NavSelection.Folder => true,
        _ => false,
    };

    /// <summary>What is selected can be renamed and deleted: a user's notebook or a folder.</summary>
    private bool CanChangeContainer => CanHoldFolders;

    protected override async Task OnInitializedAsync()
    {
        Autosaver.StateChanged += OnSaveStateChanged;
        Autosaver.NoteSaved += OnNoteSaved;

        try
        {
            await LoadStructureAsync();
            _selection = Inbox is { } inbox ? new NavSelection.Notebook(inbox.Id) : new NavSelection.AllNotes();
            await LoadListAsync(openFirst: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Loading notes failed");
            _error = "Your notes could not be loaded. Nothing has been changed on disk.";
        }
        finally
        {
            _loading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            _shortcutsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/WorriorVex.UI/js/shortcuts.js");
            _shortcuts = await _shortcutsModule.InvokeAsync<IJSObjectReference>("register", _self);
        }

        if (_focusNewNote && _editor is not null)
        {
            _focusNewNote = false;
            _editor.FocusTitleOnNextLoad();
            StateHasChanged();
        }
    }

    [JSInvokable]
    public Task OnShortcut(string action)
    {
        if (_prompt is not null || _confirm is not null)
        {
            return Task.CompletedTask;
        }

        return action switch
        {
            "newNote" => InvokeAsync(NewNoteAsync),
            "search" => InvokeAsync(async () =>
            {
                if (_searchBox is not null)
                {
                    await _searchBox.FocusAsync();
                }
            }),
            "save" => InvokeAsync(SaveNowAsync),
            _ => Task.CompletedTask,
        };
    }

    // ---- Loading -------------------------------------------------------------------------------

    private async Task LoadStructureAsync()
    {
        _notebooks = [.. await Notebooks.ListAsync()];
        _folders = [.. await Tree.ListFoldersAsync()];
    }

    private async Task LoadListAsync(bool openFirst)
    {
        _trashItem = null;
        switch (_selection)
        {
            case NavSelection.Trash:
                _trash = [.. await Trash.ListAsync()];
                _notes = [];
                _current = null;
                return;
            case NavSelection.Documentation or NavSelection.About or NavSelection.Search:
                _notes = [];
                _current = null;
                return;
            case NavSelection.Notebook notebook:
                _notes = [.. await Notes.ListAsync(notebook.NotebookId)];
                break;
            case NavSelection.Folder folder:
                _notes = [.. await Notes.ListAsync(folder.NotebookId, folder.FolderId)];
                break;
            default:
                _notes = [.. await Notes.ListAllAsync()];
                break;
        }

        if (openFirst)
        {
            _current = _notes.Count > 0 ? await Notes.GetAsync(_notes[0].Id) : null;
        }
    }

    // ---- Navigation and notes ------------------------------------------------------------------

    private async Task SelectAsync(NavSelection selection)
    {
        if (selection == _selection || !await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("That could not be opened.", async () =>
        {
            EndSearch();
            _selection = selection;
            _notice = null;
            await LoadListAsync(openFirst: true);
        });
    }

    private async Task NewNoteAsync()
    {
        if (!await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("The note could not be created.", async () =>
        {
            NoteDetail note;
            switch (_selection)
            {
                case NavSelection.Folder folder:
                    note = await Notes.CreateAsync(folder.NotebookId, folder.FolderId);
                    break;
                case NavSelection.Notebook notebook:
                    note = await Notes.CreateAsync(notebook.NotebookId);
                    break;
                case NavSelection.Trash or NavSelection.Documentation or NavSelection.About or NavSelection.Search:
                    // Nothing is created in the trash or on a help page: a new note goes to the Inbox, and so does the view.
                    note = await Notes.CreateAsync();
                    EndSearch();
                    _selection = new NavSelection.Notebook(note.NotebookId);
                    await LoadListAsync(openFirst: false);
                    break;
                default:
                    note = await Notes.CreateAsync();
                    break;
            }

            _notes.RemoveAll(n => n.Id == note.Id);
            _notes.Insert(0, note.ToSummary());
            _current = note;
            _focusNewNote = true;
            _notice = null;
        });
    }

    private async Task OpenAsync(Guid id)
    {
        if (id == _current?.Id || !await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("The note could not be opened.", async () =>
        {
            var note = await Notes.GetAsync(id);
            if (note is null || note.DeletedAt is not null)
            {
                _notes.RemoveAll(n => n.Id == id);
                _error = "That note is no longer here.";
                return;
            }

            _current = note;
        });
    }

    private async Task DeleteCurrentNoteAsync()
    {
        if (_current is not { } note || !await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("The note could not be moved to the trash.", async () =>
        {
            await Trash.MoveToTrashAsync(note.Id);
            _searchResults.RemoveAll(r => r.NoteId == note.Id);
            var index = _notes.FindIndex(n => n.Id == note.Id);
            _notes.RemoveAll(n => n.Id == note.Id);
            var next = _notes.Count == 0 ? null : _notes[Math.Clamp(index, 0, _notes.Count - 1)];
            _current = next is null ? null : await Notes.GetAsync(next.Id);
            _notice = $"\"{note.Title}\" was moved to the trash.";
        });
    }

    private void OnEdited(NoteEdit edit) => Autosaver.Edit(edit.NoteId, edit.Title, edit.Content);

    private async Task SaveNowAsync()
    {
        if (_editor is not null)
        {
            await _editor.FlushAsync();
        }

        await Autosaver.FlushAsync();
    }

    /// <summary>Leaving a note with edits that cannot be saved would lose them, so stay put and say why.</summary>
    private async Task<bool> SaveBeforeLeavingAsync()
    {
        if (_editor is not null && _current is not null)
        {
            await _editor.FlushAsync();
        }

        if (await Autosaver.FlushAsync())
        {
            return true;
        }

        _error = "This note could not be saved, so it stays open. Retry the save before doing anything else.";
        return false;
    }

    // ---- Search --------------------------------------------------------------------------------

    /// <summary>Runs when the text in the search box has settled. The list pane becomes the list of results.</summary>
    private async Task SearchAsync(string text)
    {
        if (_selection is not NavSelection.Search)
        {
            if (string.IsNullOrWhiteSpace(text) || !await SaveBeforeLeavingAsync())
            {
                return;
            }

            _beforeSearch = _selection;
            _selection = new NavSelection.Search();
            _notes = [];
            _current = null;
            _trashItem = null;
            _notice = null;
        }

        if (text == _searchText && _searchResults.Count > 0)
        {
            // Enter re-sends the text that was already searched for: keep the results and the mark on them.
            return;
        }

        _searchText = text;
        _searchMark = -1;
        var run = ++_searchRun;
        if (string.IsNullOrWhiteSpace(text))
        {
            _searchResults = [];
            _searching = false;
            return;
        }

        _searching = true;
        try
        {
            var results = await Search.SearchAsync(text);
            if (run == _searchRun)
            {
                _searchResults = [.. results];
                _error = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Search failed");
            if (run == _searchRun)
            {
                _searchResults = [];
                _error = "The search could not be run. Your notes are not affected.";
            }
        }
        finally
        {
            if (run == _searchRun)
            {
                _searching = false;
            }
        }
    }

    private void MoveSearchMark(int step)
    {
        if (_selection is NavSelection.Search && _searchResults.Count > 0)
        {
            _searchMark = Math.Clamp(_searchMark + step, 0, _searchResults.Count - 1);
        }
    }

    private async Task OpenMarkedResultAsync()
    {
        if (_selection is NavSelection.Search && _searchResults.Count > 0)
        {
            await OpenAsync(_searchResults[Math.Max(_searchMark, 0)].NoteId);
        }
    }

    /// <summary>Escape in the search box: back to where the user was before searching.</summary>
    private async Task CloseSearchAsync()
    {
        if (_selection is not NavSelection.Search)
        {
            _searchText = string.Empty;
            return;
        }

        if (!await SaveBeforeLeavingAsync())
        {
            return;
        }

        var back = _beforeSearch ?? (Inbox is { } inbox ? new NavSelection.Notebook(inbox.Id) : new NavSelection.AllNotes());
        await RunAsync("That could not be opened.", () => GoToAsync(back));
    }

    private void EndSearch()
    {
        _searchRun++;
        _searchText = string.Empty;
        _searchResults = [];
        _searchMark = -1;
        _searching = false;
        _beforeSearch = null;
    }

    // ---- Notebooks and folders -----------------------------------------------------------------

    private void PromptNewNotebook() => OpenPrompt(new PromptRequest(
        "New notebook", "Name", string.Empty, "Create", Notebook.MaxNameLength,
        async name =>
        {
            var notebook = await Notebooks.CreateAsync(name);
            await LoadStructureAsync();
            await GoToAsync(new NavSelection.Notebook(notebook.Id));
        }));

    private void PromptNewFolder()
    {
        var (notebookId, parentId) = _selection switch
        {
            NavSelection.Folder folder => (folder.NotebookId, (Guid?)folder.FolderId),
            NavSelection.Notebook notebook => (notebook.NotebookId, null),
            _ => (Guid.Empty, null),
        };
        if (notebookId == Guid.Empty)
        {
            return;
        }

        OpenPrompt(new PromptRequest(
            "New folder", $"Name of the folder in \"{Heading}\"", string.Empty, "Create", Node.MaxNameLength,
            async name =>
            {
                var folder = await Tree.CreateFolderAsync(notebookId, parentId, name);
                await LoadStructureAsync();
                await GoToAsync(new NavSelection.Folder(folder.NotebookId, folder.Id));
            }));
    }

    private void PromptRename()
    {
        switch (_selection)
        {
            case NavSelection.Folder folder:
                OpenPrompt(new PromptRequest(
                    "Rename folder", "Name", Heading, "Rename", Node.MaxNameLength,
                    async name =>
                    {
                        await Tree.RenameFolderAsync(folder.FolderId, name);
                        await LoadStructureAsync();
                    }));
                break;
            case NavSelection.Notebook notebook when CanChangeContainer:
                OpenPrompt(new PromptRequest(
                    "Rename notebook", "Name", Heading, "Rename", Notebook.MaxNameLength,
                    async name =>
                    {
                        await Notebooks.RenameAsync(notebook.NotebookId, name);
                        await LoadStructureAsync();
                    }));
                break;
        }
    }

    private void ConfirmDeleteContainer()
    {
        var name = Heading;
        switch (_selection)
        {
            case NavSelection.Folder folder:
                OpenConfirm(new ConfirmRequest(
                    $"Delete folder \"{name}\"?",
                    "The folder and everything in it move to the trash. You can restore them from there.",
                    "Move to trash",
                    Danger: false,
                    async () =>
                    {
                        await Trash.MoveToTrashAsync(folder.FolderId);
                        await AfterContainerDeletedAsync(name);
                    }));
                break;
            case NavSelection.Notebook notebook when CanChangeContainer:
                OpenConfirm(new ConfirmRequest(
                    $"Delete notebook \"{name}\"?",
                    "The notebook and everything in it move to the trash. You can restore them from there.",
                    "Move to trash",
                    Danger: false,
                    async () =>
                    {
                        await Trash.MoveNotebookToTrashAsync(notebook.NotebookId);
                        await AfterContainerDeletedAsync(name);
                    }));
                break;
        }
    }

    private async Task AfterContainerDeletedAsync(string name)
    {
        await LoadStructureAsync();
        await GoToAsync(Inbox is { } inbox ? new NavSelection.Notebook(inbox.Id) : new NavSelection.AllNotes());
        _notice = $"\"{name}\" was moved to the trash.";
    }

    private async Task GoToAsync(NavSelection selection)
    {
        EndSearch();
        _selection = selection;
        _notice = null;
        await LoadListAsync(openFirst: true);
    }

    // ---- Trash ---------------------------------------------------------------------------------

    private async Task RestoreAsync(TrashItem item)
    {
        await RunAsync("The item could not be restored.", async () =>
        {
            var result = await Trash.RestoreAsync(item.Id);
            await LoadStructureAsync();
            await LoadListAsync(openFirst: false);

            var notebook = _notebooks.FirstOrDefault(n => n.Id == result.NotebookId)?.Name ?? "its notebook";
            _notice = result.Relocated
                ? $"\"{item.Name}\" was restored to the top of {notebook}, because the place it was in is gone."
                : $"\"{item.Name}\" was restored.";
        });
    }

    private void ConfirmDeletePermanently(TrashItem item) => OpenConfirm(new ConfirmRequest(
        $"Delete \"{item.Name}\" permanently?",
        item.Kind == TrashItemKind.Note
            ? "The note, its history and its attachments are removed for good. This cannot be undone."
            : "It and everything that was deleted with it, including attachments, are removed for good. This cannot be undone.",
        "Delete permanently",
        Danger: true,
        async () =>
        {
            await Trash.DeletePermanentlyAsync(item.Id);
            await LoadListAsync(openFirst: false);
            _notice = $"\"{item.Name}\" was deleted permanently.";
        }));

    private void ConfirmEmptyTrash() => OpenConfirm(new ConfirmRequest(
        "Empty the trash?",
        $"{_trash.Count} item(s) and everything in them, including attachments, are removed for good. This cannot be undone.",
        "Empty trash",
        Danger: true,
        async () =>
        {
            var removed = await Trash.EmptyAsync();
            await LoadListAsync(openFirst: false);
            _notice = $"The trash was emptied: {removed} item(s) removed.";
        }));

    // ---- Dialogs and errors --------------------------------------------------------------------

    private void OpenPrompt(PromptRequest request)
    {
        _dialogError = null;
        _confirm = null;
        _prompt = request;
    }

    private void OpenConfirm(ConfirmRequest request)
    {
        _dialogError = null;
        _prompt = null;
        _confirm = request;
    }

    private void CloseDialogs()
    {
        _prompt = null;
        _confirm = null;
        _dialogError = null;
    }

    /// <summary>A refused name keeps the dialog open with the reason, so it can be corrected in place.</summary>
    private async Task ConfirmPromptAsync(string value)
    {
        if (_prompt is not { } prompt || !await SaveBeforeLeavingAsync())
        {
            return;
        }

        try
        {
            await prompt.OnConfirm(value);
            CloseDialogs();
        }
        catch (DomainException ex)
        {
            _dialogError = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Dialog} failed", prompt.Title);
            _dialogError = "That did not work. Nothing was changed.";
        }
    }

    private async Task ConfirmActionAsync()
    {
        if (_confirm is not { } confirm)
        {
            return;
        }

        CloseDialogs();
        if (await SaveBeforeLeavingAsync())
        {
            await RunAsync("That did not work. Nothing was removed.", confirm.OnConfirm);
        }
    }

    /// <summary>Runs an action; a broken rule is shown in its own words, anything else is logged and summarised.</summary>
    private async Task RunAsync(string failureMessage, Func<Task> action)
    {
        try
        {
            _error = null;
            await action();
        }
        catch (DomainException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Failure}", failureMessage);
            _error = failureMessage;
        }
    }

    // ---- Autosave events -----------------------------------------------------------------------

    private void OnSaveStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnNoteSaved(NoteDetail saved) => _ = InvokeAsync(() =>
    {
        var index = _notes.FindIndex(n => n.Id == saved.Id);
        if (index < 0)
        {
            return;
        }

        var summary = _notes[index] with { Title = saved.Title, UpdatedAt = saved.UpdatedAt };
        if (_notes[index] == summary)
        {
            return;
        }

        _notes[index] = summary;
        _notes = [.. _notes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt)];
        StateHasChanged();
    });

    public async ValueTask DisposeAsync()
    {
        Autosaver.StateChanged -= OnSaveStateChanged;
        Autosaver.NoteSaved -= OnNoteSaved;

        try
        {
            if (_shortcuts is not null)
            {
                await _shortcuts.InvokeVoidAsync("dispose");
                await _shortcuts.DisposeAsync();
            }

            if (_shortcutsModule is not null)
            {
                await _shortcutsModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record PromptRequest(string Title, string Label, string InitialValue, string ConfirmText, int MaxLength, Func<string, Task> OnConfirm);

    private sealed record ConfirmRequest(string Title, string Message, string ConfirmText, bool Danger, Func<Task> OnConfirm);
}
