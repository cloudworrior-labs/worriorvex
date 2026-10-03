# Changelog

## Unreleased

- Optional check for a new version (Settings → Updates, off by default; About → Check now).
- Homebrew: the cask moved to its own tap, `brew tap cloudworrior-labs/tap`.

## 0.2.0 — 2026-10-03

Everything since the first release.

### Writing
- Links to web pages, pictures (from a file, pasted or dropped) and tables in notes.
- Links between notes with a note picker; Ctrl/⌘+click follows them. *Links to*, *Linked from* and
  *Related by tags* under each note.
- History: earlier versions of a note are kept while you edit and can be viewed and restored.
- Attach any file to a note; open it, save a copy, rename or remove it; unused pictures can be cleaned up.

### Finding and organising
- Full-text search as you type, with highlighted matches, phrases and `tag:` / `in:` filters.
- Tags, favourites, pinned notes, a Recent list; move notes and folders by drag-and-drop or dialog;
  duplicate; right-click menus.

### Your data
- Import a KeepNote notebook (format 5 and 6) with a scan, a confirmation and a report.
- Backup to one zip, restore with a safety backup first, export as HTML, Markdown, JSON or a
  `.worriorvex` package.
- Settings: theme, text size, line height, spell check, autosave pause, confirm deletion, open where you left off.

### Safety
- Everything stored passes an allow-list sanitiser; the window loads nothing from the network.
- Script addresses carry the build version, so an upgrade never runs an old script.

## 0.1.0 — 2026-10-02

First release: Inbox, notes with autosave, notebooks, folders, trash, About and Documentation pages,
installers for Windows, macOS and Linux.
