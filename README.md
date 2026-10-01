# WorriorNotes

[![CI](https://github.com/cloudworrior-labs/worriornotes/actions/workflows/ci.yml/badge.svg)](https://github.com/cloudworrior-labs/worriornotes/actions/workflows/ci.yml)

**Your personal knowledge workspace.** A local-first note-taking app for Windows, macOS and Linux, built
as a modern replacement for [KeepNote](http://keepnote.org).

> Capture quickly. Organize naturally. Find everything. Own your data.

WorriorNotes works with the internet, servers and the cloud switched off. There is no account, no
telemetry and no AI dependency. Your notes live in a SQLite file on your own disk.

> **Status: early development.** The foundation works (see below); it is not yet ready for daily use.

## What works today

- Desktop app with a three-pane workspace, light and dark theme following the system
- Notes in an Inbox: create, edit, list
- Rich text: headings, bold, italic, underline, strikethrough, lists, checklists, quotes, code, rules
- Autosave with a visible save state; a failed save keeps your edits and offers Retry
- SQLite database created and upgraded by migrations; notes survive a restart
- Per-OS data folder, log file

## Roadmap

Notebooks, folders and trash → images and tables → full-text search → tags, favourites, pins → links and
backlinks → attachments → KeepNote import → revisions → backup and export → installers → Android and iOS.
Details: [`docs/product.md`](docs/product.md) and the full
[development plan](WorriorNotes-Development-Plan.md).

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows: WebView2 runtime (included in Windows 11)
- Linux: WebKitGTK (for example `libwebkit2gtk-4.1` on Debian/Ubuntu)
- macOS: nothing extra
- Node.js 20+ only if you change the editor bundle

## Running locally

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/WorriorNotes.Desktop
```

To try it without touching your real notes, point it at a scratch folder:

```bash
WORRIORNOTES_DATA_DIR=/tmp/worriornotes-dev dotnet run --project src/WorriorNotes.Desktop
```

## Architecture

```text
Desktop (Photino window) → UI (Razor components) → Application (services) → Domain
                                                         ▲
                                              Infrastructure (EF Core + SQLite)
```

One process, no server. The UI is a shared Razor component library hosted in a native window through
Photino.Blazor; the same library is intended for Android and iOS through .NET MAUI Blazor Hybrid.
See [`docs/architecture.md`](docs/architecture.md) and the decision records for the
[host](docs/decisions/cross-platform-host.md) and the [editor](docs/decisions/editor.md).

## Database

SQLite through EF Core, schema managed by migrations: [`docs/database.md`](docs/database.md).

Default data folder: `%LOCALAPPDATA%\WorriorNotes` (Windows), `~/Library/Application Support/WorriorNotes`
(macOS), `~/.local/share/WorriorNotes` (Linux).

## Testing

`dotnet test` runs domain tests, application tests (autosave with a fake clock) and integration tests
against real SQLite files, including a restart. CI builds and tests on Windows, macOS and Linux.

## KeepNote import, backup, export

Not implemented yet. The KeepNote notebook format has been analysed in
[`docs/keepnote-analysis.md`](docs/keepnote-analysis.md); the importer, backup and the open export formats
(HTML, Markdown, JSON, `.worriornotes` package) are on the roadmap.

## Keyboard shortcuts

[`docs/keyboard-shortcuts.md`](docs/keyboard-shortcuts.md)

## Release process

No releases yet. Installers and packaging are planned for the release-preparation phase.

## Contributing

Issues and pull requests are welcome. Please keep the build warning-free (`TreatWarningsAsErrors` is on)
and add tests for behaviour you change.

## Licence

[MIT](LICENSE). Third-party components: [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
KeepNote is a separate GPL project by Matt Rasmussen; WorriorNotes contains none of its code.
