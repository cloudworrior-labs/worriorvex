# Mobile: how Android and iOS will reuse the desktop code

Not started. This records the design so the desktop work does not paint mobile into a corner.

## Host

.NET MAUI Blazor Hybrid: a `BlazorWebView` inside a MAUI app, hosting the same `WorriorVex.UI` Razor
components that Photino hosts on the desktop. The core (`Domain`, `Application`, `Infrastructure`) runs
unchanged: SQLite, EF Core migrations, FTS5 and the file-based attachments all work on both platforms.

## What already keeps the UI portable

- No desktop-only dependency in `WorriorVex.UI`: everything the host must provide goes through
  `IPlatformShell` (open a page or file, pick a file or folder, choose a save location) and `AppInfo`.
  The MAUI host implements `IPlatformShell` with MAUI's `FilePicker`, `FolderPicker`, `Launcher` and `Share`.
- `IApplicationDataPathProvider` decides where data lives; on mobile it points at the app sandbox.
- Attachment images are served through the app's own origin (`AttachmentImageFileProvider`); the
  `BlazorWebView` takes the same `IFileProvider`.
- The content security policy and the sanitiser are host-independent.

## What changes on a phone

- **Navigation stack instead of three panes.** The plan is explicit: do not shrink the desktop layout.
  A mobile `MobileShell` component shows one pane at a time (navigation → list → editor) and keeps
  `Workspace`'s state logic; the plan is to lift that state out of `Workspace.razor.cs` into a
  `WorkspaceState` class shared by both shells.
- **Input.** Drag-and-drop and right-click become long-press menus and move dialogs (already present).
  Keyboard shortcuts become toolbar actions.
- **Dialogs** that use the system file pickers already run through `IPlatformShell`.
- **Background and lifecycle.** Flush the autosaver when the app is backgrounded (MAUI `Window.Deactivated`).

## Steps

1. Install the MAUI workload (`dotnet workload install maui`) with Xcode / Android SDK on the build machine.
2. `src/WorriorVex.Mobile`: MAUI Blazor Hybrid project referencing `WorriorVex.UI` and `WorriorVex.Infrastructure`.
3. Extract `WorkspaceState`; add `MobileShell`.
4. Implement `IPlatformShell` for MAUI; register the same services as `Program.cs` on the desktop.
5. Test the MVP journey on an Android emulator and an iOS simulator; add both to the release workflow.
