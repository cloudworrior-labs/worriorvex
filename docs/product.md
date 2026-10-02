# Product

**WorriorVex — your personal knowledge workspace.** A local-first notebook and knowledge base for
Windows, macOS and Linux, built as a modern replacement for KeepNote.

> Capture quickly. Organize naturally. Find everything. Own your data.

The full requirements are in [`WorriorVex-Development-Plan.md`](../WorriorVex-Development-Plan.md).
This page is the short version and records what exists today.

## Promises

- Works with the internet, any server and any cloud switched off. No account.
- No telemetry, no analytics, no remote AI calls.
- Your data is a SQLite file and a folder of attachments on your disk, with backup and open export formats.
- Edits are never lost silently: autosave, visible save state, retry on failure.
- KeepNote notebooks can be imported without modifying the original.

## Who it is for

Someone who keeps years of personal and project notes in a tree, wants fast capture into an Inbox,
full-text search, tags and links between notes, and does not want that knowledge to depend on a
subscription or a vendor.

## MVP journey

Launch → create notebook → create folder → create note → write formatted content → autosave → close →
reopen → find and search the note → edit → attach an image → tag → link another note → see the backlink →
delete → restore.

## Status

| Area | State |
| --- | --- |
| Desktop shell, SQLite with migrations, per-OS data folder, log file | done |
| Inbox, create / edit / list notes, rich text basics, autosave with retry | done |
| Notebooks, folders, tree, move, trash UI | next (Phases 3–5) |
| Images, tables, sanitiser | Phase 6 |
| Full-text search (FTS5) | Phase 7 |
| Tags, favourites, pins, recent | Phase 8 |
| Links and backlinks | Phase 9 |
| Attachments | Phase 10 |
| KeepNote import | Phase 11 |
| Revisions | Phase 12 |
| Backup and export | Phase 13 |
| Installers, mobile | Phases 14–15 |

## Out of scope for the MVP

Synchronisation, a web version, AI features, graph visualisation, scheduled backups, a command palette.
The data model keeps stable ids and created/updated/deleted timestamps so that sync can be added later.
