# WorriorVex

<img src="assets/icon/worriorvex-256.png" alt="WorriorVex icon" width="96" align="right" />

[![CI](https://github.com/cloudworrior-labs/worriorvex/actions/workflows/ci.yml/badge.svg)](https://github.com/cloudworrior-labs/worriorvex/actions/workflows/ci.yml)

**Your personal knowledge workspace.** A local-first note-taking app for Windows, macOS and Linux, built
as a modern replacement for [KeepNote](http://keepnote.org).

> Capture quickly. Organize naturally. Find everything. Own your data.

WorriorVex works with the internet, servers and the cloud switched off. There is no account, no
telemetry and no AI dependency. Your notes live in a SQLite file on your own disk.

> **Status: feature-complete for the desktop MVP, not yet widely tested.** Phases 0–13 of the plan are
> built. Back up before trusting it with the only copy of anything.

## Install

Download the latest version from the [Releases page](https://github.com/cloudworrior-labs/worriorvex/releases/latest).

| System | File | Notes |
| --- | --- | --- |
| Windows 10/11, 64-bit | `WorriorVex-Setup-<version>-x64.exe` | Installs for your user; no administrator rights needed |
| macOS, Apple silicon | `WorriorVex-<version>-macos-arm64.dmg` | Drag to Applications |
| macOS, Intel | `WorriorVex-<version>-macos-x64.dmg` | Drag to Applications |
| Debian / Ubuntu | `worriorvex_<version>_amd64.deb` | `sudo apt install ./worriorvex_<version>_amd64.deb` |
| Other Linux | `WorriorVex-<version>-linux-x64.tar.gz` | Unpack and run `./WorriorVex`; needs WebKitGTK 4.1 |

With Homebrew on macOS:

```bash
brew tap cloudworrior-labs/worriorvex https://github.com/cloudworrior-labs/worriorvex
brew trust cloudworrior-labs/worriorvex   # recent Homebrew asks you to trust a third-party tap once
brew install --cask worriorvex
```

The installers are not code-signed yet, so the operating system asks for confirmation the first time:

- **Windows:** SmartScreen shows "Windows protected your PC". Choose **More info**, then **Run anyway**.
- **macOS:** right-click the app and choose **Open**, or run
  `xattr -dr com.apple.quarantine /Applications/WorriorVex.app`.

## What works today

- Desktop app with a three-pane workspace, light and dark theme following the system
- Inbox for quick capture, notebooks, nested folders, an All Notes list
- Rich text: headings, bold, italic, underline, strikethrough, lists, checklists, quotes, code, rules
- Links, tables, and images added from a file, by pasting or by dropping
- Everything stored is sanitised; the app window can load nothing from the network
- Full-text search as you type, with highlighted matches, phrases and `tag:` / `in:` filters
- Tags, favourites, pinned notes, a Recent list; move by drag-and-drop or dialog; duplicate; right-click menus
- Links between notes, with backlinks and related notes shown under each note
- Attach any file to a note; open it, save a copy, rename or remove it
- Import a KeepNote notebook (format 5 and 6): folders, pages, files, pictures, links and trash, with a report
- Backup to one zip (consistent even while you work), restore with a safety backup first
- Export everything or one note as HTML, Markdown or JSON, or the whole workspace as a `.worriorvex` package
- Autosave with a visible save state; a failed save keeps your edits and offers Retry
- Trash for notes, folders and whole notebooks: restore, delete permanently, empty
- Earlier versions of a note are kept automatically while you edit; History shows and restores them
- Built-in Documentation and About pages
- SQLite database created and upgraded by migrations; per-OS data folder; log file

## Roadmap

Cross-platform polish and installers signing → Android and iOS.
Details: [`docs/product.md`](docs/product.md) and the full
[development plan](WorriorVex-Development-Plan.md).

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
dotnet run --project src/WorriorVex.Desktop
```

To try it without touching your real notes, point it at a scratch folder:

```bash
WORRIORVEX_DATA_DIR=/tmp/worriorvex-dev dotnet run --project src/WorriorVex.Desktop
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

Default data folder: `%LOCALAPPDATA%\WorriorVex` (Windows), `~/Library/Application Support/WorriorVex`
(macOS), `~/.local/share/WorriorVex` (Linux).

## Testing

`dotnet test` runs domain tests, application tests (autosave with a fake clock) and integration tests
against real SQLite files, including a restart. CI builds and tests on Windows, macOS and Linux.

## KeepNote import, backup, export

**Import:** Import & backup → Choose a KeepNote notebook folder. The folder is scanned first and a summary
shown; the import itself runs only after confirmation and ends with a report. The source is never changed.
Format analysis and importer rules: [`docs/keepnote-analysis.md`](docs/keepnote-analysis.md).

**Backup:** Import & backup → Back up now (to the `backups` folder) or Save a backup to…. The database is
copied with SQLite's backup API, so a backup taken while the app runs is consistent. **Restore** checks the
file (zip, manifest, database integrity, schema not newer than the app) and writes a safety backup of the
current data before replacing anything.

**Export:** every note as HTML, Markdown or JSON into a folder, keeping the notebook/folder structure and the
links between notes, or everything as a `.worriorvex` package (manifest, notebooks, nodes, tags, one JSON per
note with its links and history, and the attachment files). One note can be exported from its context menu.

## Keyboard shortcuts

[`docs/keyboard-shortcuts.md`](docs/keyboard-shortcuts.md)

## Release process

Pushing a version tag (`v0.2.0`) builds the Windows installer, the macOS disk images and the Linux
packages and publishes them as a GitHub release: [`docs/release.md`](docs/release.md).

## Contributing

Issues and pull requests are welcome. Please keep the build warning-free (`TreatWarningsAsErrors` is on)
and add tests for behaviour you change.

## Licence

[MIT](LICENSE), © 2026 Musa Consulting. More at [www.cloudworrior.com](https://www.cloudworrior.com). Third-party components: [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
KeepNote is a separate GPL project by Matt Rasmussen; WorriorVex contains none of its code.
