# WorriorNotes — Cross-Platform Product Development Plan

## 1. Product Vision

**WorriorNotes** is a modern, personal-first knowledge and note-taking application inspired by KeepNote.

The product should feel like:

- A modern replacement for KeepNote
- A personal digital notebook
- A lightweight knowledge base
- A place to capture ideas quickly
- A structured repository for projects and reference material

Core philosophy:

> Capture quickly. Organize naturally. Find everything. Own your data.

The application must work without AI, cloud services, or an internet connection.

---

# 2. Hard Requirements

## Platforms

Desktop support is mandatory for:

- Windows
- macOS
- Linux

The architecture must also be ready for:

- Android
- iOS

Mobile implementation may follow the desktop MVP.

## Local-first

The core application must work:

- Offline
- Without an account
- Without a remote database
- Without cloud services
- Without internet connectivity

## Data ownership

Users must be able to:

- Back up their data
- Export their notes
- Restore backups
- Migrate from KeepNote
- Leave the application without proprietary lock-in

---

# 3. Product Principles

1. **Local first**
2. **Fast capture**
3. **Search is first-class**
4. **Hierarchy remains important**
5. **Organization should not be mandatory during capture**
6. **Data must be portable**
7. **The application should remain useful without AI**
8. **Cross-platform behavior should be consistent**
9. **Never silently lose user data**
10. **Keep the architecture simple until complexity is justified**

---

# 4. Technology Strategy

Do not make ASP.NET Core Web the primary application host.

ASP.NET Core may be used for an optional web/server component, but the core application must be platform-independent.

Target current stable .NET, initially:

- .NET 10
- C#
- Entity Framework Core
- SQLite
- Shared application/domain layers
- Shared UI components where practical

Investigate these host strategies before committing:

1. Avalonia
2. .NET MAUI + Blazor Hybrid
3. Blazor Web/PWA
4. Other credible .NET cross-platform approaches if appropriate

Important:

.NET MAUI officially targets Windows, macOS, Android and iOS, but does not provide official Linux desktop support. Therefore MAUI alone does not satisfy the mandatory desktop requirement.

The selected architecture must provide a real Linux desktop solution.

Create:

`docs/decisions/cross-platform-host.md`

Compare:

- Windows
- macOS
- Linux
- Android
- iOS
- Offline operation
- SQLite
- Filesystem access
- Rich text editor
- Drag/drop
- Keyboard shortcuts
- Native integration
- Packaging
- Performance
- Future synchronization

---

# 5. Recommended Architecture

```text
                    WORRIORNOTES
                         |
               +---------+---------+
               |                   |
          Shared Core          Shared UI
               |                   |
        +------+-------+           |
        |              |           |
      Domain       Application     |
        |              |           |
        +------+-------+           |
               |                   |
        Infrastructure             |
               |                   |
      +--------+--------+----------+
      |        |        |
   Windows   macOS    Linux
      |
   Mobile later
```

Core layers:

```text
WorriorNotes.Domain
WorriorNotes.Application
WorriorNotes.Infrastructure
```

Presentation/host layers:

```text
WorriorNotes.UI
WorriorNotes.Desktop
WorriorNotes.Web
WorriorNotes.Mobile
```

Adjust the exact project structure if the selected UI framework makes a better arrangement possible.

The key requirement is architectural separation, not a fixed number of projects.

---

# 6. Repository Structure

Suggested:

```text
WorriorNotes/
|
+-- WorriorNotes.sln
|
+-- src/
|   +-- WorriorNotes.Domain/
|   +-- WorriorNotes.Application/
|   +-- WorriorNotes.Infrastructure/
|   +-- WorriorNotes.UI/
|   +-- WorriorNotes.Desktop/
|   +-- WorriorNotes.Web/
|   +-- WorriorNotes.Mobile/
|
+-- tests/
|   +-- WorriorNotes.Domain.Tests/
|   +-- WorriorNotes.Application.Tests/
|   +-- WorriorNotes.IntegrationTests/
|
+-- docs/
|   +-- architecture.md
|   +-- product.md
|   +-- database.md
|   +-- import-format.md
|   +-- keyboard-shortcuts.md
|   +-- keepnote-analysis.md
|   +-- decisions/
|
+-- scripts/
|
+-- README.md
+-- LICENSE
+-- .gitignore
```

Do not create unnecessary projects.

---

# 7. Domain Model

Initial entities:

```text
Notebook
Node
Note
Tag
NoteTag
Attachment
NoteLink
NoteRevision
```

A tree can use a Node model:

```text
Notebook
  |
  +-- Folder
  |     |
  |     +-- Folder
  |     +-- Note
  |
  +-- Note
```

A node should contain concepts such as:

```text
Id
NotebookId
ParentId
NodeType
Name
SortOrder
CreatedAt
UpdatedAt
```

Choose separate Folder/Note entities instead if that produces a cleaner long-term model.

Do not blindly reproduce KeepNote's internal representation.

---

# 8. Note Model

A note should support:

```text
Id
NodeId / ParentId
NotebookId
Title
Content
ContentFormat
CreatedAt
UpdatedAt
DeletedAt
IsFavorite
IsPinned
SortOrder
```

Initially support HTML content, but keep the content abstraction open to Markdown or another format later.

---

# 9. Tags

Use a many-to-many relationship:

```text
Tag
---
Id
Name
CreatedAt
```

```text
NoteTag
-------
NoteId
TagId
```

Examples:

```text
#project
#idea
#meeting
#programming
#reference
#todo
```

---

# 10. Attachments

Store attachment metadata in SQLite and physical files separately.

```text
Attachment
----------
Id
NoteId
OriginalFileName
StoredFileName
ContentType
Size
CreatedAt
StoragePath
Hash
```

Suggested storage:

```text
WorriorNotesData/
    worriornotes.db
    attachments/
    backups/
    exports/
```

Use generated identifiers for stored filenames.

Protect against:

- Path traversal
- Invalid filenames
- Unsafe content
- Oversized files
- Duplicate handling problems

---

# 11. Internal Links

Implement:

```text
NoteLink
--------
Id
SourceNoteId
TargetNoteId
CreatedAt
```

This enables:

```text
Note A
  |
  +--> Note B
```

and backlinks:

```text
Note B

Backlinks:
- Note A
- Architecture
- Meeting Notes
```

Do not implement a graph visualization in the MVP.

---

# 12. Revisions

Implement:

```text
NoteRevision
------------
Id
NoteId
Title
Content
CreatedAt
ChangeReason
```

Support:

- History list
- View revision
- Restore revision

Restoring a revision should itself be handled safely and should not destroy current content.

---

# 13. Trash

Use soft deletion:

```text
DeletedAt
```

instead of immediately deleting notes.

Trash must support:

- Restore
- Permanently delete
- Empty trash

Handle attachments correctly when a note is permanently removed.

---

# 14. Database

Use:

- SQLite
- EF Core
- EF Core migrations

Do not use `EnsureCreated()` as the production schema strategy.

Use migrations.

Database location must be platform-specific and configurable.

Never hard-code Windows/macOS/Linux paths.

Provide:

```csharp
IApplicationDataPathProvider
```

or equivalent abstraction.

---

# 15. Database Indexes

Index appropriately:

- Note title
- UpdatedAt
- CreatedAt
- DeletedAt
- NotebookId
- ParentId
- Tag name
- NoteTag composite key
- NoteLink source
- NoteLink target

---

# 16. Full-Text Search

Use SQLite FTS5 if practical.

Create:

```csharp
INoteSearchService
```

Search should cover:

- Note title
- Note content
- Tags
- Notebook/folder context
- Attachment names where practical

Eventually support syntax such as:

```text
tag:project
in:Projects
is:favorite
is:pinned
```

Do not overengineer the parser initially.

Search must not load the entire database into memory.

---

# 17. UI

Desktop layout:

```text
+--------------------------------------------------------------+
| WorriorNotes     Search...                     + New     ... |
+--------------+----------------------+------------------------+
|              |                      |                        |
| Navigation   | Note List            | Editor                 |
|              |                      |                        |
| Inbox        | Project Alpha        | Project Alpha          |
| All Notes    | Requirements         |                        |
| Favorites    | Meeting Notes        | # Architecture         |
| Recent       |                      | Content...             |
|              |                      |                        |
| NOTEBOOKS    |                      |                        |
| Development  |                      |                        |
| Personal     |                      |                        |
| Reference    |                      |                        |
|              |                      |                        |
| Trash        |                      |                        |
+--------------+----------------------+------------------------+
|                         ✓ Saved                              |
+--------------------------------------------------------------+
```

Mobile should use a navigation stack rather than squeezing the three-pane layout:

```text
Navigation
    ->
Note list
    ->
Editor
```

Do not simply shrink the desktop UI.

---

# 18. Visual Design

Do not use default framework styling.

The application should be:

- Modern
- Calm
- Professional
- Information-dense
- Low-noise
- Keyboard-friendly
- Accessible

Avoid:

- Excessive gradients
- Excessive cards
- Giant marketing-style sections
- Generic SaaS dashboard styling
- Excessive animations

The visual direction should feel closer to a modern desktop workspace such as VS Code/Obsidian than a marketing website.

Support:

- Light theme
- Dark theme
- System theme

---

# 19. Branding

Product name:

**WorriorNotes**

Use the exact spelling selected by the product owner unless explicitly changed.

Initial positioning:

> WorriorNotes — Your personal knowledge workspace.

The visual identity can subtly reference:

- focus
- discipline
- knowledge
- preparedness

Avoid an aggressive medieval/weapon aesthetic.

Potential icon direction:

- Abstract W
- Bookmark
- Notebook page
- Shield-like geometry

Create a simple initial SVG logo that can later be replaced.

---

# 20. Navigation

Initial navigation:

```text
Inbox
All Notes
Favorites
Recent

Notebooks
    Development
    Projects
    Personal
    Reference

Tags

Trash
```

Support:

- Create notebook
- Rename notebook
- Delete notebook
- Reorder notebook
- Create folder
- Rename folder
- Move folder
- Delete folder
- Move note

---

# 21. Note Tree

Support drag/drop where practical.

Also provide context menus:

```text
New Note
New Folder
Rename
Move
Duplicate
Favorite
Pin
Delete
Export
```

Do not make drag/drop the only way to move content.

---

# 22. Rich Text Editor

The editor is a core product component.

Support:

- Paragraphs
- Headings
- Bold
- Italic
- Underline
- Strikethrough
- Ordered lists
- Unordered lists
- Checklists
- Block quotes
- Code blocks
- Links
- Images
- Tables
- Horizontal rules

Also support:

- Paste images
- Drag/drop images
- Drag/drop files

Use a mature editor rather than implementing a rich text engine from scratch.

Evaluate licensing before selecting a library.

Document the decision in:

`docs/decisions/editor.md`

Sanitize all imported/rendered HTML.

---

# 23. Autosave

Autosave is mandatory.

Desired behavior:

```text
User types
    |
Debounce
    |
Save
    |
Update UI
    |
✓ Saved
```

During saving:

```text
Saving...
```

On failure:

```text
Unable to save

Retry
```

Never silently lose edits.

Keep unsaved content in memory if persistence fails.

---

# 24. Quick Capture / Inbox

New notes should default to Inbox unless the user explicitly selects another destination.

The user should be able to capture an idea without first deciding where to file it.

Potential shortcut:

```text
Ctrl+N
```

Add a command palette later.

---

# 25. Search UX

Global search should be prominent.

Example:

```text
+-------------------------------------------------------------+
| 🔎 Search notes...                                          |
+-------------------------------------------------------------+
```

Results:

```text
Project Architecture
Projects / WorriorNotes
Updated 10 minutes ago

...SQLite database and search architecture...
```

Support:

- Keyboard navigation
- Enter to open
- Esc to close
- Match highlighting where practical

---

# 26. Recent Notes

Track recent access separately from modification timestamps where necessary.

Do not modify `UpdatedAt` just because a note was opened.

---

# 27. Favorites and Pins

Favorites and pins are separate concepts.

Favorite:

> Make this note easy to access.

Pin:

> Keep this note prominent in its current context.

Do not treat them as aliases.

---

# 28. Settings

Initial settings:

```text
Appearance
    Theme: System / Light / Dark

Editor
    Font size
    Line height
    Spell check
    Autosave delay

Behavior
    Open last notebook
    Confirm deletion
    Start in Inbox

Storage
    Data location
    Backup
    Export
```

Do not expose unnecessary technical settings.

---

# 29. Keyboard Shortcuts

Centralize shortcut handling.

Initial actions:

- New note
- Search
- Force save
- Close search
- Focus editor
- Toggle sidebar
- Navigate tree
- Command palette later

Avoid browser/system conflicts.

Document shortcuts in:

`docs/keyboard-shortcuts.md`

---

# 30. KeepNote Migration

The supplied KeepNote archive:

`keepnote-0.7.8.tar.gz`

must be inspected before implementing the importer.

First document:

- Notebook format
- Folder representation
- Note format
- HTML storage
- Attachments
- Metadata
- Links
- Preferences
- Trash
- Version differences

Create:

`docs/keepnote-analysis.md`

Then implement a dedicated importer.

The importer must:

1. Validate source
2. Scan source
3. Produce import summary
4. Allow confirmation
5. Import notebooks
6. Import folders
7. Import notes
8. Import attachments
9. Translate supported metadata
10. Report unsupported data
11. Never modify the source
12. Produce an import report

Example:

```text
Import Complete

438 notes
37 folders
126 attachments
18 images

2 unsupported metadata fields ignored

No notes were lost.
```

---

# 31. KeepNote HTML

Treat imported HTML as untrusted.

Sanitize:

- Scripts
- Event handlers
- Unsafe URLs
- Dangerous embedded content

Preserve:

- Headings
- Paragraphs
- Lists
- Links
- Images
- Tables
- Emphasis
- Code where practical

---

# 32. Export

Design an export format from the beginning.

Possible package:

```text
.worriornotes
```

ZIP-based structure:

```text
manifest.json
notes/
attachments/
metadata/
```

Also support practical exports such as:

- HTML
- Markdown
- JSON

JSON must contain enough information to reconstruct notes.

Do not lock the user into a proprietary format.

---

# 33. Backup

Support:

```text
Backup Now
```

Later add scheduled backups.

Backup should include:

- Database
- Attachments
- Metadata
- Application version
- Schema version

Use a safe SQLite backup strategy rather than blindly copying a live database during writes.

---

# 34. Privacy

No analytics in the first release.

No telemetry by default.

No remote AI calls.

No cloud dependency.

No account requirement for local use.

Future telemetry must be explicit and opt-in.

---

# 35. Security

Treat all note content and uploaded files as untrusted.

Implement:

- HTML sanitization
- Path traversal protection
- File size limits
- Safe content types
- Safe attachment storage names
- Appropriate security headers
- Safe error handling

Never render arbitrary imported HTML unsanitized.

---

# 36. Accessibility

Target WCAG 2.2 AA principles where practical.

Ensure:

- Keyboard navigation
- Visible focus
- Semantic controls
- Accessible dialogs
- Accessible tree navigation
- Sufficient contrast
- Screen reader labels
- No keyboard-only dead ends

Do not rely on color alone to communicate state.

---

# 37. Performance

The application should remain responsive with:

- 10,000+ notes
- Large notebooks
- Many tags
- Many relationships
- Many attachments

Use:

- Async operations
- Efficient queries
- Cancellation tokens
- Lazy loading
- Virtualization where appropriate
- Debounced search

Do not prematurely optimize without measurements.

---

# 38. Error Handling

Never silently lose user work.

Provide actionable errors for:

- Save failure
- Search failure
- Import failure
- Attachment failure
- Backup failure
- Export failure

Do not expose stack traces to users.

---

# 39. Logging

Use standard .NET logging.

Log:

- Startup
- Migrations
- Imports
- Exports
- Backups
- Unexpected errors

Do not log full note contents or attachment contents.

---

# 40. Testing

## Domain tests

Test:

- Tree relationships
- Note lifecycle
- Soft deletion
- Restoration
- Tags
- Links
- Revisions

## Application tests

Test:

- Create note
- Update note
- Delete note
- Move note
- Search
- Tags
- Links
- Import
- Export
- Backup

## Integration tests

Test:

- SQLite
- EF mappings
- Migrations
- Note lifecycle
- Search
- Attachments
- KeepNote import

## UI tests

Test critical flows:

```text
Create note
Edit note
Reload
Verify persistence

Create folder
Create note
Move note
Reload
Verify hierarchy

Delete
Trash
Restore

Search
Open result
Edit
Verify save
```

---

# 41. Cross-Platform Proof of Concept

Before implementing the full UI, prove that the selected host technology can run on:

- Windows
- macOS
- Linux

The proof of concept must:

```text
Launch
    |
Initialize SQLite
    |
Create note
    |
Save note
    |
Reload note
    |
Display note
```

Do not proceed to full application development until the cross-platform foundation is validated.

---

# 42. Development Phases

## Phase 0 — KeepNote Analysis

Inspect the archive and produce:

```text
docs/keepnote-analysis.md
docs/product.md
docs/architecture.md
```

Do not guess about the old format.

## Phase 1 — Cross-Platform Technology Spike

Compare UI/host strategies.

Create:

`docs/decisions/cross-platform-host.md`

Build a minimal proof of concept for Windows/macOS/Linux.

## Phase 2 — Foundation

Create:

- Solution
- Domain
- Application
- Infrastructure
- UI
- Platform host(s)
- Test infrastructure
- SQLite
- EF Core
- Migrations

## Phase 3 — Domain + Persistence

Implement:

- Notebook
- Node
- Note
- Tags
- Attachments
- Links
- Revisions

## Phase 4 — Application Services

Implement:

```text
INoteService
INotebookService
ITreeService
ITagService
IAttachmentService
INoteLinkService
IRevisionService
ITrashService
ISearchService
```

## Phase 5 — Core UI

Implement:

- Three-pane desktop UI
- Navigation
- Tree
- Note list
- Editor
- Create
- Edit
- Delete
- Restore
- Autosave

## Phase 6 — Rich Editor

Implement the selected editor and sanitization.

## Phase 7 — Search

Implement SQLite FTS and search UI.

## Phase 8 — Organization

Implement:

- Tags
- Favorites
- Pins
- Recent
- Drag/drop
- Move
- Duplicate
- Context menus

## Phase 9 — Links

Implement:

- Internal links
- Backlinks
- Related notes

## Phase 10 — Attachments

Implement:

- Upload
- Download/open
- Delete
- Rename
- Images
- Drag/drop
- Paste images

## Phase 11 — KeepNote Import

Implement importer and automated tests using real KeepNote data.

## Phase 12 — Revisions

Implement:

- History
- Viewer
- Restore

## Phase 13 — Backup/Export

Implement:

- Backup
- Restore validation
- HTML export
- Markdown export
- JSON export
- Portable package

## Phase 14 — Cross-Platform Polish

Test:

- Windows
- macOS
- Linux

Then prepare mobile architecture and implementation.

## Phase 15 — Release Preparation

Implement:

- Installers/packages
- Versioning
- CI/CD
- Release notes
- Documentation
- Upgrade/migration handling

---

# 43. First Milestone

The first milestone is not the complete product.

It is:

```text
WorriorNotes launches.

Windows works.
macOS works.
Linux works.

SQLite works.

A note can be created.

A note can be edited.

A note survives restart.
```

Only then proceed to advanced features.

---

# 44. Mobile Strategy

After desktop MVP, implement mobile using the same core.

Desktop:

```text
Tree | Notes | Editor
```

Mobile:

```text
Navigation
    ↓
Notes
    ↓
Editor
```

Do not simply shrink the desktop interface.

The application should remain offline-first on mobile.

---

# 45. Future Synchronization

Do not implement synchronization in MVP.

However, keep the data model ready for it.

Use stable IDs.

Track appropriate:

```text
CreatedAt
UpdatedAt
DeletedAt
```

Future architecture may become:

```text
Desktop
    ↕
Sync Service
    ↕
Mobile
```

But local functionality must remain independent of synchronization.

---

# 46. Future AI

AI is optional and must not become a core dependency.

Potential future capabilities:

- Summarize note
- Rewrite note
- Extract tasks
- Suggest tags
- Find related notes
- Semantic search
- Ask questions about notes

All AI features should be optional.

---

# 47. Future Desktop Enhancements

Potential future features:

- Native menus
- System notifications
- File associations
- Open-with integration
- Multiple windows
- Global quick capture
- System tray
- Automatic updates

Implement platform-specific features behind abstractions.

---

# 48. Future Web Version

A web application may eventually reuse:

- Domain
- Application
- UI

but must not become a dependency of the local desktop product.

Potential future uses:

- Remote access
- Web editing
- Server synchronization

---

# 49. Code Quality

Use:

- Nullable reference types
- Async/await
- Cancellation tokens
- Dependency injection
- Appropriate interfaces
- Small focused classes
- Clear naming
- DTOs where useful
- Immutable models where appropriate

Avoid:

- Giant services
- Giant Razor components
- Business logic in UI
- Static global state
- Magic strings
- Duplicate persistence logic
- Unnecessary abstractions

---

# 50. UI Component Structure

Suggested:

```text
Components/
    Layout/
    Navigation/
    Notes/
        NoteList
        NoteEditor
        NoteToolbar
        NoteProperties
    Search/
    Dialogs/
    Attachments/
    Revisions/
    Tags/
    CommandPalette/
```

Do not put the application into one large component.

---

# 51. CSS

Create a coherent design system.

Define tokens for:

- Colors
- Spacing
- Typography
- Borders
- Radius
- Shadows
- Z-index
- Transitions

Use CSS custom properties where appropriate.

Support:

- Light
- Dark
- System

---

# 52. Application States

Important UI screens must support:

- Loading
- Empty
- Normal
- Saving
- Error
- Offline

Example:

```text
Nothing here yet.

Create your first note.
```

Search:

```text
No notes found.

Try a different search.
```

---

# 53. Git

Use meaningful commits:

```text
Initialize WorriorNotes solution
Add cross-platform proof of concept
Add domain model
Add SQLite persistence
Implement notebook tree
Implement note editor
Add autosave
Add search
Add attachments
Add KeepNote importer
Add revisions
Add backup/export
```

Never commit:

- Database files
- User attachments
- Secrets
- Build output
- IDE state

---

# 54. CI/CD

Use GitHub Actions or an equivalent CI platform.

At minimum:

```text
Pull Request
    ↓
Restore
    ↓
Build
    ↓
Unit tests
    ↓
Integration tests
    ↓
Platform builds
```

Build at least:

- Windows
- macOS
- Linux

Later:

- Android
- iOS

---

# 55. Release Packaging

Eventually provide:

## Windows

Appropriate installer/package.

## macOS

ARM64 and x64 where supported.

## Linux

Evaluate:

- AppImage
- Flatpak
- .deb

Select and document the initial release format.

## Mobile

Later:

- Android
- iOS

---

# 56. README

README must document:

- What WorriorNotes is
- Features
- Architecture
- Requirements
- Development
- Running locally
- Testing
- Database
- KeepNote import
- Backup
- Export
- Release process
- Roadmap
- License

Commands should include:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run
```

---

# 57. Definition of Done

A feature is complete only when:

- Code exists
- UI exists if required
- Tests exist
- Error handling exists
- Accessibility has been considered
- Documentation is updated
- Database migration exists if required
- Build succeeds
- Tests pass
- Restart behavior works
- No known compilation errors remain

---

# 58. Claude Code Operating Instructions

Before writing substantial code:

1. Inspect the repository
2. Inspect the KeepNote archive
3. Understand the existing format
4. Compare cross-platform host options
5. Document important decisions

Do not guess about KeepNote behavior.

When requirements are unambiguous:

```text
Inspect
→ Implement
→ Test
→ Fix
→ Document
```

After every significant phase:

```text
dotnet build
dotnet test
```

Fix failures before continuing.

Do not claim something works without testing it.

If an architectural decision has major long-term consequences, explain it before proceeding.

Prefer working vertical slices over large amounts of speculative code.

Keep the repository buildable throughout development.

---

# 59. KeepNote Source and Licensing

The KeepNote source archive is a functional and migration reference.

Before copying any code:

1. Inspect the license
2. Identify copyright notices
3. Determine whether copying is actually necessary
4. Prefer clean modern reimplementation
5. Preserve attribution/license requirements if source code is reused

Do not blindly copy KeepNote implementation code into WorriorNotes.

The objective is a modern application with compatible concepts and reliable migration, not a line-by-line port.

---

# 60. First User Experience

First launch:

```text
Welcome to WorriorNotes

A private place for your notes,
ideas and knowledge.

[ Create my first notebook ]

or

[ Import a KeepNote notebook ]
```

The first-run process should take less than a minute.

---

# 61. MVP User Journey

The MVP is complete when a user can:

```text
Launch WorriorNotes
        ↓
Create notebook
        ↓
Create folder
        ↓
Create note
        ↓
Write formatted content
        ↓
Automatic save
        ↓
Close application
        ↓
Reopen application
        ↓
Find note
        ↓
Search note
        ↓
Edit note
        ↓
Attach image
        ↓
Tag note
        ↓
Link another note
        ↓
See backlink
        ↓
Delete note
        ↓
Restore note
```

and all operations are reliable.

---

# 62. Final Architecture Principle

WorriorNotes is a **cross-platform application first**.

ASP.NET Core is an optional technology within the ecosystem, not the product architecture.

The architecture must allow:

```text
Internet OFF
Server OFF
Cloud OFF
```

while the local application continues to work normally.

The most important architectural goal is:

> The same user's notes, attachments, hierarchy, search, links, history and core behavior must work consistently across Windows, macOS and Linux without requiring a server.

