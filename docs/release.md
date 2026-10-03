# Releasing

## Making a release

1. Make sure `main` is green.
2. Tag and push:

   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```

The `Release` workflow then runs the tests, builds on Windows, macOS and Linux runners, and creates the
GitHub release with all files and checksums. The Homebrew tap picks the release up by itself.
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

The cask lives in its own tap, https://github.com/cloudworrior-labs/homebrew-tap. A workflow there
rewrites `Casks/worriorvex.rb` from the latest release every six hours and on demand (Actions → Update
cask → Run workflow), computing the checksums from the published disk images. Nothing in this repository
needs to change for a release to reach Homebrew.

```bash
brew tap cloudworrior-labs/tap
brew trust cloudworrior-labs/tap
brew install --cask worriorvex
```

## Update check

The app can ask `api.github.com/repos/cloudworrior-labs/worriorvex/releases/latest` for the newest tag
(Settings → Updates, off by default; About → Check now). It compares the tag with its own version and
offers the downloads page. It sends only the request; there is no telemetry.

## Icon

`assets/icon/worriorvex.svg` is the reference drawing. `python3 assets/icon/build-icons.py` regenerates
the PNG, ICO and ICNS files and the copies used by the app.

## Testing an installer locally (macOS)

```bash
dotnet publish src/WorriorVex.Desktop -c Release -r osx-arm64 --self-contained true -o /tmp/wv-publish
installer/macos/build-dmg.sh /tmp/wv-publish 0.0.0 arm64 /tmp/wv-dist
```
