# Releasing

## Making a release

1. Make sure `main` is green.
2. Tag and push:

   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```

The `Release` workflow then runs the tests, builds on Windows, macOS and Linux runners, creates the
GitHub release with all files and checksums, and updates the Homebrew cask in `Casks/worriorvex.rb`.
The version shown in the app comes from the tag.

## What is built

| File | Built by | How |
| --- | --- | --- |
| `WorriorVex-Setup-<v>-x64.exe` | `installer/windows/worriorvex.iss` | Inno Setup; per-user install, Start menu entry, uninstaller |
| `WorriorVex-<v>-windows-x64-portable.zip` | workflow | the published folder, zipped |
| `WorriorVex-<v>-macos-arm64.dmg`, `-x64.dmg` | `installer/macos/build-dmg.sh` | `.app` bundle, ad-hoc signed, in a disk image |
| `worriorvex_<v>_amd64.deb` | `installer/linux/build-deb.sh` | installs to `/opt/worriorvex`, adds a menu entry |
| `WorriorVex-<v>-linux-x64.tar.gz` | `installer/linux/build-deb.sh` | portable folder |

All builds are self-contained: the user does not need .NET installed.

Linux format: `.deb` first, because the web view (WebKitGTK) is a system library that a package
dependency can pull in; AppImage and Flatpak would not remove that dependency. A portable archive covers
other distributions.

## Signing

Nothing is signed with a paid certificate yet, so Windows SmartScreen and macOS Gatekeeper warn on first
launch. To remove the warnings:

- **Windows:** an Authenticode code-signing certificate; sign `WorriorVex.exe` and the installer.
- **macOS:** an Apple Developer ID; sign with the hardened runtime and notarise the `.dmg`.

Both need secrets in the repository settings and a signing step in the workflow.

## Homebrew

The cask lives in this repository (`Casks/worriorvex.rb`, generated from
`installer/macos/worriorvex.rb.template`), so the repository itself is the tap:

```bash
brew tap cloudworrior-labs/worriorvex https://github.com/cloudworrior-labs/worriorvex
brew install --cask worriorvex
```

## Icon

`assets/icon/worriorvex.svg` is the reference drawing. `python3 assets/icon/build-icons.py` regenerates
the PNG, ICO and ICNS files and the copies used by the app.

## Testing an installer locally (macOS)

```bash
dotnet publish src/WorriorVex.Desktop -c Release -r osx-arm64 --self-contained true -o /tmp/wv-publish
installer/macos/build-dmg.sh /tmp/wv-publish 0.0.0 arm64 /tmp/wv-dist
```
