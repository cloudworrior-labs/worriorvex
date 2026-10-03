// WorriorVex rich text editor: a thin wrapper around TipTap that Blazor drives through JS interop.
// Content loaded here is parsed against the ProseMirror schema, so markup outside the schema
// (scripts, event handlers, unknown elements) is dropped rather than rendered.
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TaskList, TaskItem } from '@tiptap/extension-list';
import { TableKit } from '@tiptap/extension-table';
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight';
import Highlight from '@tiptap/extension-highlight';
import { common, createLowlight } from 'lowlight';
import { Callout, Search, ResizableImage, NoteLinkTrigger, cleanPastedHtml } from './extensions.js';

const lowlight = createLowlight(common);

const CHANGE_DELAY_MS = 250;

// An image in a note is always one of the note's own attachments. Anything else (a web address,
// embedded data) is not accepted into the document, so opening a note never fetches from the network.
const ATTACHMENT_IMAGE = /^attachments\/[0-9a-f]{32}\.(png|jpg|jpeg|gif|webp)$/;
const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];
const WEB_LINK = /^https?:\/\//i;
// A link to another note: "note:" and the note's id. The app opens these itself.
const NOTE_LINK = /^note:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const NoteImage = ResizableImage.extend({
  parseHTML() {
    return [{ tag: 'img[src]', getAttrs: (element) => (ATTACHMENT_IMAGE.test(element.getAttribute('src') || '') ? null : false) }];
  },
}).configure({ inline: false, allowBase64: false });

const imageFiles = (transfer) => Array.from(transfer?.files || []).filter((file) => IMAGE_TYPES.includes(file.type));

/**
 * @param {HTMLElement} element  host element for the editor
 * @param {object} dotNetRef     .NET object with OnEditorChanged(html), OnEditorSelectionChanged(activeMarks),
 *                               OnImageFile(stream, name, type) returning the image address, OnOpenLink(address)
 *                               and OnNoteLinkTrigger() when "[[" is typed
 * @param {string} html          initial content
 */
export function create(element, dotNetRef, html) {
  let timer = null;
  let suppress = false;

  const pushChange = () => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
      dotNetRef.invokeMethodAsync('OnEditorChanged', editor.getHTML());
    }
  };

  const pushActive = () => {
    const active = ACTIVE_CHECKS.filter(([, check]) => check(editor)).map(([name]) => name);
    dotNetRef.invokeMethodAsync('OnEditorSelectionChanged', active);
  };

  // Hands an image file to .NET, which stores it as an attachment, then places it in the note.
  const addImage = async (file, position) => {
    const stream = DotNet.createJSStreamReference(file);
    const source = await dotNetRef.invokeMethodAsync('OnImageFile', stream, file.name || 'image', file.type);
    if (source && !editor.isDestroyed) {
      // A dropped image goes where it was dropped, a pasted one where the cursor is.
      const chain = editor.chain().focus();
      if (position !== undefined) {
        chain.setTextSelection(Math.min(position, editor.state.doc.content.size));
      }
      chain.setImage({ src: source, alt: file.name || '' }).run();
    }
  };

  const editor = new Editor({
    element,
    extensions: [
      StarterKit.configure({
        codeBlock: false,
        link: {
          openOnClick: false,
          autolink: true,
          defaultProtocol: 'https',
          protocols: ['http', 'https', 'note'],
          isAllowedUri: (url) => WEB_LINK.test(url) || NOTE_LINK.test(url),
          HTMLAttributes: { target: null, rel: null },
        },
      }),
      TaskList,
      TaskItem.configure({ nested: true }),
      NoteImage,
      TableKit.configure({ table: { resizable: true, lastColumnResizable: false } }),
      CodeBlockLowlight.configure({ lowlight, defaultLanguage: null }),
      Highlight,
      Callout,
      Search,
      NoteLinkTrigger.configure({ onTrigger: () => dotNetRef.invokeMethodAsync('OnNoteLinkTrigger') }),
    ],
    content: html || '',
    editorProps: {
      attributes: {
        class: 'wn-prose',
        role: 'textbox',
        'aria-multiline': 'true',
        'aria-label': 'Note content',
        spellcheck: 'true',
      },
      transformPastedHTML: cleanPastedHtml,
      handlePaste: (view, event) => {
        const files = imageFiles(event.clipboardData);
        if (files.length === 0) {
          return false;
        }
        event.preventDefault();
        files.forEach((file) => addImage(file));
        return true;
      },
      handleDrop: (view, event, slice, moved) => {
        const all = moved ? [] : Array.from(event.dataTransfer?.files || []);
        if (all.length === 0) {
          return false;
        }
        event.preventDefault();
        const position = view.posAtCoords({ left: event.clientX, top: event.clientY })?.pos;
        all.forEach((file) => {
          if (IMAGE_TYPES.includes(file.type)) {
            addImage(file, position);
          } else {
            // Any other file becomes an attachment of the note.
            dotNetRef.invokeMethodAsync('OnFileDrop', DotNet.createJSStreamReference(file), file.name || 'file', file.type || '');
          }
        });
        return true;
      },
      handleClick: (view, position, event) => {
        // A plain click places the cursor so a link can be edited; with Ctrl or Cmd it is followed.
        const link = event.metaKey || event.ctrlKey ? event.target.closest?.('a[href]') : null;
        if (!link) {
          return false;
        }
        dotNetRef.invokeMethodAsync('OnOpenLink', link.getAttribute('href'));
        return true;
      },
    },
    onUpdate: () => {
      if (suppress) {
        return;
      }
      if (timer !== null) {
        clearTimeout(timer);
      }
      timer = setTimeout(pushChange, CHANGE_DELAY_MS);
      pushActive();
    },
    onSelectionUpdate: pushActive,
    onBlur: pushChange,
  });

  const searchState = () => ({ count: editor.storage.search.matches.length, current: editor.storage.search.current + 1 });

  return {
    setContent(newHtml) {
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
      suppress = true;
      try {
        editor.commands.setContent(newHtml || '', { emitUpdate: false });
        editor.commands.setTextSelection(0);
      } finally {
        suppress = false;
      }
      pushActive();
    },
    getHtml: () => editor.getHTML(),
    flush: pushChange,
    // Straight to the view: the focus command waits for an animation frame, which a hidden window never gets.
    focus: () => editor.view.focus(),
    run(command) {
      const action = COMMANDS[command];
      if (action) {
        action(editor.chain().focus()).run();
      }
    },
    /** The address of the link at the cursor, or an empty string. */
    getLinkAddress: () => editor.getAttributes('link').href || '',
    /** Makes the selection (or the link at the cursor) point to an address; an empty address removes the link. */
    setLink(address) {
      const chain = editor.chain().focus().extendMarkRange('link');
      if (!address) {
        chain.unsetLink().run();
      } else if (editor.state.selection.empty && !editor.isActive('link')) {
        // Nothing selected: the address itself becomes the linked text.
        chain.insertContent({ type: 'text', text: address, marks: [{ type: 'link', attrs: { href: address } }] }).run();
      } else {
        chain.setLink({ href: address }).run();
      }
    },
    /** Links the selection to another note, or inserts the note's title as the link when nothing is selected. */
    setNoteLink(address, title) {
      const chain = editor.chain().focus().extendMarkRange('link');
      if (editor.state.selection.empty && !editor.isActive('link')) {
        chain.insertContent({ type: 'text', text: title, marks: [{ type: 'link', attrs: { href: address } }] }).run();
      } else {
        chain.setLink({ href: address }).run();
      }
    },
    setSpellCheck(enabled) {
      editor.view.dom.setAttribute('spellcheck', enabled ? 'true' : 'false');
    },
    insertImage(source, alt) {
      editor.chain().focus().setImage({ src: source, alt: alt || '' }).run();
    },
    /** The language of the code block at the cursor, or an empty string. */
    getCodeLanguage: () => editor.getAttributes('codeBlock').language || '',
    setCodeLanguage(language) {
      editor.chain().focus().updateAttributes('codeBlock', { language: language || null }).run();
    },
    /** Find & replace: returns { count, current } after the search runs. */
    search(term, caseSensitive) {
      editor.commands.setSearch(term || '', !!caseSensitive);
      return searchState();
    },
    findNext(backwards) {
      editor.commands.findNext(!!backwards);
      return searchState();
    },
    replace(replacement) {
      editor.chain().focus().replaceCurrent(replacement || '').run();
      return searchState();
    },
    replaceAll(replacement) {
      editor.chain().focus().replaceAll(replacement || '').run();
      return searchState();
    },
    clearSearch() {
      editor.commands.clearSearch();
    },
    destroy() {
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
      editor.destroy();
    },
  };
}

const COMMANDS = {
  bold: (c) => c.toggleBold(),
  italic: (c) => c.toggleItalic(),
  underline: (c) => c.toggleUnderline(),
  strike: (c) => c.toggleStrike(),
  code: (c) => c.toggleCode(),
  paragraph: (c) => c.setParagraph(),
  heading1: (c) => c.toggleHeading({ level: 1 }),
  heading2: (c) => c.toggleHeading({ level: 2 }),
  heading3: (c) => c.toggleHeading({ level: 3 }),
  bulletList: (c) => c.toggleBulletList(),
  orderedList: (c) => c.toggleOrderedList(),
  taskList: (c) => c.toggleTaskList(),
  blockquote: (c) => c.toggleBlockquote(),
  codeBlock: (c) => c.toggleCodeBlock(),
  horizontalRule: (c) => c.setHorizontalRule(),
  insertTable: (c) => c.insertTable({ rows: 3, cols: 3, withHeaderRow: true }),
  addRow: (c) => c.addRowAfter(),
  addColumn: (c) => c.addColumnAfter(),
  deleteRow: (c) => c.deleteRow(),
  deleteColumn: (c) => c.deleteColumn(),
  toggleHeaderRow: (c) => c.toggleHeaderRow(),
  deleteTable: (c) => c.deleteTable(),
  mergeCells: (c) => c.mergeCells(),
  splitCell: (c) => c.splitCell(),
  highlight: (c) => c.toggleHighlight(),
  calloutInfo: (c) => c.toggleCallout('info'),
  calloutTip: (c) => c.toggleCallout('tip'),
  calloutWarning: (c) => c.toggleCallout('warning'),
  calloutDanger: (c) => c.toggleCallout('danger'),
  unlink: (c) => c.extendMarkRange('link').unsetLink(),
  undo: (c) => c.undo(),
  redo: (c) => c.redo(),
};

const ACTIVE_CHECKS = [
  ['bold', (e) => e.isActive('bold')],
  ['italic', (e) => e.isActive('italic')],
  ['underline', (e) => e.isActive('underline')],
  ['strike', (e) => e.isActive('strike')],
  ['code', (e) => e.isActive('code')],
  ['heading1', (e) => e.isActive('heading', { level: 1 })],
  ['heading2', (e) => e.isActive('heading', { level: 2 })],
  ['heading3', (e) => e.isActive('heading', { level: 3 })],
  ['bulletList', (e) => e.isActive('bulletList')],
  ['orderedList', (e) => e.isActive('orderedList')],
  ['taskList', (e) => e.isActive('taskList')],
  ['blockquote', (e) => e.isActive('blockquote')],
  ['codeBlock', (e) => e.isActive('codeBlock')],
  ['link', (e) => e.isActive('link')],
  ['table', (e) => e.isActive('table')],
  ['highlight', (e) => e.isActive('highlight')],
  ['callout', (e) => e.isActive('callout')],
];
