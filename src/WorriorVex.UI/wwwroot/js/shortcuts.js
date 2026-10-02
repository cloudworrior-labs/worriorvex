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

  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('dragover', ignoreDrop);
  window.addEventListener('drop', ignoreDrop);
  return {
    dispose: () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('dragover', ignoreDrop);
      window.removeEventListener('drop', ignoreDrop);
    },
  };
}

const SHORTCUTS = {
  n: 'newNote',
  s: 'save',
};
