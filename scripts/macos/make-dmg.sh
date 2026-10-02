#!/usr/bin/env bash
# Wraps a finished Wandur.app in a compressed disk image laid out as an installer window: the app on the left,
# an Applications shortcut on the right, and a background that says to drag one onto the other. Used by the
# release workflow and scripts/macos/sign-and-notarize.sh.
#
#   scripts/macos/make-dmg.sh <path/to/Wandur.app> <out.dmg>
#
# The layout is scripts/macos/dmg-settings.py, built with dmgbuild (pinned below), which writes the window's
# .DS_Store directly instead of scripting Finder. dmgbuild comes from the Python on PATH when it has it (the
# workflow installs it), and otherwise from a throwaway virtual environment.
#
# A disk image keeps the extended attributes that hold the signatures of the managed .dll files in
# Contents/MacOS; a zip only keeps them when macOS's own tools unpack it.
set -euo pipefail
DMGBUILD_VERSION=1.6.5  # the newest that still runs on the system Python 3.9 of a stock Mac

[[ $# -eq 2 ]] || { echo "usage: make-dmg.sh <Wandur.app> <out.dmg>" >&2; exit 2; }
app="${1%/}"
out="$2"
here="$(cd "$(dirname "$0")" && pwd -P)"
[[ -d "$app/Contents/MacOS" ]] || { echo "not an app bundle: $app" >&2; exit 1; }
mkdir -p "$(dirname "$out")"
rm -f "$out"

work="$(mktemp -d "${TMPDIR:-/tmp}/wandur-dmg.XXXXXX")"
trap 'rm -rf "$work"' EXIT
python="${PYTHON:-python3}"
if ! "$python" -c "import dmgbuild" 2>/dev/null; then
  "$python" -m venv "$work/venv"
  "$work/venv/bin/pip" install --quiet --disable-pip-version-check "dmgbuild==$DMGBUILD_VERSION"
  python="$work/venv/bin/python"
fi

args=(-s "$here/dmg-settings.py" -D "app=$app" -D "background=$here/dmg-background.tiff")
[[ -f "$app/Contents/Resources/Wandur.icns" ]] && args+=(-D "volume_icon=$app/Contents/Resources/Wandur.icns")
# hdiutil, under dmgbuild, fails now and then with "Resource busy" while Spotlight or another process still
# holds the fresh volume; one retry after a pause gets past it.
build() { "$python" -m dmgbuild "${args[@]}" Wandur "$out" >/dev/null; }
if ! build; then
  echo "dmgbuild failed; retrying once in 5 seconds" >&2
  rm -f "$out"
  sleep 5
  build
fi
echo "Built: $out"
