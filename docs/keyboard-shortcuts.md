# Keyboard shortcuts

`Ctrl` on Windows and Linux, `⌘` on macOS. Application shortcuts are registered in one place:
`src/WorriorVex.UI/wwwroot/js/shortcuts.js`.

## Application

| Shortcut | Action |
| --- | --- |
| `Ctrl/⌘ + N` | New note (in the Inbox) |
| `Ctrl/⌘ + S` | Save now |
| `Ctrl/⌘ + K` | Search |
| `↑` `↓` `Enter` in the search box | Move through the results, open one |
| `Esc` in the search box | Leave the search |
| `Enter` in the title | Move to the note body |
| `Esc` | Close a dialog or context menu |
| `Enter` / `Backspace` in the tag row | Add a tag / remove the last tag |
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
| `Tab` / `Shift + Tab` in a table | Next / previous cell |
| `Ctrl/⌘` + click on a link | Open the link in the browser |
| `Ctrl/⌘ + V` with an image copied | Paste the image into the note |
| `Ctrl/⌘ + Z`, `Ctrl/⌘ + Shift + Z` | Undo, redo |

The editor shortcuts are TipTap's defaults. Typing `# `, `- `, `1. `, `[ ] `, `> ` or three backticks at
the start of a line also starts the matching block.

## Planned

Focus editor, toggle sidebar, tree navigation, command palette.
