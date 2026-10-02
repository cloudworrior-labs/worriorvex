#!/usr/bin/env bash
# Wraps a published macOS build in WorriorVex.app and packs it into a .dmg.
#
#   installer/macos/build-dmg.sh <publish-dir> <version> <arch: arm64|x64> <output-dir>
#
# The app is signed ad hoc (required to run on Apple silicon). It is not signed with an Apple
# Developer ID and not notarised, so Gatekeeper asks the user to confirm the first launch.
set -euo pipefail

publish_dir="$1"
version="$2"
arch="$3"
output_dir="$4"

root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

app="$work/stage/WorriorVex.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$output_dir"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
cp "$root/assets/icon/worriorvex.icns" "$app/Contents/Resources/worriorvex.icns"
chmod +x "$app/Contents/MacOS/WorriorVex"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>WorriorVex</string>
    <key>CFBundleDisplayName</key><string>WorriorVex</string>
    <key>CFBundleIdentifier</key><string>com.musaconsulting.worriorvex</string>
    <key>CFBundleExecutable</key><string>WorriorVex</string>
    <key>CFBundleIconFile</key><string>worriorvex</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>${version}</string>
    <key>CFBundleVersion</key><string>${version}</string>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>© 2026 Musa Consulting</string>
</dict>
</plist>
PLIST

# Web assets are data, not code: they live in Resources, and the app finds them through a link.
# (A folder such as "WorriorVex.UI" inside MacOS would be mistaken for a nested bundle when signing.)
mv "$app/Contents/MacOS/wwwroot" "$app/Contents/Resources/wwwroot"
ln -s ../Resources/wwwroot "$app/Contents/MacOS/wwwroot"

codesign --force --deep --sign - "$app" 2>/dev/null
codesign --verify --deep --strict "$app"

ln -s /Applications "$work/stage/Applications"
dmg="$output_dir/WorriorVex-${version}-macos-${arch}.dmg"
rm -f "$dmg"
hdiutil create -volname "WorriorVex ${version}" -srcfolder "$work/stage" -ov -format UDZO "$dmg" >/dev/null
echo "$dmg"
