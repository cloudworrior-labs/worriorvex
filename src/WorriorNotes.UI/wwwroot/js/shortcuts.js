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

  window.addEventListener('keydown', onKeyDown);
  return {
    dispose: () => window.removeEventListener('keydown', onKeyDown),
  };
}

const SHORTCUTS = {
  n: 'newNote',
  s: 'save',
};
