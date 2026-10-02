# KeepNote format analysis

Source: `keepnote-0.7.8.tar.gz` (KeepNote 0.7.8, Python 2 / PyGTK, by Matt Rasmussen). Everything below was
read from that source; the file each fact comes from is named so it can be checked. The archive contains no
sample notebook, so the points listed under [Open questions](#open-questions) must be confirmed against a
real notebook before the importer is written.

## Licence

KeepNote is licensed under the GPL version 2 (`LICENSE`, `COPYING`). WorriorVex uses the archive only as a
description of the on-disk format. No KeepNote code is copied or translated into this repository, and the
archive itself is not redistributed here (it is git-ignored).

## A notebook is a directory tree

Format version 6 (`NOTEBOOK_FORMAT_VERSION` in `keepnote/notebook/__init__.py`). A notebook is a directory;
that directory is also the root node.

```text
MyNotebook/                  root node
  node.xml                   metadata of the root node
  notebook.nbk               notebook preferences
  __NOTEBOOK__/              notebook-level data, not a node
    icons/                   custom icons
    index.sqlite             search/lookup cache, can be rebuilt
    lost_found/              files KeepNote could not place
    orphans/                 nodes whose parent is missing
  __TRASH__/                 the trash folder (a node)
  Some folder/               a child node
    node.xml
    A page/
      node.xml
      page.html              the note body
      diagram.png            a file belonging to the page
```

- A subdirectory is a child node if, and only if, it contains `node.xml`
  (`iter_child_node_paths` in `keepnote/notebook/connection/fs/__init__.py`).
- Directory names are derived from titles (slashes become `-`, the characters ``* ? ' & < > | ` : ;`` are
  removed, a leading `__` is stripped, names are limited to 40 characters) and made unique with a numeric
  suffix. The name is therefore not the title; the `title` attribute is.
- `index.sqlite` is a cache. The importer ignores it and reads the directories.

## Node metadata: `node.xml`

Written by `_write_attr` in `connection/fs/__init__.py`: a `<node>` element holding `<version>` and one
property-list dictionary (`keepnote/plist.py`: `dict`, `key`, `string`, `integer`, `real`, `true`, `false`,
`array`).

```xml
<?xml version="1.0" encoding="UTF-8"?>
<node>
<version>6</version>
<dict>
  <key>nodeid</key><string>b810760f-…</string>
  <key>content_type</key><string>text/xhtml+xml</string>
  <key>title</key><string>A page</string>
  <key>order</key><integer>2</integer>
  <key>created_time</key><integer>1262304000</integer>
  <key>modified_time</key><integer>1262307600</integer>
</dict>
</node>
```

Default attributes (`g_default_attr_defs`):

| Attribute | Type | Meaning | WorriorVex |
| --- | --- | --- | --- |
| `nodeid` | string | UUID of the node | kept as the source id in the import report; used to resolve links |
| `content_type` | string | what the node is, see below | decides folder / note / attachment |
| `title` | string | display name | `Node.Name` |
| `order` | integer | position among siblings | `Node.SortOrder` |
| `created_time`, `modified_time` | integer | seconds since the Unix epoch (`keepnote/timestamp.py`) | `CreatedAt`, `UpdatedAt` |
| `expanded`, `expanded2` | bool | tree view state | ignored |
| `info_sort`, `info_sort_dir` | string, integer | per-folder sort setting | ignored (reported) |
| `icon`, `icon_open` | string | custom icon file names | ignored (reported) |
| `title_bgcolor` | string | title colour in the tree | ignored (reported) |
| `payload_filename` | string | file name of an attached file | attachment file |
| `duplicate_of` | string | id of the node this was copied from | ignored |

Notebooks may define further attributes; unknown keys are kept by KeepNote and will be listed in the import
report as unsupported rather than dropped silently.

### Content types

| `content_type` | Node is | Import as |
| --- | --- | --- |
| `application/x-notebook-dir` | a folder (also the default when the attribute is missing) | folder |
| `text/xhtml+xml` | a page; body in `page.html` | note |
| `application/x-notebook-trash` | the trash folder (title "Trash") | its children become trashed nodes |
| any other MIME type | an attached file: the file named by `payload_filename` lies in the node directory (`attach_file`, `set_payload`) | attachment |

A page can have children, so in KeepNote a note can also act as a folder. The WorriorVex tree allows a
note node to have children for the same reason.

## Note body: `page.html`

UTF-8 XHTML 1.0 Transitional (`BLANK_NOTE`; writer in `keepnote/gui/richtext/richtext_html.py`). The markup
KeepNote itself writes is small:

- inline: `b`, `i`, `u`, `strike`, `tt`, `nobr`, `a href`, `br`
- `span style="…"` with `font-size` (pt), `font-family`, `color`, `background-color`
- `div style="text-align: …"`; the reader also accepts `center` and `font`
- blocks: `p`, `ul`, `ol`, `li` (indentation is written as `li style="list-style-type: none"`), `hr`
- `img src="file" width height`, where `src` is a file name relative to the node directory

There are no headings, tables or code blocks in KeepNote's own output, but a file may have been edited
by hand or pasted from elsewhere, so the importer treats every `page.html` as untrusted HTML: it is
sanitised (scripts, event handlers, unsafe URLs, embedded content removed) before it is stored.

## Links between notes

Internal links are ordinary anchors whose address uses the `nbk` scheme: `nbk://<host>/<nodeid>`, normally
with an empty host, e.g. `nbk:///b810760f-f246-4e42-aebb-50ce51c3d1ed` (`get_node_url`, `parse_node_url`).
The importer maps the node id to the imported note and records a `NoteLink`; a link to a node that is not
in the notebook stays as text and is reported.

## Notebook preferences: `notebook.nbk`

```xml
<notebook>
<version>6</version>
<pref><dict>… <key>quick_pick_icons</key><array>…</array> …</dict></pref>
</notebook>
```

Only the version is used by the importer. Application preferences (window layout, fonts, external apps)
live in the user's home directory, outside the notebook, and are not imported.

## Trash

The trash is a normal child node of the root with `content_type` `application/x-notebook-trash`, stored in
`__TRASH__`. Deleting a node moves its directory there; deleting it again removes it for good. Imported
nodes found there are created with `DeletedAt` set, so they appear in the WorriorVex trash and can be
restored.

## Older format versions

`keepnote/notebook/update.py` upgrades 1 → 2 → 3 → 4 → 5 → 6 step by step.

- Version 5 stored `node.xml` as flat `<attr key="…">value</attr>` elements instead of a dictionary
  (`compat/notebook_update_v5_6.py`); the attributes are the same.
- Versions 1 to 4 differ more (other metadata files and index layout).

The importer will read versions 5 and 6 directly. For anything older it will stop with a clear message
asking the user to open the notebook once in KeepNote, which upgrades it in place. This avoids
re-implementing four legacy readers that cannot be tested without old data.

## Importer rules that follow

1. Never write to the source directory. Do not open `index.sqlite`.
2. Walk directories; a node is any directory with a readable `node.xml`. Unreadable ones are reported.
3. Also scan `__NOTEBOOK__/orphans` and `lost_found` and report what is there instead of ignoring it.
4. Files in a page directory other than `node.xml` and `page.html` are that page's attachments (images
   referenced from the body, and loose files).
5. Count everything first (notes, folders, attachments, images, unsupported attributes), show the
   summary, and import only after confirmation.

## Open questions

To be answered with a real notebook before Phase 11:

- Are embedded image files always in the page's own directory, and how are they named?
- Do real notebooks contain `nbk://` links with a non-empty host?
- Which custom attributes and icons occur in practice?
- How large are typical attachment nodes, and are there attached directories?
