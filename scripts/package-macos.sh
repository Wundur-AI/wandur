#!/usr/bin/env bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1
# Do not reuse worker nodes left by another build on this machine, and do not leave
# any behind. Shared nodes carry cached project state between unrelated solutions.
export MSBUILDDISABLENODEREUSE=1

usage() {
  cat >&2 <<'USAGE'
usage: package-macos.sh [--clean] [--force]
       package-macos.sh --rid osx-arm64|osx-x64 --version X [--self-contained]
                        [--output DIR] [--build-number N] [--sign-adhoc|--no-sign]

With no --rid this is the local development build: a framework-dependent bundle
at artifacts/macos/Wandur.app, version 0.1.0.

  --clean  Delete obj/ and bin/ first. Off by default: a cold graph here has
           produced a Wandur.deps.json missing its own project references, which
           builds and packages cleanly and then aborts on launch. The incremental
           path does not do this. Use it only when you suspect stale output, and
           check the result.
  --force  Package even if Wandur is running or Rider has the solution open.

  --rid             Release build for one architecture. Restores in locked mode,
                    publishes for that runtime and puts the lock files back
                    afterwards (scripts/lock-guard.py). Needs --version.
  --version         SemVer without the leading v, for example 0.1.0 or
                    0.1.0-beta.1. Sets Version and InformationalVersion; the
                    Info.plist gets the numeric part (0.1.0), because macOS
                    reads CFBundleShortVersionString as numbers only.
  --build-number    CFBundleVersion. Default with --rid: $GITHUB_RUN_NUMBER on
                    GitHub Actions, otherwise the numeric version plus a UTC
                    timestamp (0.1.0.202609261730), so every local build differs.
  --self-contained  Bundle the .NET runtime, so users need nothing installed.
  --output          Directory for Wandur.app (default artifacts/macos/<rid>).
  --sign-adhoc      Ad-hoc sign the finished bundle (codesign --deep -s -). The
                    default with --rid: an unsigned bundle fails verification and
                    Apple Silicon will not run it once downloaded.
  --no-sign         Leave the bundle unsigned, for a caller that signs it next
                    (scripts/release-macos-signed.sh).
USAGE
  exit 2
}

clean=0
force=0
rid=""
version=""
self_contained=0
output=""
sign_adhoc=""
build_number=""
need_value() { [[ $# -ge 2 && -n "$2" ]] || { echo "$1 needs a value" >&2; usage; }; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --clean) clean=1 ;;
    --no-clean) clean=0 ;;
    --force) force=1 ;;
    --rid) need_value "$@"; rid="$2"; shift ;;
    --version) need_value "$@"; version="$2"; shift ;;
    --self-contained) self_contained=1 ;;
    --output) need_value "$@"; output="$2"; shift ;;
    --sign-adhoc) sign_adhoc=1 ;;
    --no-sign) sign_adhoc=0 ;;
    --build-number) need_value "$@"; build_number="$2"; shift ;;
    -h|--help) usage ;;
    *) echo "unknown option: $1" >&2; usage ;;
  esac
  shift
done

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Build the macOS app on macOS. Use dotnet publish for other platforms." >&2
  exit 1
fi

case "$rid" in
  ""|osx-arm64|osx-x64) ;;
  *) echo "--rid must be osx-arm64 or osx-x64, not $rid" >&2; exit 2 ;;
esac
if [[ -n "$rid" && -z "$version" ]]; then
  echo "--rid needs --version" >&2; exit 2
fi
if [[ -z "$rid" && ( $self_contained -eq 1 || -n "$output" ) ]]; then
  echo "--self-contained and --output need --rid" >&2; exit 2
fi
if [[ -n "$version" ]] && ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "--version must look like 1.2.3 or 1.2.3-beta.1 (no leading v), not $version" >&2; exit 2
fi
if [[ -n "$build_number" ]] && ! [[ "$build_number" =~ ^[0-9]+(\.[0-9]+)*$ ]]; then
  echo "--build-number must be dot-separated integers, not $build_number" >&2; exit 2
fi
# CFBundleShortVersionString is the numeric SemVer core (macOS reads it as integers only).
# CFBundleVersion identifies the build: the CI run number, or the core plus a timestamp.
bundle_version="${version%%-*}"
bundle_version="${bundle_version:-0.1.0}"
if [[ -n "$build_number" ]]; then
  bundle_build="$build_number"
elif [[ -n "$rid" && -n "${GITHUB_RUN_NUMBER:-}" ]]; then
  bundle_build="$GITHUB_RUN_NUMBER"
elif [[ -n "$rid" ]]; then
  bundle_build="$bundle_version.$(date -u +%Y%m%d%H%M)"
else
  bundle_build=1
fi

if [[ -z "$sign_adhoc" ]]; then
  sign_adhoc=0
  [[ -n "$rid" ]] && sign_adhoc=1
fi

# Physical path (pwd -P): restoring through a symlinked path (~/wandur here, /var -> /private/var
# for temp directories) leaves the project references out of project.assets.json, and the build
# then fails with CS0234 on every Wandur.Models type.
project_root="$(cd "$(dirname "$0")/.." && pwd -P)"
if [[ -n "$rid" ]]; then
  bundle_parent="${output:-$project_root/artifacts/macos/$rid}"
  mkdir -p "$bundle_parent"
  bundle_parent="$(cd "$bundle_parent" && pwd -P)"
else
  bundle_parent="$project_root/artifacts/macos"
fi
app_bundle="$bundle_parent/Wandur.app"

# The bundle is replaced with rsync --delete, which pulls files out from under a
# running process. Quit the app rather than debug the crash that follows.
if [[ $force -eq 0 ]] && pgrep -f "$app_bundle/Contents/MacOS/Wandur" >/dev/null 2>&1; then
  echo "Wandur is running from $app_bundle. Quit it first, or pass --force." >&2
  exit 1
fi

# A run killed before its trap fired leaves its publish directory behind.
mkdir -p "$project_root/artifacts/macos"
rm -rf "$project_root/artifacts/macos/".publish.*

if [[ $clean -eq 1 ]]; then
  # Rider restores into obj/ in the background whenever it notices the project
  # change that clearing obj/ is. Two restores in one directory produce
  # "the file ... already exists", an empty obj/<config>/<tfm>/ref/ that fails the
  # next project with CS0006, or a bin/ missing its deps.json. All three look like
  # build bugs and none of them are, so this stops before starting the fight.
  # Match Rider's backend only. The JetBrains Toolbox daemon is not a builder, and
  # idle MSBuild worker nodes (/nodemode:, kept alive by nodeReuse) linger after every
  # build, so matching those would refuse to run almost always.
  if pgrep -f 'ReSharperHost|JetBrains\.Roslyn\.Worker' >/dev/null 2>&1; then
    if [[ $force -eq 0 ]]; then
      echo "Rider has this solution open and will restore into obj/ while this script" >&2
      echo "clears it. Quit Rider, or re-run with --no-clean to skip the clean." >&2
      exit 1
    fi
    echo "warning: cleaning while Rider is open; output may be corrupt." >&2
  fi
  # Driven off the project files so new projects and the SDK submodule are included,
  # and so directory-server/.venv is never in scope.
  while IFS= read -r project; do
    rm -rf "$(dirname "$project")/obj" "$(dirname "$project")/bin"
  done < <(find "$project_root/src" "$project_root/tests" "$project_root/external" \
             -name '*.csproj' -not -path '*/.venv/*' 2>/dev/null)
fi

# Serial, because after a clean every project creates its obj/ files from nothing and
# projects sharing a dependency have raced to write the same nuget.g.props, failing with
# "the file ... already exists". A no-op when everything is already restored.
# A release build restores in locked mode first, so it fails on a lock file that does not
# match the projects instead of quietly resolving something else.
restore_mode=()
[[ -n "$rid" ]] && restore_mode=(--locked-mode)
if ! dotnet restore "$project_root/Wandur.sln" --disable-parallel ${restore_mode[@]+"${restore_mode[@]}"}; then
  echo "restore failed, retrying once" >&2
  sleep 2
  dotnet restore "$project_root/Wandur.sln" --disable-parallel ${restore_mode[@]+"${restore_mode[@]}"}
fi

# Publish into an empty directory: package timestamps can be older than DLLs
# left by a previous dependency version, which incremental publishing may skip.
publish_dir="$(mktemp -d "$project_root/artifacts/macos/.publish.XXXXXX")"
lock_store=""
cleanup() {
  local status=$?
  rm -rf "$publish_dir"
  if [[ -n "$lock_store" ]]; then
    python3 "$project_root/scripts/lock-guard.py" check "$project_root" "$lock_store" || status=1
    rm -rf "$lock_store"
  fi
  exit $status
}
trap cleanup EXIT

publish_args=(-c Release --no-restore --disable-build-servers -o "$publish_dir")
if [[ -n "$rid" ]]; then
  sc=false
  [[ $self_contained -eq 1 ]] && sc=true
  # Locked mode cannot restore for a runtime the lock files do not list (NU1004), so the
  # runtime restore runs unlocked and lock-guard.py undoes and checks what it writes.
  lock_store="$(mktemp -d "$project_root/artifacts/macos/.locks.XXXXXX")"
  python3 "$project_root/scripts/lock-guard.py" snapshot "$project_root" "$lock_store"
  dotnet restore "$project_root/src/Wandur.Desktop/Wandur.Desktop.csproj" --disable-parallel \
    -r "$rid" -p:SelfContained=$sc
  publish_args+=(-r "$rid" --self-contained "$sc"
    -p:Version="$version" -p:InformationalVersion="$version"
    -p:IncludeSourceRevisionInInformationalVersion=false)
else
  publish_args+=(--no-self-contained)
fi

dotnet publish "$project_root/src/Wandur.Desktop/Wandur.Desktop.csproj" "${publish_args[@]}"
mkdir -p "$app_bundle/Contents/MacOS"

# A publish that produced no launcher must not reach the bundle: rsync --delete
# would empty a working app and leave nothing to fall back to.
if [[ ! -x "$publish_dir/Wandur" ]]; then
  echo "Publish produced no Wandur executable; leaving the existing bundle alone." >&2
  exit 1
fi

# A publish has emitted a deps.json missing its own project references, and that app aborts
# on launch although every file is present. See scripts/check-deps-manifest.py.
python3 "$project_root/scripts/check-deps-manifest.py" "$publish_dir"

# A release bundle starts empty, so no signature or file from an earlier build survives.
if [[ -n "$rid" ]]; then
  rm -rf "$app_bundle"
  mkdir -p "$app_bundle/Contents/MacOS"
fi
# On exFAT (this drive) extended attributes appear as AppleDouble ._ files. Copied into a bundle
# on APFS they become real files that codesign seals, and unzipping turns them back into
# attributes, which breaks the seal ("a sealed resource is missing").
rsync -a --delete --exclude='._*' "$publish_dir/" "$app_bundle/Contents/MacOS/"

# Remove symbols left by an earlier build of this bundle; Release omits them.
rm -f "$app_bundle/Contents/MacOS/Wandur.pdb" "$app_bundle/Contents/MacOS/Wandur.Core.pdb"

# Bundle icon, generated from the 1024 px master by scripts/make-icons.sh.
bash "$project_root/scripts/make-icons.sh" >/dev/null
mkdir -p "$app_bundle/Contents/Resources"
cp "$project_root/artifacts/icons/Wandur.icns" "$app_bundle/Contents/Resources/Wandur.icns"

cat > "$app_bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Wandur</string>
  <key>CFBundleDisplayName</key><string>Wandur</string>
  <key>CFBundleIdentifier</key><string>net.wandur.client</string>
  <key>CFBundleExecutable</key><string>Wandur</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>Wandur</string>
  <key>CFBundleShortVersionString</key><string>$bundle_version</string>
  <key>CFBundleVersion</key><string>$bundle_build</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

# Extended attributes (quarantine, provenance, Finder info) are "detritus" to codesign.
[[ -n "$rid" ]] && xattr -cr "$app_bundle"

if [[ $sign_adhoc -eq 1 ]]; then
  # Ad-hoc: no identity, so Gatekeeper still warns, but the bundle's seal is consistent.
  # Apple Silicon refuses to run arm64 code with no signature at all.
  codesign --force --deep -s - "$app_bundle"
  codesign --verify --strict --deep "$app_bundle"
fi

echo "Built: $app_bundle"
