# Changelog

## Unreleased

- Notes appear as leaves of the navigation tree under their notebook and folder (Settings → Appearance to turn off).
- Rename notebooks, folders and notes in place: double-click or F2.
- Keyboard navigation of the tree: arrows, Home/End, Delete.
- Sort the note list by last change, newest or title; compact list density.
- Word count, reading time and last-saved time in the status bar.
- Editor: find & replace (Ctrl/⌘+F), highlight marker, callout boxes (info, tip, warning, danger),
  code blocks coloured by language, images resized by dragging a corner, table columns resized by
  dragging, merge and split cells, type `[[` to link to another note, cleaner paste from Word,
  Google Docs and web pages.
- Search: limit a search to the notebook or folder you were in ("Only in …"), save searches to the
  navigation pane, and "Did you mean …?" when a word is a letter or two off.
- Lists show how many links a note has; a "Loose ends" view lists notes with no tag and no link.
- Note templates: Ctrl/⌘+Shift+N (or the ▾ next to + New) makes a note from one of the notes in the
  "Templates" notebook, which is created with three starters; `{{date}}` becomes today's date.

## 0.4.0 — 2026-10-03

- Notebooks grow like a tree: a **+** at the end of every notebook and folder row adds a folder inside it,
  and the box at the left shows **−** when a branch is open and **+** when it is collapsed. The branch
  that holds the selected folder always opens.

## 0.3.0 — 2026-10-03

- The navigation and note-list panes can be resized by dragging their right edge (or with the arrow
  keys when the edge has focus); the widths are remembered. Long notebook names show in full on hover.
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
