#!/bin/sh
# Adds Wandur to your desktop's application menu, for this user only. Run it from the
# folder you unpacked, after moving that folder where you want to keep it:
#   ./install-desktop-entry.sh
# Undo with: rm ~/.local/share/applications/net.wandur.client.desktop \
#   ~/.local/share/icons/hicolor/256x256/apps/net.wandur.client.png
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
chmod +x "$here/Wandur"
mkdir -p "$data/applications" "$data/icons/hicolor/256x256/apps"
cp "$here/net.wandur.client.png" "$data/icons/hicolor/256x256/apps/net.wandur.client.png"
sed "s|^Exec=.*|Exec=\"$here/Wandur\"|" "$here/net.wandur.client.desktop" \
  > "$data/applications/net.wandur.client.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true
echo "Added Wandur to the application menu ($data/applications/net.wandur.client.desktop)."
