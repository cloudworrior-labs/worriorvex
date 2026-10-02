using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WorriorVex.Application.Export;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Platform;
using WorriorVex.Application.Search;
using WorriorVex.Application.Tags;
using WorriorVex.Application.Trash;
using WorriorVex.Application.Tree;
using WorriorVex.Domain;
using Microsoft.AspNetCore.Components.Web;
using WorriorVex.UI.Components.Dialogs;
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
    private List<TagSummary> _tags = [];
    private MenuState? _menu;
    private MoveRequest? _move;
    private NoteSummary? _export;
    private (Guid Id, bool IsFolder)? _dragging;
    private int _connectionsVersion;
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
    [Inject] private ITagService TagService { get; set; } = default!;
    [Inject] private IExportService Exporter { get; set; } = default!;
    [Inject] private IPlatformShell Shell { get; set; } = default!;
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
        NavSelection.Favorites => "Favorites",
        NavSelection.Recent => "Recent",
        NavSelection.Tag tag => _tags.FirstOrDefault(t => t.Id == tag.TagId)?.Name ?? "Tag",
        _ => "All Notes",
    };

    /// <summary>Lists that gather notes from several notebooks say which notebook each note is in.</summary>
    private bool ShowsManyPlaces => _selection is NavSelection.AllNotes or NavSelection.Favorites or NavSelection.Recent or NavSelection.Tag;

    private string? EmptyMessage => _selection switch
    {
        NavSelection.Favorites => "No favorites yet. Mark a note with ★ to find it here.",
        NavSelection.Recent => "Notes you open appear here, most recent first.",
        NavSelection.Tag => "No notes carry this tag.",
        _ => null,
    };

    private bool IsHelpPage => _selection is NavSelection.Documentation or NavSelection.About or NavSelection.Data;

    /// <summary>Nothing has been written yet: the Inbox is the only notebook and it is empty.</summary>
    private bool _firstRun;

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
            _firstRun = _notebooks.Count == 1 && _notes.Count == 0 && (await Trash.ListAsync()).Count == 0;
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
            _shortcutsModule = await JS.InvokeAsync<IJSObjectReference>("import", UiAssets.Shortcuts);
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
        if (_prompt is not null || _confirm is not null || _move is not null || _menu is not null)
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
        await LoadTagsAsync();
    }

    private async Task LoadTagsAsync()
    {
        try
        {
            _tags = [.. await TagService.ListAsync()];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Tags could not be loaded");
        }
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
            case NavSelection.Documentation or NavSelection.About or NavSelection.Data or NavSelection.Search:
                _notes = [];
                _current = null;
                return;
            case NavSelection.Notebook notebook:
                _notes = [.. await Notes.ListAsync(notebook.NotebookId)];
                break;
            case NavSelection.Folder folder:
                _notes = [.. await Notes.ListAsync(folder.NotebookId, folder.FolderId)];
                break;
            case NavSelection.Favorites:
                _notes = [.. await Notes.ListFavoritesAsync()];
                break;
            case NavSelection.Recent:
                _notes = [.. await Notes.ListRecentAsync()];
                break;
            case NavSelection.Tag tag:
                _notes = [.. await Notes.ListByTagAsync(tag.TagId)];
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
                case NavSelection.Trash or NavSelection.Documentation or NavSelection.About or NavSelection.Data or NavSelection.Search:
                    // Nothing is created in the trash or on a help page: a new note goes to the Inbox, and so does the view.
                    note = await Notes.CreateAsync();
                    EndSearch();
                    _selection = new NavSelection.Notebook(note.NotebookId);
                    await LoadListAsync(openFirst: false);
                    break;
                case NavSelection.Tag tag:
                    // A note made while looking at a tag gets that tag, so it stays in view.
                    note = await Notes.CreateAsync();
                    if (_tags.FirstOrDefault(t => t.Id == tag.TagId) is { } tagged)
                    {
                        await TagService.AddToNoteAsync(note.Id, tagged.Name);
                        await LoadTagsAsync();
                    }

                    break;
                case NavSelection.Favorites:
                    note = await Notes.CreateAsync();
                    await Notes.SetFavoriteAsync(note.Id, true);
                    note = note with { IsFavorite = true };
                    break;
                default:
                    note = await Notes.CreateAsync();
                    break;
            }

            _notes.RemoveAll(n => n.Id == note.Id);
            _notes.Insert(0, note.ToSummary());
            _current = note;
            _focusNewNote = true;
            _firstRun = false;
            _notice = null;
            await RecordOpenedAsync(note.Id);
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
            await RecordOpenedAsync(note.Id);
        });
    }

    /// <summary>A link inside a note was followed. The note opens wherever it is filed.</summary>
    private async Task OpenLinkedNoteAsync(Guid id)
    {
        if (id == _current?.Id)
        {
            return;
        }

        var note = await Notes.GetAsync(id);
        if (note is null)
        {
            _error = "The note this link points to no longer exists.";
            return;
        }

        if (note.DeletedAt is not null)
        {
            _error = $"\"{note.Title}\" is in the trash. Restore it to open it.";
            return;
        }

        if (_notes.All(n => n.Id != id) && _selection is not NavSelection.Search)
        {
            // Show the note in its own place, so the list beside it makes sense.
            var place = note.ParentId is { } folder ? new NavSelection.Folder(note.NotebookId, folder) : (NavSelection)new NavSelection.Notebook(note.NotebookId);
            if (!await SaveBeforeLeavingAsync())
            {
                return;
            }

            await RunAsync("The note could not be opened.", async () =>
            {
                EndSearch();
                _selection = place;
                _notice = null;
                await LoadListAsync(openFirst: false);
            });
        }

        await OpenAsync(id);
    }

    /// <summary>An earlier version of the open note was restored; show it and keep the list in step.</summary>
    private async Task OnRestoredAsync(NoteDetail restored)
    {
        await Autosaver.FlushAsync();
        _current = restored;
        ReplaceSummary(restored.ToSummary());
        SortNotes();
        _connectionsVersion++;
        _notice = $"\"{restored.Title}\" was restored to an earlier version. The version it replaced is kept in its history.";
    }

    private async Task OnTagsChangedAsync()
    {
        _connectionsVersion++;
        await LoadTagsAsync();
    }

    /// <summary>Remembers the note for the Recent list. Not worth an error if it fails.</summary>
    private async Task RecordOpenedAsync(Guid id)
    {
        try
        {
            await Notes.RecordOpenedAsync(id);
            var index = _notes.FindIndex(n => n.Id == id);
            if (index >= 0)
            {
                _notes[index] = _notes[index] with { LastOpenedAt = DateTimeOffset.UtcNow };
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The note could not be marked as opened");
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (_current is not { } note)
        {
            return;
        }

        await RunAsync("The favorite could not be changed.", async () =>
        {
            var favorite = !note.IsFavorite;
            await Notes.SetFavoriteAsync(note.Id, favorite);
            _current = note with { IsFavorite = favorite };
            ReplaceSummary(_current.ToSummary());
            if (_selection is NavSelection.Favorites && !favorite)
            {
                _notes.RemoveAll(n => n.Id == note.Id);
            }
        });
    }

    private async Task TogglePinnedAsync()
    {
        if (_current is not { } note)
        {
            return;
        }

        await RunAsync("The pin could not be changed.", async () =>
        {
            var pinned = !note.IsPinned;
            await Notes.SetPinnedAsync(note.Id, pinned);
            _current = note with { IsPinned = pinned };
            ReplaceSummary(_current.ToSummary());
            SortNotes();
        });
    }

    private void ReplaceSummary(NoteSummary summary)
    {
        var index = _notes.FindIndex(n => n.Id == summary.Id);
        if (index >= 0)
        {
            _notes[index] = summary with { LastOpenedAt = _notes[index].LastOpenedAt };
        }
    }

    private void SortNotes()
    {
        _notes = _selection is NavSelection.Notebook or NavSelection.Folder
            ? [.. _notes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt)]
            : _notes;
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

    // ---- Context menus -------------------------------------------------------------------------

    private void ShowNavMenu((NavSelection Target, MouseEventArgs Mouse) at)
    {
        var items = new List<ContextMenu.Item>();
        switch (at.Target)
        {
            case NavSelection.Notebook notebook when notebook.NotebookId == Inbox?.Id:
                items.Add(new("New note", () => NewNoteInAsync(notebook.NotebookId, null)));
                break;
            case NavSelection.Notebook notebook:
                var notebookName = _notebooks.FirstOrDefault(n => n.Id == notebook.NotebookId)?.Name ?? "Notebook";
                items.Add(new("New note", () => NewNoteInAsync(notebook.NotebookId, null)));
                items.Add(new("New folder", () => { PromptNewFolderIn(notebook.NotebookId, null, notebookName); return Task.CompletedTask; }));
                items.Add(ContextMenu.Item.Line());
                items.Add(new("Rename…", () => { PromptRenameNotebook(notebook.NotebookId, notebookName); return Task.CompletedTask; }));
                items.Add(new("Delete", () => { ConfirmDeleteNotebook(notebook.NotebookId, notebookName); return Task.CompletedTask; }));
                break;
            case NavSelection.Folder folder:
                var folderName = _folders.FirstOrDefault(f => f.Id == folder.FolderId)?.Name ?? "Folder";
                items.Add(new("New note", () => NewNoteInAsync(folder.NotebookId, folder.FolderId)));
                items.Add(new("New folder", () => { PromptNewFolderIn(folder.NotebookId, folder.FolderId, folderName); return Task.CompletedTask; }));
                items.Add(ContextMenu.Item.Line());
                items.Add(new("Rename…", () => { PromptRenameFolder(folder.FolderId, folderName); return Task.CompletedTask; }));
                items.Add(new("Move…", () => { OpenMove(folder.FolderId, isFolder: true, folderName); return Task.CompletedTask; }));
                items.Add(new("Delete", () => { ConfirmDeleteFolder(folder.FolderId, folderName); return Task.CompletedTask; }));
                break;
            case NavSelection.Tag tag:
                var tagName = _tags.FirstOrDefault(t => t.Id == tag.TagId)?.Name ?? "Tag";
                items.Add(new("Rename tag…", () => { PromptRenameTag(tag.TagId, tagName); return Task.CompletedTask; }));
                items.Add(new("Delete tag", () => { ConfirmDeleteTag(tag.TagId, tagName); return Task.CompletedTask; }));
                break;
            default:
                return;
        }

        _menu = new MenuState(items, at.Mouse.ClientX, at.Mouse.ClientY, "Actions");
    }

    private void ShowNoteMenu((NoteSummary Note, MouseEventArgs Mouse) at)
    {
        var note = at.Note;
        var items = new List<ContextMenu.Item>
        {
            new("Open", () => OpenAsync(note.Id)),
            new(note.IsFavorite ? "Remove from favorites" : "Add to favorites", () => SetFavoriteAsync(note, !note.IsFavorite)),
            new(note.IsPinned ? "Unpin" : "Pin to top", () => SetPinnedAsync(note, !note.IsPinned)),
            ContextMenu.Item.Line(),
            new("Move…", () => { OpenMove(note.Id, isFolder: false, note.Title); return Task.CompletedTask; }),
            new("Duplicate", () => DuplicateAsync(note.Id)),
            new("Export…", () => { _menu = null; _export = note; return Task.CompletedTask; }),
            ContextMenu.Item.Line(),
            new("Move to trash", () => DeleteNoteAsync(note.Id)),
        };
        _menu = new MenuState(items, at.Mouse.ClientX, at.Mouse.ClientY, $"Actions for {note.Title}");
    }

    private async Task SetFavoriteAsync(NoteSummary note, bool favorite)
    {
        if (_current?.Id == note.Id)
        {
            await ToggleFavoriteAsync();
            return;
        }

        await RunAsync("The favorite could not be changed.", async () =>
        {
            await Notes.SetFavoriteAsync(note.Id, favorite);
            ReplaceSummary(note with { IsFavorite = favorite });
            if (_selection is NavSelection.Favorites && !favorite)
            {
                _notes.RemoveAll(n => n.Id == note.Id);
            }
        });
    }

    private async Task SetPinnedAsync(NoteSummary note, bool pinned)
    {
        if (_current?.Id == note.Id)
        {
            await TogglePinnedAsync();
            return;
        }

        await RunAsync("The pin could not be changed.", async () =>
        {
            await Notes.SetPinnedAsync(note.Id, pinned);
            ReplaceSummary(note with { IsPinned = pinned });
            SortNotes();
        });
    }

    private async Task DuplicateAsync(Guid id)
    {
        if (!await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("The note could not be duplicated.", async () =>
        {
            var copy = await Notes.DuplicateAsync(id);
            if (_selection is NavSelection.Notebook or NavSelection.Folder or NavSelection.AllNotes or NavSelection.Tag)
            {
                _notes.Insert(0, copy.ToSummary());
                SortNotes();
            }

            _current = copy;
            _focusNewNote = true;
            await RecordOpenedAsync(copy.Id);
            await LoadTagsAsync();
            _notice = $"\"{copy.Title}\" was created beside the original.";
        });
    }

    private async Task DeleteNoteAsync(Guid id)
    {
        if (_current?.Id == id)
        {
            await DeleteCurrentNoteAsync();
            return;
        }

        var title = _notes.FirstOrDefault(n => n.Id == id)?.Title ?? "The note";
        await RunAsync("The note could not be moved to the trash.", async () =>
        {
            await Trash.MoveToTrashAsync(id);
            _notes.RemoveAll(n => n.Id == id);
            _searchResults.RemoveAll(r => r.NoteId == id);
            await LoadTagsAsync();
            _notice = $"\"{title}\" was moved to the trash.";
        });
    }

    private async Task NewNoteInAsync(Guid notebookId, Guid? parentId)
    {
        NavSelection target = parentId is { } folder ? new NavSelection.Folder(notebookId, folder) : new NavSelection.Notebook(notebookId);
        if (target != _selection)
        {
            await SelectAsync(target);
        }

        await NewNoteAsync();
    }

    // ---- Moving --------------------------------------------------------------------------------

    /// <summary>Opens the destination picker. A folder cannot go into itself or anything below it.</summary>
    private void OpenMove(Guid nodeId, bool isFolder, string name)
    {
        var excluded = new HashSet<Guid>();
        if (isFolder)
        {
            var pending = new Queue<Guid>([nodeId]);
            while (pending.TryDequeue(out var id) && excluded.Add(id))
            {
                foreach (var child in _folders.Where(f => f.ParentId == id))
                {
                    pending.Enqueue(child.Id);
                }
            }
        }

        _dialogError = null;
        _menu = null;
        _move = new MoveRequest(nodeId, isFolder, name, excluded);
    }

    private async Task ConfirmMoveAsync((Guid NotebookId, Guid? ParentId) destination)
    {
        if (_move is not { } move || !await SaveBeforeLeavingAsync())
        {
            return;
        }

        try
        {
            await MoveAsync(move.NodeId, move.IsFolder, move.Name, destination.NotebookId, destination.ParentId);
            _move = null;
        }
        catch (DomainException ex)
        {
            _dialogError = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Move failed");
            _dialogError = "That did not work. Nothing was moved.";
        }
    }

    private async Task MoveAsync(Guid nodeId, bool isFolder, string name, Guid notebookId, Guid? parentId)
    {
        await Tree.MoveAsync(nodeId, notebookId, parentId);
        await LoadStructureAsync();
        await LoadListAsync(openFirst: false);
        if (_current is not null && !isFolder && _current.Id == nodeId)
        {
            _current = await Notes.GetAsync(nodeId);
        }
        else if (_current is not null && _notes.Count > 0 && _notes.All(n => n.Id != _current.Id) && _selection is NavSelection.Notebook or NavSelection.Folder)
        {
            _current = null;
        }

        var place = parentId is { } folder
            ? _folders.FirstOrDefault(f => f.Id == folder)?.Name
            : _notebooks.FirstOrDefault(n => n.Id == notebookId)?.Name;
        _notice = $"\"{name}\" was moved to {place ?? "its new place"}.";
    }

    /// <summary>A note or folder was dropped on a notebook or folder in the navigation.</summary>
    private async Task DropAsync(NavSelection target)
    {
        if (_dragging is not { } dragged)
        {
            return;
        }

        _dragging = null;
        var (notebookId, parentId) = target switch
        {
            NavSelection.Folder folder => (folder.NotebookId, (Guid?)folder.FolderId),
            NavSelection.Notebook notebook => (notebook.NotebookId, null),
            _ => (Guid.Empty, null),
        };
        if (notebookId == Guid.Empty || parentId == dragged.Id)
        {
            return;
        }

        var name = dragged.IsFolder
            ? _folders.FirstOrDefault(f => f.Id == dragged.Id)?.Name ?? "Folder"
            : _notes.FirstOrDefault(n => n.Id == dragged.Id)?.Title ?? _current?.Title ?? "Note";
        if (!await SaveBeforeLeavingAsync())
        {
            return;
        }

        await RunAsync("That could not be moved.", () => MoveAsync(dragged.Id, dragged.IsFolder, name, notebookId, parentId));
    }

    // ---- Notebooks and folders -----------------------------------------------------------------

    private void PromptNewNotebook() => OpenPrompt(new PromptRequest(
        "New notebook", "Name", string.Empty, "Create", Notebook.MaxNameLength,
        async name =>
        {
            var notebook = await Notebooks.CreateAsync(name);
            _firstRun = false;
            await LoadStructureAsync();
            await GoToAsync(new NavSelection.Notebook(notebook.Id));
        }));

    /// <summary>The whole data set was replaced from a backup: start over as if the app had just opened.</summary>
    private async Task AfterRestoreAsync()
    {
        await RunAsync("The restored data could not be shown. Restart WorriorVex.", async () =>
        {
            _current = null;
            _notes = [];
            EndSearch();
            await LoadStructureAsync();
            _selection = new NavSelection.Data();
            _notice = "The backup was restored. Everything you see now comes from it.";
        });
    }

    private async Task ExportNoteAsync(NoteSummary note, ExportFormat format)
    {
        _export = null;
        if (!await SaveBeforeLeavingAsync())
        {
            return;
        }

        var suggested = ExportService_SafeName(note.Title) + IExportService.Extension(format);
        var path = await Shell.PickSaveLocationAsync($"Export \"{note.Title}\"", suggested);
        if (path is null)
        {
            return;
        }

        await RunAsync("The note could not be exported.", async () =>
        {
            var result = await Exporter.ExportNoteAsync(note.Id, format, path);
            _notice = result.Problems.Count == 0
                ? $"\"{note.Title}\" was exported to {result.Path}."
                : $"\"{note.Title}\" was exported to {result.Path}, with problems: {string.Join(" ", result.Problems)}";
        });
    }

    /// <summary>A file name from a title: no path separators or characters file systems refuse.</summary>
    private static string ExportService_SafeName(string title)
    {
        var cleaned = Domain.Attachment.CleanFileName(title.Replace('/', '-').Replace('\\', '-'));
        return cleaned.Length > 120 ? cleaned[..120].TrimEnd() : cleaned;
    }

    /// <summary>A KeepNote notebook was imported (or an import stopped half-way): show what is there now.</summary>
    private async Task AfterImportAsync(Guid notebookId)
    {
        _firstRun = false;
        await RunAsync("The imported notebook could not be shown.", async () =>
        {
            await LoadStructureAsync();
            if (notebookId != Guid.Empty && _notebooks.Any(n => n.Id == notebookId))
            {
                _notice = $"\"{_notebooks.First(n => n.Id == notebookId).Name}\" was imported.";
            }
        });
    }

    private void PromptNewFolder()
    {
        switch (_selection)
        {
            case NavSelection.Folder folder:
                PromptNewFolderIn(folder.NotebookId, folder.FolderId, Heading);
                break;
            case NavSelection.Notebook notebook when CanHoldFolders:
                PromptNewFolderIn(notebook.NotebookId, null, Heading);
                break;
        }
    }

    private void PromptNewFolderIn(Guid notebookId, Guid? parentId, string placeName) => OpenPrompt(new PromptRequest(
        "New folder", $"Name of the folder in \"{placeName}\"", string.Empty, "Create", Node.MaxNameLength,
        async name =>
        {
            var folder = await Tree.CreateFolderAsync(notebookId, parentId, name);
            await LoadStructureAsync();
            await GoToAsync(new NavSelection.Folder(folder.NotebookId, folder.Id));
        }));

    private void PromptRename()
    {
        switch (_selection)
        {
            case NavSelection.Folder folder:
                PromptRenameFolder(folder.FolderId, Heading);
                break;
            case NavSelection.Notebook notebook when CanChangeContainer:
                PromptRenameNotebook(notebook.NotebookId, Heading);
                break;
        }
    }

    private void PromptRenameFolder(Guid folderId, string currentName) => OpenPrompt(new PromptRequest(
        "Rename folder", "Name", currentName, "Rename", Node.MaxNameLength,
        async name =>
        {
            await Tree.RenameFolderAsync(folderId, name);
            await LoadStructureAsync();
        }));

    private void PromptRenameNotebook(Guid notebookId, string currentName) => OpenPrompt(new PromptRequest(
        "Rename notebook", "Name", currentName, "Rename", Notebook.MaxNameLength,
        async name =>
        {
            await Notebooks.RenameAsync(notebookId, name);
            await LoadStructureAsync();
        }));

    private void PromptRenameTag(Guid tagId, string currentName) => OpenPrompt(new PromptRequest(
        "Rename tag", "Name", currentName, "Rename", Domain.Tag.MaxNameLength,
        async name =>
        {
            await TagService.RenameAsync(tagId, name);
            await LoadTagsAsync();
        }));

    private void ConfirmDeleteContainer()
    {
        switch (_selection)
        {
            case NavSelection.Folder folder:
                ConfirmDeleteFolder(folder.FolderId, Heading);
                break;
            case NavSelection.Notebook notebook when CanChangeContainer:
                ConfirmDeleteNotebook(notebook.NotebookId, Heading);
                break;
        }
    }

    private void ConfirmDeleteFolder(Guid folderId, string name) => OpenConfirm(new ConfirmRequest(
        $"Delete folder \"{name}\"?",
        "The folder and everything in it move to the trash. You can restore them from there.",
        "Move to trash",
        Danger: false,
        async () =>
        {
            await Trash.MoveToTrashAsync(folderId);
            await AfterContainerDeletedAsync(name);
        }));

    private void ConfirmDeleteNotebook(Guid notebookId, string name) => OpenConfirm(new ConfirmRequest(
        $"Delete notebook \"{name}\"?",
        "The notebook and everything in it move to the trash. You can restore them from there.",
        "Move to trash",
        Danger: false,
        async () =>
        {
            await Trash.MoveNotebookToTrashAsync(notebookId);
            await AfterContainerDeletedAsync(name);
        }));

    private void ConfirmDeleteTag(Guid tagId, string name) => OpenConfirm(new ConfirmRequest(
        $"Delete tag \"{name}\"?",
        "The tag is removed from every note that carries it. The notes themselves stay as they are.",
        "Delete tag",
        Danger: true,
        async () =>
        {
            await TagService.DeleteAsync(tagId);
            await LoadTagsAsync();
            if (_selection is NavSelection.Tag current && current.TagId == tagId)
            {
                await GoToAsync(new NavSelection.AllNotes());
            }

            _notice = $"The tag \"{name}\" was deleted.";
        }));

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
        _move = null;
        _menu = null;
        _prompt = request;
    }

    private void OpenConfirm(ConfirmRequest request)
    {
        _dialogError = null;
        _prompt = null;
        _move = null;
        _menu = null;
        _confirm = request;
    }

    private void CloseDialogs()
    {
        _prompt = null;
        _confirm = null;
        _move = null;
        _menu = null;
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
        _connectionsVersion++;
        var index = _notes.FindIndex(n => n.Id == saved.Id);
        if (index < 0)
        {
            StateHasChanged();
            return;
        }

        var summary = _notes[index] with { Title = saved.Title, UpdatedAt = saved.UpdatedAt };
        if (_notes[index] == summary)
        {
            StateHasChanged();
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

    private sealed record MoveRequest(Guid NodeId, bool IsFolder, string Name, IReadOnlySet<Guid> Excluded);

    private sealed record MenuState(IReadOnlyList<ContextMenu.Item> Items, double X, double Y, string Label);
}
