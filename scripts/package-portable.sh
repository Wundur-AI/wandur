#!/usr/bin/env bash
# Self-contained Windows and Linux downloads. Runs on any OS with the .NET SDK and Python 3;
# the release workflow runs it on the matching runner, and it cross-publishes from macOS.
#
#   scripts/package-portable.sh --rid win-x64|linux-x64 --version X [--output DIR]
#
#   win-x64    DIR/Wandur-X-windows-x64.zip     (folder Wandur-X-windows-x64 with Wandur.exe)
#   linux-x64  DIR/Wandur-X-linux-x64.tar.gz    (folder Wandur-X-linux-x64 with an executable
#              Wandur, install-desktop-entry.sh, the .desktop template it fills in and the icon)
#
# The output is a folder, not a single file: the app starts itself again as its script worker,
# and ONNX Runtime, SQLite, Skia and HarfBuzz ship native libraries that a single-file bundle
# would have to extract to disk on every start. DIR defaults to artifacts/release.
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
export COPYFILE_DISABLE=1   # no AppleDouble files in a tar made on macOS

usage() { sed -n '4,6p' "$0" >&2; exit 2; }
rid=""; version=""; output=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) rid="${2:-}"; shift ;;
    --version) version="${2:-}"; shift ;;
    --output) output="${2:-}"; shift ;;
    -h|--help) usage ;;
    *) echo "unknown option: $1" >&2; usage ;;
  esac
  shift
done
case "$rid" in
  win-x64) platform=windows-x64 ;;
  linux-x64) platform=linux-x64 ;;
  *) echo "--rid must be win-x64 or linux-x64" >&2; usage ;;
esac
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
  || { echo "--version must look like 1.2.3 or 1.2.3-beta.1 (no leading v)" >&2; exit 2; }

# python3 on Windows runners can be the Microsoft Store stub, which exits with an error.
python=python3
"$python" -c '' 2>/dev/null || python=python

project_root="$(cd "$(dirname "$0")/.." && pwd)"
output="${output:-$project_root/artifacts/release}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
name="Wandur-$version-$platform"
# Stage in the system temp directory: on exFAT (the owner's external drive) every file gains an
# AppleDouble ._ companion and loses its permission bits, and both would end up in the archive.
work="$(mktemp -d "${TMPDIR:-/tmp}/wandur-portable.XXXXXX")"
stage="$work/$name"
locks="$work/locks"

cleanup() {
  local status=$?
  if [[ -d "$locks" ]]; then
    "$python" "$project_root/scripts/lock-guard.py" check "$project_root" "$locks" || status=1
  fi
  rm -rf "$work"
  exit $status
}
trap cleanup EXIT

# Locked restore first, so a lock file that does not match the projects fails the build. The
# runtime restore cannot be locked (NU1004); lock-guard.py checks and undoes what it writes.
dotnet restore "$project_root/Wandur.sln" --locked-mode --disable-parallel
"$python" "$project_root/scripts/lock-guard.py" snapshot "$project_root" "$locks"
dotnet restore "$project_root/src/Wandur.Desktop/Wandur.Desktop.csproj" --disable-parallel \
  -r "$rid" -p:SelfContained=true
dotnet publish "$project_root/src/Wandur.Desktop/Wandur.Desktop.csproj" -c Release --no-restore \
  --disable-build-servers -r "$rid" --self-contained true -o "$stage" \
  -p:Version="$version" -p:InformationalVersion="$version" \
  -p:IncludeSourceRevisionInInformationalVersion=false
"$python" "$project_root/scripts/check-deps-manifest.py" "$stage"

if [[ "$rid" == linux-x64 ]]; then
  [[ -f "$stage/Wandur" ]] || { echo "publish produced no Wandur executable" >&2; exit 1; }
  cp "$project_root/scripts/linux/net.wandur.client.desktop.in" "$stage/"
  cp "$project_root/src/Wandur.Desktop/Assets/icon-256.png" "$stage/net.wandur.client.png"
  cp "$project_root/scripts/linux/install-desktop-entry.sh" "$stage/"
  # Plain modes whatever the source volume gave the files, then the executable bits.
  find "$stage" -type d -exec chmod 755 {} +
  find "$stage" -type f -exec chmod 644 {} +
  chmod 755 "$stage/Wandur" "$stage/install-desktop-entry.sh"
  [[ -f "$stage/createdump" ]] && chmod 755 "$stage/createdump"
  archive="$output/$name.tar.gz"
  tar -C "$work" --exclude='._*' --owner=0 --group=0 --numeric-owner -czf "$archive" "$name"
else
  [[ -f "$stage/Wandur.exe" ]] || { echo "publish produced no Wandur.exe" >&2; exit 1; }
  archive="$output/$name.zip"
  "$python" - "$work" "$name" "$archive" <<'PY'
import os, sys, zipfile
root, name, archive = sys.argv[1:]
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for folder, _, files in os.walk(os.path.join(root, name)):
        for file in sorted(f for f in files if not f.startswith("._")):
            path = os.path.join(folder, file)
            zf.write(path, os.path.relpath(path, root).replace(os.sep, "/"))
PY
fi
echo "Built: $archive"
