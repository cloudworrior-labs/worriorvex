# Performance

Target from the plan: 10,000 notes without the app feeling slow. Measured on 3 October 2026 with
`tools/WorriorVex.Bench` (an Apple-silicon Mac, Release build, median of five after a warm-up),
on a data folder of 10,000 notes of about 240 words each, 100 notebooks × 10 folders × 10 notes,
a tag on every seventh note, 49 MB database:

| Operation | Time |
| --- | --- |
| Startup (migrations and index check) | 1.1 s |
| List all notes (10,000 rows) | 23 ms |
| List one folder | 0.3 ms |
| Loose ends (8,500 rows) | 21 ms |
| Folder tree | 1 ms |
| Open a note | 0.1 ms |
| Search, one word (50 results, ranked, highlighted) | 22 ms |
| Search, two words | 30 ms |
| Search limited to a notebook | 18 ms |
| "Did you mean" | 6 ms |
| Save a note (changed) | 6 ms |

What the measurements changed in the interface:

- The navigation tree starts with folders collapsed (notebooks open) and remembers what you opened,
  so a big tree renders about a hundred rows rather than ten thousand. The branch holding the
  selected folder opens itself when the selection moves.
- The note list is virtualised (`Virtualize`): only the rows in view exist in the page, so
  All Notes with 10,000 entries opens at once.

Rerun with `dotnet run -c Release --project tools/WorriorVex.Bench -- notes=10000`; the data
folder is reused between runs (`dir=<folder>` to choose another).
