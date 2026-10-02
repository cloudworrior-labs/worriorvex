# Decision: rich text editor

Status: accepted.

## Decision

[TipTap](https://tiptap.dev) 3 (open-source core and extensions, MIT), which is built on ProseMirror (MIT).
It is bundled with esbuild into one local ES module; nothing is loaded from the network.

## Why

- Covers the required features with maintained extensions: headings, bold/italic/underline/strike,
  ordered, unordered and task lists, block quotes, code blocks, links, images, tables, horizontal rules.
- Schema-based: content is parsed into a typed document. Markup the schema does not know (scripts,
  event handlers, unknown elements and attributes) cannot enter the document, which is a strong first
  layer for handling untrusted HTML.
- Headless: no imposed toolbar or theme, so the UI follows the WorriorVex design system and stays
  accessible on our terms.
- Permissive licences throughout (MIT), compatible with any licence chosen for WorriorVex.

Alternatives considered: Quill 2 (BSD-3, simpler but weaker tables and task lists), CKEditor 5 (GPL or
commercial), TinyMCE (GPL or commercial since v7), Lexical (MIT, younger extension ecosystem). Only the
MIT/BSD options keep licensing simple; TipTap has the widest feature coverage among them. TipTap also
sells paid "Pro" extensions; none are used and none are needed for the plan.

## Storage format

Notes are stored as HTML (`ContentFormat.Html`) exactly as the editor serialises it. HTML is portable,
exports directly, and matches KeepNote's format for import. `ContentFormat` leaves room for Markdown.

## Sanitisation

Two layers, because the editor is not the only way content arrives.

1. **Editor schema.** Markup the schema does not know never enters the document. The image node only
   accepts the address of an attachment, so pasted web pages lose their remote images at this point.
2. **`INoteHtmlSanitizer`**, applied by `NoteService` to everything that is stored, whoever the caller is
   (editor, importer, restore). It is an allow-list built on
   [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) (MIT, on AngleSharp): the tags and attributes
   the editor writes, nothing else. No `style`, no event handlers, no scripts, frames, forms or embedded
   content. Links must be absolute `http`/`https` addresses and get `rel="noopener noreferrer nofollow"`;
   any other address leaves the text and drops the link. An `img` survives only when its source is
   `attachments/<32 hex>.<png|jpg|jpeg|gif|webp>`.

Writing our own sanitiser was rejected: HTML parsing has too many edge cases to get right by hand, and
this library is the established one for .NET.

The window itself carries a Content-Security-Policy (`default-src 'self'`, no remote sources, no inline
script), so even content that slipped through could not load or send anything over the network.

## Links between notes

A link to another note is an ordinary anchor with the address `note:<id>` (the note's GUID). The
sanitiser accepts exactly that form; the editor's link extension allows the scheme; the app, not the
web view, follows it. `NoteLinks` rows are derived from the text on every save, so backlinks never go
stale. The link text is fixed when the link is made and does not follow a later rename of the target.

## Images

Images are attachments: the file is copied into the attachments folder under a generated name and the
note refers to it as `attachments/<stored name>`. The desktop host serves exactly those files to its own
window through `AttachmentImageFileProvider`, on the app's own origin; no web server is involved.
Images are added from a native file dialog, by pasting, or by dropping a file on the note.
Accepted types: PNG, JPEG, GIF, WebP (SVG is refused because it can carry script).

Known gap: removing an image from a note leaves its file in the attachments folder until the note is
deleted permanently. The attachments screen (Phase 10) will show and clean these up.

## Current state

Enabled: StarterKit (paragraphs, headings, bold, italic, underline, strike, inline code, lists, block
quote, code block, horizontal rule, links, undo/redo), task lists, images and tables (insert, add and
delete rows and columns, header row). Not yet: resizing images and columns, merging cells.
