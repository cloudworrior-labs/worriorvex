# Architecture

WorriorNotes is a desktop application first. There is no server: the UI, the application logic and the
database all run in one local process.

```text
 WorriorNotes.Desktop        Photino window + composition root (one file)
        │
 WorriorNotes.UI             Razor components, CSS design tokens, editor bundle (shared with mobile later)
        │
 WorriorNotes.Application    service interfaces, DTOs, NoteAutosaver
        │
 WorriorNotes.Domain         Notebook, Node, Note and their rules; no dependencies
        ▲
 WorriorNotes.Infrastructure EF Core + SQLite, migrations, data paths, file logger; implements Application
```

| Project | Responsibility | May reference |
| --- | --- | --- |
| `WorriorNotes.Domain` | Entities and invariants (names, soft delete, "UpdatedAt moves only on real change") | nothing |
| `WorriorNotes.Application` | What the app can do: `INoteService`, `INotebookService`, DTOs, autosave logic, `IApplicationDataPathProvider` | Domain |
| `WorriorNotes.Infrastructure` | How it is stored: `WorriorNotesDbContext`, migrations, service implementations, paths, logging | Application, Domain |
| `WorriorNotes.UI` | Components and assets; talks only to Application interfaces | Application |
| `WorriorNotes.Desktop` | Creates the window, registers services, runs migrations at startup, flushes edits on close | UI, Infrastructure |

`WorriorNotes.Web` and `WorriorNotes.Mobile` from the plan do not exist yet; they are added when needed,
not before. Host choice and editor choice are explained in [`decisions/`](decisions/).

## Rules

- The UI contains no persistence code and no business rules; it calls Application services.
- Entities are changed only through their own methods, which enforce the rules.
- Every service call opens a short-lived `DbContext` from a factory; nothing holds a context across calls.
- All I/O is async and takes a `CancellationToken`.
- Time comes from `TimeProvider`, so tests control the clock.
- The schema changes only through EF Core migrations, applied at startup. `EnsureCreated` is not used.
- Paths come from `IApplicationDataPathProvider`; no OS path is hard-coded.

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
| Windows | `%LOCALAPPDATA%\WorriorNotes` |
| macOS | `~/Library/Application Support/WorriorNotes` |
| Linux | `$XDG_DATA_HOME/WorriorNotes` or `~/.local/share/WorriorNotes` |

Set `WORRIORNOTES_DATA_DIR` to use another folder. Contents: `worriornotes.db`, `attachments/`,
`backups/`, `exports/`, `logs/`.

## Logging

Standard `Microsoft.Extensions.Logging`, written to one file per day in `logs/`. Startup, migrations and
unexpected errors are logged. Note titles and contents are never logged.

## Testing

| Project | Covers |
| --- | --- |
| `WorriorNotes.Domain.Tests` | entity rules |
| `WorriorNotes.Application.Tests` | autosave timing, failure and retry, with a fake clock |
| `WorriorNotes.IntegrationTests` | real SQLite files: migrations, note lifecycle, survival across a restart |

UI tests are not automated yet; the critical flows in the plan (section 40) are run by hand for now.
