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
| Domain and persistence for tags, attachments, links, revisions, trash (Phase 3) | done |
| Application services for all of the above, plus tree, move and basic search (Phase 4) | done, tested |
| Notebooks, nested folders, All Notes, delete and restore, trash screen (Phase 5) | done |
| About and Documentation pages, icon, installers for Windows, macOS, Linux | done |
| Links, images (file, paste, drop), tables, HTML sanitiser, content security policy (Phase 6) | done |
| Full-text search on SQLite FTS5 with a search box, highlighting and filters (Phase 7) | done |
| Tags, favourites, pins, Recent, move dialog, duplicate, context menus, drag and drop (Phase 8) | done |
| Screens for links and backlinks | Phase 9 |
| Screens for attachments | Phase 10 |
| KeepNote import | Phase 11 |
| Revision history screen | Phase 12 |
| Backup and export | Phase 13 |
| Mobile | Phase 14 |

## Out of scope for the MVP

Synchronisation, a web version, AI features, graph visualisation, scheduled backups, a command palette.
The data model keeps stable ids and created/updated/deleted timestamps so that sync can be added later.
