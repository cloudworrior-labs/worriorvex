# WorriorVex

<img src="assets/icon/worriorvex-256.png" alt="WorriorVex icon" width="96" align="right" />

[![CI](https://github.com/cloudworrior-labs/worriorvex/actions/workflows/ci.yml/badge.svg)](https://github.com/cloudworrior-labs/worriorvex/actions/workflows/ci.yml)

**Your personal knowledge workspace.** A local-first note-taking app for Windows, macOS and Linux, built
as a modern replacement for [KeepNote](http://keepnote.org).

> Capture quickly. Organize naturally. Find everything. Own your data.

WorriorVex works with the internet, servers and the cloud switched off. There is no account, no
telemetry and no AI dependency. Your notes live in a SQLite file on your own disk.

> **Status: early development.** Usable for writing and organising notes; search, tags, attachments and
> import do not have screens yet. Keep a backup of anything important.

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
- Autosave with a visible save state; a failed save keeps your edits and offers Retry
- Trash for notes, folders and whole notebooks: restore, delete permanently, empty
- Earlier versions of a note are kept automatically while you edit
- Built-in Documentation and About pages
- SQLite database created and upgraded by migrations; per-OS data folder; log file

Built and tested underneath, without a screen yet: tags, links and backlinks, attachments, restoring
earlier versions, moving notes and folders, basic search.

## Roadmap

Images and tables → full-text search → tags, favourites, pins, move → links and backlinks → attachments →
KeepNote import → revision history → backup and export → Android and iOS.
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

Not implemented yet. The KeepNote notebook format has been analysed in
[`docs/keepnote-analysis.md`](docs/keepnote-analysis.md); the importer, backup and the open export formats
(HTML, Markdown, JSON, `.worriorvex` package) are on the roadmap.

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
