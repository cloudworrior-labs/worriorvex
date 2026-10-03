// Extensions of our own: callouts, find & replace, resizable images, the "[[" note-link trigger
// and a cleaner for HTML pasted from Word, Google Docs and web pages.
import { Node, Extension, InputRule, mergeAttributes } from '@tiptap/core';
import Image from '@tiptap/extension-image';
import { Plugin, PluginKey, TextSelection } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';

const CALLOUT_KINDS = ['info', 'tip', 'warning', 'danger'];

/** A box that stands out from the text: `<div data-callout="info">…</div>`. */
export const Callout = Node.create({
  name: 'callout',
  group: 'block',
  content: 'block+',
  defining: true,
  addAttributes() {
    return {
      kind: {
        default: 'info',
        parseHTML: (element) => (CALLOUT_KINDS.includes(element.getAttribute('data-callout')) ? element.getAttribute('data-callout') : 'info'),
        renderHTML: (attributes) => ({ 'data-callout': attributes.kind }),
      },
    };
  },
  parseHTML() {
    return [{ tag: 'div[data-callout]' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { class: 'wn-callout' }), 0];
  },
  addCommands() {
    return {
      toggleCallout:
        (kind = 'info') =>
        ({ commands, editor }) => {
          if (editor.isActive('callout', { kind })) {
            return commands.lift('callout');
          }
          if (editor.isActive('callout')) {
            return commands.updateAttributes('callout', { kind });
          }
          return commands.wrapIn('callout', { kind });
        },
    };
  },
});

const searchKey = new PluginKey('wnSearch');

const escapeRegex = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Positions of every match of the current search in the document. */
const findMatches = (doc, term, caseSensitive) => {
  if (!term) {
    return [];
  }
  const matches = [];
  const pattern = new RegExp(escapeRegex(term), caseSensitive ? 'g' : 'gi');
  doc.descendants((node, position) => {
    if (!node.isText) {
      return;
    }
    for (const match of node.text.matchAll(pattern)) {
      matches.push({ from: position + match.index, to: position + match.index + match[0].length });
    }
  });
  return matches;
};

/**
 * Find & replace. The matches are decorations, so the document is not changed by searching;
 * the state (term, options, current match) lives in the plugin and is read back through
 * `editor.storage.search`.
 */
export const Search = Extension.create({
  name: 'search',
  addStorage() {
    return { term: '', caseSensitive: false, matches: [], current: -1 };
  },
  addCommands() {
    const refresh = (editor, tr, term, caseSensitive, current) => {
      const storage = editor.storage.search;
      storage.term = term;
      storage.caseSensitive = caseSensitive;
      storage.matches = findMatches(tr.doc, term, caseSensitive);
      storage.current = storage.matches.length === 0 ? -1 : Math.min(Math.max(current, 0), storage.matches.length - 1);
      tr.setMeta(searchKey, true);
    };
    const reveal = (editor, tr) => {
      const match = editor.storage.search.matches[editor.storage.search.current];
      if (match) {
        tr.setSelection(TextSelection.create(tr.doc, match.from, match.to)).scrollIntoView();
      }
    };
    return {
      setSearch:
        (term, caseSensitive = false) =>
        ({ editor, tr, dispatch }) => {
          // A new term starts from the match at or after the cursor.
          const storage = editor.storage.search;
          const from = tr.selection.from;
          refresh(editor, tr, term, caseSensitive, 0);
          const next = storage.matches.findIndex((m) => m.from >= from);
          storage.current = storage.matches.length === 0 ? -1 : next < 0 ? 0 : next;
          if (dispatch) {
            reveal(editor, tr);
          }
          return true;
        },
      findNext:
        (backwards = false) =>
        ({ editor, tr, dispatch }) => {
          const storage = editor.storage.search;
          if (storage.matches.length === 0) {
            return false;
          }
          storage.current = (storage.current + (backwards ? -1 : 1) + storage.matches.length) % storage.matches.length;
          tr.setMeta(searchKey, true);
          if (dispatch) {
            reveal(editor, tr);
          }
          return true;
        },
      replaceCurrent:
        (replacement) =>
        ({ editor, tr, dispatch }) => {
          const storage = editor.storage.search;
          const match = storage.matches[storage.current];
          if (!match) {
            return false;
          }
          tr.insertText(replacement, match.from, match.to);
          refresh(editor, tr, storage.term, storage.caseSensitive, storage.current);
          // The match that replaced the old one is skipped unless the replacement still matches.
          const index = storage.matches.findIndex((m) => m.from >= match.from + replacement.length);
          storage.current = storage.matches.length === 0 ? -1 : index < 0 ? 0 : index;
          if (dispatch) {
            reveal(editor, tr);
          }
          return true;
        },
      replaceAll:
        (replacement) =>
        ({ editor, tr }) => {
          const storage = editor.storage.search;
          // From the end, so earlier positions stay valid.
          for (const match of [...storage.matches].reverse()) {
            tr.insertText(replacement, match.from, match.to);
          }
          refresh(editor, tr, storage.term, storage.caseSensitive, 0);
          return true;
        },
      clearSearch:
        () =>
        ({ editor, tr }) => {
          refresh(editor, tr, '', false, -1);
          return true;
        },
    };
  },
  addProseMirrorPlugins() {
    const editor = this.editor;
    return [
      new Plugin({
        key: searchKey,
        state: {
          init: () => DecorationSet.empty,
          apply(tr, old) {
            const storage = editor.storage.search;
            if (!tr.docChanged && !tr.getMeta(searchKey)) {
              return old;
            }
            if (tr.docChanged && !tr.getMeta(searchKey)) {
              storage.matches = findMatches(tr.doc, storage.term, storage.caseSensitive);
              storage.current = Math.min(storage.current, storage.matches.length - 1);
            }
            return DecorationSet.create(
              tr.doc,
              storage.matches.map((m, index) =>
                Decoration.inline(m.from, m.to, { class: index === storage.current ? 'wn-match wn-match-current' : 'wn-match' }),
              ),
            );
          },
        },
        props: {
          decorations(state) {
            return this.getState(state);
          },
        },
      }),
    ];
  },
});

/** An image that can be resized by dragging its bottom-right corner; the width is kept as an attribute. */
export const ResizableImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      width: {
        default: null,
        parseHTML: (element) => {
          const width = parseInt(element.getAttribute('width') || '', 10);
          return Number.isFinite(width) && width > 0 ? width : null;
        },
        renderHTML: (attributes) => (attributes.width ? { width: attributes.width } : {}),
      },
    };
  },
  addNodeView() {
    return ({ node, getPos, editor }) => {
      const wrapper = document.createElement('span');
      wrapper.className = 'wn-image';
      const img = document.createElement('img');
      img.src = node.attrs.src;
      img.alt = node.attrs.alt || '';
      if (node.attrs.width) {
        img.width = node.attrs.width;
      }
      const handle = document.createElement('span');
      handle.className = 'wn-image-handle';
      handle.setAttribute('aria-hidden', 'true');
      wrapper.append(img, handle);

      handle.addEventListener('pointerdown', (event) => {
        event.preventDefault();
        event.stopPropagation();
        const startX = event.clientX;
        const startWidth = img.getBoundingClientRect().width;
        const maxWidth = editor.view.dom.getBoundingClientRect().width;
        const move = (e) => {
          img.width = Math.round(Math.min(maxWidth, Math.max(40, startWidth + e.clientX - startX)));
        };
        const up = () => {
          document.removeEventListener('pointermove', move);
          document.removeEventListener('pointerup', up);
          const position = getPos();
          if (typeof position === 'number') {
            editor.view.dispatch(editor.state.tr.setNodeMarkup(position, undefined, { ...node.attrs, width: img.width }));
          }
        };
        document.addEventListener('pointermove', move);
        document.addEventListener('pointerup', up);
      });

      return {
        dom: wrapper,
        selectNode: () => wrapper.classList.add('is-selected'),
        deselectNode: () => wrapper.classList.remove('is-selected'),
        update: (updated) => {
          if (updated.type !== node.type) {
            return false;
          }
          node = updated;
          img.src = updated.attrs.src;
          img.alt = updated.attrs.alt || '';
          if (updated.attrs.width) {
            img.width = updated.attrs.width;
          } else {
            img.removeAttribute('width');
          }
          return true;
        },
        ignoreMutation: () => true,
      };
    };
  },
});

/** Typing "[[" asks the app for a note to link to; the brackets are removed. */
export const NoteLinkTrigger = Extension.create({
  name: 'noteLinkTrigger',
  addOptions() {
    return { onTrigger: () => {} };
  },
  addInputRules() {
    return [
      new InputRule({
        find: /\[\[$/,
        handler: ({ range, commands }) => {
          commands.deleteRange(range);
          this.options.onTrigger();
        },
      }),
    ];
  },
});

/**
 * Pasted HTML from Word, Google Docs and web pages carries styling the schema would mostly drop
 * anyway; this takes out what would otherwise leave odd remains (comments, Office namespaces,
 * style and script blocks, bold wrappers Google Docs puts around everything).
 */
export function cleanPastedHtml(html) {
  if (!html) {
    return html;
  }
  let cleaned = html
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<(style|script|xml|meta|link)[\s\S]*?(<\/\1>|>)/gi, '')
    .replace(/<\/?o:p>/gi, '')
    .replace(/<\/?(w|o|v|m):[^>]*>/gi, '');
  try {
    const doc = new DOMParser().parseFromString(cleaned, 'text/html');
    // Google Docs wraps the whole paste in <b style="font-weight:normal">.
    doc.querySelectorAll('b[style*="font-weight:normal"], b[style*="font-weight: normal"]').forEach((b) => b.replaceWith(...b.childNodes));
    // Word's list paragraphs come as <p class="MsoListParagraph">; without the list this is a plain paragraph.
    doc.querySelectorAll('[class]').forEach((element) => {
      if (/^Mso/i.test(element.className)) {
        element.removeAttribute('class');
      }
    });
    doc.querySelectorAll('span[style]').forEach((span) => {
      const style = span.getAttribute('style') || '';
      if (/font-weight:\s*(bold|[6-9]00)/i.test(style)) {
        const strong = doc.createElement('strong');
        strong.append(...span.childNodes);
        span.replaceWith(strong);
      } else if (/font-style:\s*italic/i.test(style)) {
        const em = doc.createElement('em');
        em.append(...span.childNodes);
        span.replaceWith(em);
      }
    });
    cleaned = doc.body.innerHTML;
  } catch {
    // Keep what the regular expressions produced.
  }
  return cleaned;
}
