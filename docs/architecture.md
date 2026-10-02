# Architecture

WorriorVex is a desktop application first. There is no server: the UI, the application logic and the
database all run in one local process.

```text
 WorriorVex.Desktop        Photino window + composition root (one file)
        │
 WorriorVex.UI             Razor components, CSS design tokens, editor bundle (shared with mobile later)
        │
 WorriorVex.Application    service interfaces, DTOs, NoteAutosaver
        │
 WorriorVex.Domain         Notebook, Node, Note and their rules; no dependencies
        ▲
 WorriorVex.Infrastructure EF Core + SQLite, migrations, data paths, file logger; implements Application
```

| Project | Responsibility | May reference |
| --- | --- | --- |
| `WorriorVex.Domain` | Entities and invariants (names, soft delete, "UpdatedAt moves only on real change") | nothing |
| `WorriorVex.Application` | What the app can do: `INoteService`, `INotebookService`, DTOs, autosave logic, `IApplicationDataPathProvider` | Domain |
| `WorriorVex.Infrastructure` | How it is stored: `WorriorVexDbContext`, migrations, service implementations, paths, logging | Application, Domain |
| `WorriorVex.UI` | Components and assets; talks only to Application interfaces | Application |
| `WorriorVex.Desktop` | Creates the window, registers services, runs migrations at startup, flushes edits on close | UI, Infrastructure |

`WorriorVex.Web` and `WorriorVex.Mobile` from the plan do not exist yet; they are added when needed,
not before. Host choice and editor choice are explained in [`decisions/`](decisions/).

## Application services

| Service | Does |
| --- | --- |
| `INoteService` | create, open, list and save notes; keeps an earlier version when due |
| `INotebookService` | Inbox, create, rename and reorder notebooks |
| `ITreeService` | folders; move folders and notes, within and between notebooks |
| `ITrashService` | move to trash, list, restore, delete permanently, empty; removes attachment files |
| `ITagService` | tags on notes, rename, delete, counts |
| `INoteLinkService` | links and backlinks |
| `IRevisionService` | list, view and restore earlier versions |
| `IAttachmentService` | store, open, rename and delete attached files under generated names |
| `INoteSearchService` | full-text search over the FTS5 index: ranking, highlighting, `tag:` / `in:` / `is:` filters |
| `INoteHtmlSanitizer` | reduces HTML to what a note may contain; applied to everything stored |
| `IPlatformShell` | open a web page or folder outside the app, show a file dialog; implemented by the host |

Only folders contain other nodes. The services enforce it; the database does not.

## Rules

- The UI contains no persistence code and no business rules; it calls Application services.
- Entities are changed only through their own methods, which enforce the rules.
- Every service call opens a short-lived `DbContext` from a factory; nothing holds a context across calls.
- All I/O is async and takes a `CancellationToken`.
- Time comes from `TimeProvider`, so tests control the clock.
- The schema changes only through EF Core migrations, applied at startup. `EnsureCreated` is not used.
- Paths come from `IApplicationDataPathProvider`; no OS path is hard-coded.
- Note content is untrusted. It is sanitised before it is stored, and the window's
  Content-Security-Policy allows nothing from outside the app (see [`decisions/editor.md`](decisions/editor.md)).

## Autosave

```text
keystroke → editor (250 ms batch) → NoteEditor → NoteAutosaver.Edit → 700 ms pause → INoteService.UpdateAsync
```

`NoteAutosaver` keeps the latest edit in memory until a save succeeds. On failure the state becomes
`Failed`, the status bar says so and offers Retry, and the app refuses to switch notes until the edit is
safe. Closing the window flushes pending edits first. `Ctrl/Cmd+S` forces a save.

## Data location

| OS | Default folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\WorriorVex` |
| macOS | `~/Library/Application Support/WorriorVex` |
| Linux | `$XDG_DATA_HOME/WorriorVex` or `~/.local/share/WorriorVex` |

Set `WORRIORVEX_DATA_DIR` to use another folder. Contents: `worriorvex.db`, `attachments/`,
`backups/`, `exports/`, `logs/`.

## Logging

Standard `Microsoft.Extensions.Logging`, written to one file per day in `logs/`. Startup, migrations and
unexpected errors are logged. Note titles and contents are never logged.

## Testing

| Project | Covers |
| --- | --- |
| `WorriorVex.Domain.Tests` | entity rules |
| `WorriorVex.Application.Tests` | autosave timing, failure and retry, with a fake clock |
| `WorriorVex.IntegrationTests` | real SQLite files: migrations, note lifecycle, survival across a restart |

UI tests are not automated yet; the critical flows in the plan (section 40) are run by hand for now.
