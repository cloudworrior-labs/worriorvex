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

The editor schema is not the only defence. Before HTML from outside (KeepNote import, paste, restore
from a package) is stored, it will go through a server-side allow-list sanitiser in the Application
layer (planned for Phase 6). Content already in the database is only ever rendered through the editor.

## Current state

Enabled today: StarterKit (paragraphs, headings, bold, italic, underline, strike, inline code, lists,
block quote, code block, horizontal rule, links, undo/redo) and task lists. Images, tables and the link
dialog arrive with Phases 6 and 10.
