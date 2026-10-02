# Keyboard shortcuts

`Ctrl` on Windows and Linux, `⌘` on macOS. Application shortcuts are registered in one place:
`src/WorriorVex.UI/wwwroot/js/shortcuts.js`.

## Application

| Shortcut | Action |
| --- | --- |
| `Ctrl/⌘ + N` | New note (in the Inbox) |
| `Ctrl/⌘ + S` | Save now |
| `Enter` in the title | Move to the note body |
| `Esc` | Close a dialog |
| `Enter` in a dialog | Confirm the name |

## Editor

| Shortcut | Action |
| --- | --- |
| `Ctrl/⌘ + B` / `I` / `U` | Bold / italic / underline |
| `Ctrl/⌘ + Shift + S` | Strikethrough |
| `Ctrl/⌘ + E` | Inline code |
| `Ctrl/⌘ + Alt + 1…3` | Heading 1–3 |
| `Ctrl/⌘ + Shift + 7` / `8` / `9` | Numbered list / bulleted list / checklist |
| `Ctrl/⌘ + Shift + B` | Block quote |
| `Ctrl/⌘ + Alt + C` | Code block |
| `Tab` / `Shift + Tab` in a list | Indent / outdent |
| `Ctrl/⌘ + Z`, `Ctrl/⌘ + Shift + Z` | Undo, redo |

The editor shortcuts are TipTap's defaults. Typing `# `, `- `, `1. `, `[ ] `, `> ` or three backticks at
the start of a line also starts the matching block.

## Planned

Search (`Ctrl/⌘ + K`), focus editor, toggle sidebar, tree navigation, command palette.
