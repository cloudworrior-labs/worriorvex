# Changelog

## 0.5.3 — 2026-10-04

- Deleting is unmistakable now: the buttons above the note list read **Delete folder…** / **Delete
  notebook…** (and **Rename folder…** / **Rename notebook…**), the confirmation says "the whole folder,
  not one note" and counts what goes, and the note's own button reads **Delete note**. Nothing is ever
  lost either way: everything deleted sits in the Trash until you restore or remove it.
- New notes join at the bottom of their notebook or folder: the list and the tree now default to
  "In the order added"; the other orders remain in the list's dropdown.
- The tree drops a deleted note at once.

## 0.5.2 — 2026-10-04

- The + on a notebook or folder row now offers **New note** as well as **New folder**, so any place can
  hold as many notes as you like straight from the tree; choosing a note in the tree selects its folder,
  so "+ New" adds the next note beside it.

## 0.5.1 — 2026-10-03

- Fixed: after changing the language (or any time the workspace was rebuilt) the navigation pane's
  edge no longer followed the mouse. Both pane edges now re-attach whenever the page changes.
- Fixed: dragging the note list's edge measured from the wrong side and collapsed the list.

## 0.5.0 — 2026-10-03

- The interface and the built-in documentation are available in English, Dutch, Polish and German
  (Settings → Language; follows the system language by default).
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
- Automatic backups: daily or weekly into the backups folder while the app is open, keeping the newest N.
- Import a `.worriorvex` package from another computer, or a folder of Markdown / text files (Obsidian,
  Joplin): folders, images, `[[wiki links]]`, front-matter and `#inline` tags come along.
- "Check attached files" on the Import & backup page: missing, changed and unused files.
- Appearance: accent colour, editor font (system, serif, sans, mono), two high-contrast themes, and a
  focus mode (Ctrl/⌘+Shift+F) that shows only the note.
- Big notebooks: folders start collapsed and the tree remembers what you opened; the note list only
  renders the rows in view. Measured with 10,000 notes (docs/performance.md).
- Open a `.worriorvex` package with WorriorVex (file association on Windows and Linux; command line
  everywhere) and it is imported straight away.
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
