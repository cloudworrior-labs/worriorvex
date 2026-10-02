#!/usr/bin/env bash
# Packs a published Linux build into a .deb and a portable .tar.gz.
#
#   installer/linux/build-deb.sh <publish-dir> <version> <output-dir>
set -euo pipefail

publish_dir="$1"
version="$2"
output_dir="$3"

root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$output_dir"

# Portable archive: unpack anywhere and run ./WorriorVex
portable="$work/portable/worriorvex-${version}"
mkdir -p "$portable"
cp -R "$publish_dir"/. "$portable/"
chmod +x "$portable/WorriorVex"
tar -C "$work/portable" -czf "$output_dir/WorriorVex-${version}-linux-x64.tar.gz" "worriorvex-${version}"

# Debian package
pkg="$work/deb"
mkdir -p "$pkg/DEBIAN" "$pkg/opt/worriorvex" "$pkg/usr/bin" "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/512x512/apps"
cp -R "$publish_dir"/. "$pkg/opt/worriorvex/"
chmod 755 "$pkg/opt/worriorvex/WorriorVex"
ln -s /opt/worriorvex/WorriorVex "$pkg/usr/bin/worriorvex"
cp "$root/assets/icon/worriorvex-512.png" "$pkg/usr/share/icons/hicolor/512x512/apps/worriorvex.png"

cat > "$pkg/usr/share/applications/worriorvex.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=WorriorVex
GenericName=Notes
Comment=Your personal knowledge workspace
Exec=/opt/worriorvex/WorriorVex
Icon=worriorvex
Terminal=false
Categories=Office;Utility;
StartupWMClass=WorriorVex
DESKTOP

installed_kb="$(du -sk "$pkg/opt" | cut -f1)"
cat > "$pkg/DEBIAN/control" <<CONTROL
Package: worriorvex
Version: ${version}
Section: utils
Priority: optional
Architecture: amd64
Depends: libwebkit2gtk-4.1-0, libnotify4
Installed-Size: ${installed_kb}
Maintainer: Musa Consulting <https://www.cloudworrior.com>
Homepage: https://github.com/cloudworrior-labs/worriorvex
Description: Local-first note-taking app and personal knowledge workspace
 WorriorVex keeps notes in notebooks and folders on your own disk.
 It works offline, needs no account and sends no telemetry.
CONTROL

dpkg-deb --build --root-owner-group "$pkg" "$output_dir/worriorvex_${version}_amd64.deb" >/dev/null
ls -1 "$output_dir"
