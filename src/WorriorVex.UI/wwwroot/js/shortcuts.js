// Application-wide keyboard shortcuts, handled in one place.
// See docs/keyboard-shortcuts.md for the list.
export function register(dotNetRef) {
  const onKeyDown = (event) => {
    const modifier = event.metaKey || event.ctrlKey;
    if (!modifier || event.altKey || event.shiftKey) {
      return;
    }

    const action = SHORTCUTS[event.key.toLowerCase()];
    if (action) {
      event.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', action);
    }
  };

  // A file dropped outside the editor would otherwise make the window navigate to that file.
  const ignoreDrop = (event) => event.preventDefault();

  // Dragging notes and folders is handled by the app; some engines only start a drag that carries data.
  const onDragStart = (event) => {
    if (event.target.closest?.('[draggable="true"]') && event.dataTransfer) {
      event.dataTransfer.setData('text/plain', 'worriorvex-item');
      event.dataTransfer.effectAllowed = 'move';
    }
  };

  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('dragover', ignoreDrop);
  window.addEventListener('drop', ignoreDrop);
  window.addEventListener('dragstart', onDragStart);
  return {
    dispose: () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('dragstart', onDragStart);
      window.removeEventListener('dragover', ignoreDrop);
      window.removeEventListener('drop', ignoreDrop);
    },
  };
}

const SHORTCUTS = {
  n: 'newNote',
  s: 'save',
  k: 'search',
};
