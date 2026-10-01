// WorriorNotes rich text editor: a thin wrapper around TipTap that Blazor drives through JS interop.
// Content loaded here is parsed against the ProseMirror schema, so markup outside the schema
// (scripts, event handlers, unknown elements) is dropped rather than rendered.
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TaskList, TaskItem } from '@tiptap/extension-list';

const CHANGE_DELAY_MS = 250;

/**
 * @param {HTMLElement} element  host element for the editor
 * @param {object} dotNetRef     .NET object with OnEditorChanged(html) and OnEditorSelectionChanged(activeMarks)
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

  const editor = new Editor({
    element,
    extensions: [
      StarterKit.configure({
        link: { openOnClick: false, autolink: true, defaultProtocol: 'https' },
      }),
      TaskList,
      TaskItem.configure({ nested: true }),
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
];
