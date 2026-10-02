# Decision: desktop host and UI technology

Status: accepted for the desktop MVP. Verified on macOS; Windows and Linux are built in CI and still need a
manual run (see [Validation](#validation)).

## Decision

- **UI:** Razor components in a shared class library, `WorriorVex.UI`.
- **Desktop host (Windows, macOS, Linux):** [Photino.Blazor](https://github.com/tryphotino/photino.Blazor)
  (Apache-2.0), a native window around the operating system's web view, with the components running
  in-process in .NET. No web server, no ports, no browser.
- **Mobile host (later):** .NET MAUI Blazor Hybrid, reusing `WorriorVex.UI` and the core unchanged.
- **Core:** `Domain`, `Application`, `Infrastructure` are plain .NET libraries with no UI or host dependency.

## Why

The editor decides the host. The plan requires a mature rich text editor with lists, checklists, tables,
images and paste/drop. Those exist as web components (ProseMirror/TipTap, MIT). No .NET-native toolkit has
an equivalent, so the options were judged first on "can it run a web editor on Linux".

| | Photino.Blazor | Avalonia | .NET MAUI + Blazor Hybrid | Blazor Web / PWA |
| --- | --- | --- | --- | --- |
| Windows | yes (WebView2) | yes | yes | browser |
| macOS | yes (WKWebView) | yes | yes (Catalyst) | browser |
| Linux | yes (WebKitGTK) | yes | **no official support** | browser |
| Android / iOS | no | yes | yes | browser |
| Works offline, no server | yes | yes | yes | only as a PWA, with browser storage limits |
| SQLite + EF Core on the local disk | yes | yes | yes | no (WASM sandbox, no real file) |
| File system access | full | full | full | restricted |
| Rich text editor | web editors (TipTap) | no mature WYSIWYG control | web editors | web editors |
| Drag/drop, shortcuts | web events in the view | native | web events in the view | browser-limited |
| Native integration (menus, tray) | basic | good | good | none |
| Packaging | self-contained `dotnet publish` per OS | same | per-platform tooling | hosting |
| Reuse of the UI on mobile | yes, via MAUI Blazor Hybrid | own mobile UI | yes | yes |

- **MAUI alone** fails the mandatory Linux requirement.
- **Blazor Web/PWA** fails local-first: no real SQLite file, no free file access, and it makes a browser
  or server part of the product.
- **Avalonia** covers every platform natively but has no rich text editor that meets the requirement;
  embedding a web view in it would bring back the same web view dependency with a second UI stack on top.
- **Photino.Blazor** is the only option that gives Linux, a real editor and one UI codebase that MAUI
  Blazor Hybrid can reuse on phones.

## Consequences and risks

- The app depends on the system web view: WebView2 on Windows (part of Windows 11, installable on 10),
  WKWebView on macOS, WebKitGTK on Linux (a distribution package). Rendering can differ slightly between
  them; the CSS avoids engine-specific features.
- Photino is a small project. The risk is contained: the host is one file (`Program.cs`). All behaviour
  is in the shared UI and core, so another web view host can replace it without touching them.
- Photino.Blazor 4.0 targets .NET 8 and runs on .NET 10 through roll-forward of its dependencies; verified
  by the proof of concept.
- Native menus, tray and file associations are limited in Photino and are behind abstractions when added.

## Validation

Proof of concept required by the plan: launch, initialise SQLite, create, save, reload and display a note.

| Platform | Build + automated tests | Manual run of the desktop app |
| --- | --- | --- |
| macOS (arm64) | passed | passed, 2 Oct 2026: database created from migrations, notes created and edited, present after restart |
| Windows | CI | **not yet done** |
| Linux | CI | **not yet done** |

The plan's gate ("do not proceed until the foundation is validated on all three") stays open until the two
manual runs are recorded here.
