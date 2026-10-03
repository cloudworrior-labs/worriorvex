WorriorVex {{VERSION}} — your personal knowledge workspace. Local-first: no account, no cloud, no telemetry.

What changed: see [CHANGELOG.md](https://github.com/cloudworrior-labs/worriorvex/blob/main/CHANGELOG.md).

## Install

| System | Download | Then |
| --- | --- | --- |
| **Windows** 10/11 (64-bit) | `WorriorVex-Setup-{{VERSION}}-x64.exe` | Run it. Windows SmartScreen may warn because the installer is not code-signed yet: choose **More info → Run anyway**. |
| **macOS** Apple silicon | `WorriorVex-{{VERSION}}-macos-arm64.dmg` | Open it and drag WorriorVex to Applications. |
| **macOS** Intel | `WorriorVex-{{VERSION}}-macos-x64.dmg` | Same. |
| **Linux** Debian/Ubuntu | `worriorvex_{{VERSION}}_amd64.deb` | `sudo apt install ./worriorvex_{{VERSION}}_amd64.deb` |
| **Linux** other | `WorriorVex-{{VERSION}}-linux-x64.tar.gz` | Unpack and run `./WorriorVex` (needs WebKitGTK 4.1). |

**macOS:** the app is not yet signed with an Apple Developer ID, so the first launch is blocked. Either
right-click the app and choose **Open**, or run `xattr -dr com.apple.quarantine /Applications/WorriorVex.app`.

**Homebrew (macOS):**

```bash
brew tap cloudworrior-labs/worriorvex https://github.com/cloudworrior-labs/worriorvex
brew trust cloudworrior-labs/worriorvex   # recent Homebrew asks you to trust a third-party tap once
brew install --cask worriorvex
```

**Windows** needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11 and most Windows 10 installations.

Checksums are in `SHA256SUMS.txt`. Your notes are stored in your user profile and are not touched by installing, upgrading or uninstalling.
