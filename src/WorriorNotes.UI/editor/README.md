# Editor bundle

The rich text editor is [TipTap](https://tiptap.dev) (MIT), built on ProseMirror. `src/editor.js` is a thin
wrapper that Blazor drives through JS interop. It is bundled into one ES module,
`../wwwroot/editor/editor.bundle.js`, which is committed so that `dotnet build` needs no Node toolchain.

Rebuild the bundle after changing `src/editor.js` or the dependencies:

```bash
npm ci
npm run build
```

Commit the regenerated bundle together with the source change. See `docs/decisions/editor.md` for why TipTap.
