#!/usr/bin/env bash
# Wraps a finished Wandur.app in a compressed disk image with an Applications shortcut, so
# users drag the app across. Used by the release workflow and scripts/release-macos-signed.sh.
#
#   scripts/macos/make-dmg.sh <path/to/Wandur.app> <out.dmg>
#
# A disk image keeps the extended attributes that hold the signatures of the managed .dll
# files in Contents/MacOS; a zip only keeps them when macOS's own tools unpack it.
set -euo pipefail
[[ $# -eq 2 ]] || { echo "usage: make-dmg.sh <Wandur.app> <out.dmg>" >&2; exit 2; }
app="$1"
out="$2"
[[ -d "$app/Contents/MacOS" ]] || { echo "not an app bundle: $app" >&2; exit 1; }
mkdir -p "$(dirname "$out")"

staging="$(mktemp -d "${TMPDIR:-/tmp}/wandur-dmg.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
ditto "$app" "$staging/Wandur.app"
ln -s /Applications "$staging/Applications"
hdiutil create -volname Wandur -srcfolder "$staging" -ov -format UDZO "$out" >/dev/null
echo "Built: $out"
