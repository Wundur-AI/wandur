#!/usr/bin/env bash
# Builds, Developer ID signs, notarizes and staples both macOS downloads for a release that
# the release workflow has already published, on the owner's Mac.
#
#   scripts/release-macos-signed.sh 0.1.0
#
# Environment:
#   WANDUR_SIGN_IDENTITY   codesign identity, by common name or hash. Default: the first
#                          "Developer ID Application:" identity in the login keychain.
#   WANDUR_NOTARY_PROFILE  notarytool keychain profile (default wandur-notary), created once with
#                          xcrun notarytool store-credentials wandur-notary --apple-id <id> --team-id <team>
#   WANDUR_RELEASE_UPLOAD  1 uploads the disk images and a refreshed SHA256SUMS.txt to release
#                          v<version> with gh, replacing the ad-hoc signed ones, and rewrites the
#                          release notes' macOS first-run paragraph. Anything else only builds.
#   WANDUR_RELEASE_REPO    GitHub repository (default YouCantGoThatWay/wandur).
#
#   WANDUR_ALLOW_NON_DEVELOPER_ID
#                          1 lets WANDUR_SIGN_IDENTITY name a non-Developer ID identity, for
#                          testing the signing steps only; notarization will reject the result.
#
# It builds exactly the tag v<version>, which must exist in this clone: the tag's tree and its
# submodules' pinned commits are exported with git archive into a scratch directory, so the
# state of this working tree does not matter.
#
# Output: artifacts/release/Wandur-<version>-macos-{arm64,x64}.dmg and SHA256SUMS-macos.txt.
# Order per architecture: sign the app inside out, make the disk image, sign the image, then
# notarize and staple the image (notarizing it covers the app inside).
# This script prints the signing identity's common name and nothing else about the certificate.
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

release_account="YouCantGoThatWay"
repo="${WANDUR_RELEASE_REPO:-YouCantGoThatWay/wandur}"
profile="${WANDUR_NOTARY_PROFILE:-wandur-notary}"
upload="${WANDUR_RELEASE_UPLOAD:-0}"

fail() { echo "error: $*" >&2; exit 1; }

[[ $# -eq 1 ]] || fail "usage: release-macos-signed.sh <version>   (for example 0.1.0)"
version="${1#v}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
  || fail "version must look like 0.1.0 or 0.1.0-beta.1, not $1"
tag="v$version"
[[ "$(uname -s)" == "Darwin" ]] || fail "run this on macOS"

project_root="$(cd "$(dirname "$0")/.." && pwd)"
out_dir="$project_root/artifacts/release"

# 1. The exact tag, exported into a scratch directory on the internal disk (codesign also
# rejects the AppleDouble files that exFAT volumes keep next to files with extended attributes).
git -C "$project_root" rev-parse -q --verify "refs/tags/$tag^{commit}" >/dev/null \
  || fail "tag $tag does not exist in this clone; run git fetch --tags, or create and push the tag first"
tag_commit="$(git -C "$project_root" rev-parse "$tag^{commit}")"

work="$(mktemp -d "${TMPDIR:-/tmp}/wandur-signed.XXXXXX")"
cleanup() {
  local volume
  for volume in "$work"/volume-*; do
    [[ -d "$volume" ]] || continue
    hdiutil detach "$volume" >/dev/null 2>&1 || hdiutil detach -force "$volume" >/dev/null 2>&1 || true
  done
  rm -rf "$work"
}
trap cleanup EXIT

# Exports <commit> of the repository at <repo> into <dest>, then each submodule at the commit
# the tree pins, taken from the local submodule clone (fetched from its remote only if missing).
export_tree() {
  local repo="$1" commit="$2" dest="$3" meta path mode type sha
  mkdir -p "$dest"
  git -C "$repo" archive --format=tar "$commit" | tar -x -C "$dest"
  while IFS=$'\t' read -r meta path; do
    read -r mode type sha <<<"$meta"
    [[ "$mode" == 160000 && "$type" == commit ]] || continue
    git -C "$repo/$path" rev-parse --git-dir >/dev/null 2>&1 \
      || fail "submodule $path is not checked out here; run git submodule update --init --recursive"
    if ! git -C "$repo/$path" cat-file -e "$sha^{commit}" 2>/dev/null; then
      git -C "$repo/$path" fetch --quiet origin "$sha" 2>/dev/null \
        || fail "submodule $path does not have commit $sha, and its remote did not provide it"
    fi
    export_tree "$repo/$path" "$sha" "$dest/$path"
  done < <(git -C "$repo" ls-tree -r "$commit")
}
src="$work/src"
export_tree "$project_root" "$tag_commit" "$src"
echo "Building $tag (${tag_commit:0:12}) from a clean export"
entitlements="$src/scripts/macos/Wandur.entitlements"
for needed in scripts/package-macos.sh scripts/macos/make-dmg.sh scripts/macos/signed-notes.py \
              scripts/macos/Wandur.entitlements; do
  [[ -f "$src/$needed" ]] || fail "$tag has no $needed; tag a commit that includes the release scripts"
done

# 2. Signing identity. find-identity lines look like:  1) <sha1> "Developer ID Application: Name (TEAM)"
# Only the quoted common names are ever printed; the hash stays internal.
identities="$(security find-identity -v -p codesigning 2>/dev/null || true)"
names_only() { sed -nE 's/^ *[0-9]+\) [0-9A-F]{40} "(.*)"/  \1/p; s/^ *([0-9]+ valid identit.*)/  \1/p' <<<"$identities"; }
pick() { # $1: awk test on hash ($1) and name ($2); prints "hash<TAB>name" of the first match
  sed -nE 's/^ *[0-9]+\) ([0-9A-F]{40}) "(.*)"/\1	\2/p' <<<"$identities" | awk -F'\t' "$1 { print; exit }"
}
if [[ -n "${WANDUR_SIGN_IDENTITY:-}" ]]; then
  wanted="$WANDUR_SIGN_IDENTITY"
  match="$(W="$wanted" pick '$1 == ENVIRON["W"] || $2 == ENVIRON["W"]')"
  [[ -n "$match" ]] || { echo "WANDUR_SIGN_IDENTITY does not name a valid codesigning identity. security find-identity shows:" >&2; names_only >&2; exit 1; }
else
  match="$(pick 'index($2, "Developer ID Application:") == 1')"
  if [[ -z "$match" ]]; then
    echo "error: no \"Developer ID Application:\" identity in the keychain. security find-identity -v -p codesigning shows:" >&2
    names_only >&2
    echo "Install the Developer ID Application certificate with its private key (Xcode > Settings > Accounts," >&2
    echo "or developer.apple.com), or set WANDUR_SIGN_IDENTITY." >&2
    exit 1
  fi
fi
sign_hash="${match%%	*}"
sign_name="${match#*	}"
echo "Signing as: $sign_name"
if [[ "$sign_name" != "Developer ID Application:"* ]]; then
  [[ "${WANDUR_ALLOW_NON_DEVELOPER_ID:-0}" == 1 ]] \
    || fail "\"$sign_name\" is not a Developer ID Application identity, and notarization accepts only those. Set WANDUR_ALLOW_NON_DEVELOPER_ID=1 to sign with it anyway for a test."
  echo "warning: not a Developer ID Application identity; notarization will reject the result." >&2
fi

# 3. Tools and credentials, checked before a build that takes minutes.
for tool in codesign ditto spctl; do command -v "$tool" >/dev/null || fail "$tool not found"; done
xcrun --find notarytool >/dev/null 2>&1 || fail "xcrun notarytool not found; install the Xcode command line tools"
xcrun --find stapler >/dev/null 2>&1 || fail "xcrun stapler not found; install the Xcode command line tools"
if ! xcrun notarytool history --keychain-profile "$profile" >/dev/null 2>&1; then
  echo "error: notarytool cannot use keychain profile \"$profile\". Create it once with:" >&2
  echo "  xcrun notarytool store-credentials $profile --apple-id <apple id> --team-id <team id>" >&2
  echo "(it prompts for an app-specific password from appleid.apple.com), or set WANDUR_NOTARY_PROFILE." >&2
  exit 1
fi

if [[ "$upload" == "1" ]]; then
  command -v gh >/dev/null || fail "gh not found; WANDUR_RELEASE_UPLOAD=1 needs the GitHub CLI"
  login="$(gh api user --jq .login 2>/dev/null || true)"
  if [[ "$login" != "$release_account" ]]; then
    echo "error: gh is authenticated as '${login:-nobody}', not $release_account. A release asset uploaded" >&2
    echo "from another account shows that account as its uploader. Switch first, then re-run:" >&2
    echo "  gh auth login --hostname github.com --git-protocol https --web   # sign in as $release_account" >&2
    echo "  gh auth switch --hostname github.com --user $release_account     # if both accounts are logged in" >&2
    echo "or, for this command only:" >&2
    echo "  GH_TOKEN=<token of $release_account> WANDUR_RELEASE_UPLOAD=1 $0 $version" >&2
    exit 1
  fi
  gh release view "$tag" --repo "$repo" >/dev/null 2>&1 \
    || fail "release $tag not found in $repo; push the tag and let the release workflow finish first"
  # Checked now rather than after a long build: the notes must still have the marked paragraph.
  notes_now="$(gh release view "$tag" --repo "$repo" --json body --jq .body)"
  [[ "$notes_now" == *"<!-- macos-first-run:start -->"*"<!-- macos-first-run:end -->"* ]] \
    || fail "the $tag release notes have no <!-- macos-first-run:start/end --> markers; restore them or edit the notes by hand"
fi

# 4. Build, sign, package, notarize.
mkdir -p "$out_dir"

sign() { codesign --force --timestamp --options runtime --sign "$sign_hash" "$@"; }

dmgs=()
for rid in osx-arm64 osx-x64; do
  arch="${rid#osx-}"
  echo "== $rid"
  bash "$src/scripts/package-macos.sh" --rid "$rid" --version "$version" --self-contained \
    --output "$work/$arch" --no-sign
  app="$work/$arch/Wandur.app"

  # Inside out, without --deep: every file in Contents/MacOS is nested code to codesign, the
  # managed .dll files included (their signatures go into extended attributes), and the main
  # executable is signed last, as part of the bundle, with the entitlements. Deepest paths
  # first, so the satellite resource folders are sealed before anything that contains them.
  xattr -cr "$app"
  count=0
  while IFS= read -r file; do
    [[ "$file" == "$app/Contents/MacOS/Wandur" ]] && continue
    out="$(sign "$file" 2>&1)" || { echo "$out" >&2; fail "codesign failed on ${file#"$app/"}"; }
    count=$((count + 1))
  done < <(find "$app/Contents/MacOS" -type f | awk -F/ '{ print NF "\t" $0 }' | sort -rn | cut -f2-)
  out="$(sign --entitlements "$entitlements" "$app" 2>&1)" || { echo "$out" >&2; fail "codesign failed on the bundle"; }
  echo "signed $count nested files and the bundle"
  codesign --verify --strict --deep --verbose=2 "$app"

  # The script worker mode starts the runtime and the Wandur assemblies without opening a
  # window or a connection; it proves the entitlements let the hardened runtime JIT.
  if [[ "$arch" == "arm64" ]]; then runnable="$([[ "$(uname -m)" == "arm64" ]] && echo 1 || true)"
  else runnable="$(arch -x86_64 /usr/bin/true 2>/dev/null && echo 1 || true)"; fi
  if [[ -n "$runnable" ]]; then
    reply="$(echo '{"Kind":"shutdown"}' | "$app/Contents/MacOS/Wandur" --script-worker 2>&1 || true)"
    [[ "$reply" == *'"Results":[]'* ]] || fail "signed $arch app did not start: $reply"
    echo "signed $arch app starts under the hardened runtime"
  else
    echo "skipping the $arch start check: this Mac cannot run $arch code"
  fi

  dmg="$out_dir/Wandur-$version-macos-$arch.dmg"
  rm -f "$dmg"
  bash "$src/scripts/macos/make-dmg.sh" "$app" "$dmg"
  out="$(codesign --force --timestamp --sign "$sign_hash" "$dmg" 2>&1)" || { echo "$out" >&2; fail "codesign failed on $dmg"; }

  echo "notarizing $arch (usually a few minutes)"
  result="$(xcrun notarytool submit "$dmg" --keychain-profile "$profile" --wait --output-format json || true)"
  status="$(python3 -c 'import json,sys; print(json.loads(sys.stdin.read() or "{}").get("status",""))' <<<"$result" 2>/dev/null || true)"
  if [[ "$status" != "Accepted" ]]; then
    id="$(python3 -c 'import json,sys; print(json.loads(sys.stdin.read() or "{}").get("id",""))' <<<"$result" 2>/dev/null || true)"
    echo "error: notarization of $arch returned '${status:-no result}'." >&2
    [[ -n "$id" ]] && xcrun notarytool log "$id" --keychain-profile "$profile" >&2 || echo "$result" >&2
    exit 1
  fi
  xcrun stapler staple "$dmg"
  xcrun stapler validate "$dmg"
  spctl -a -t open --context context:primary-signature -vv "$dmg"

  # What a user gets: the app as it sits in the image, checked by Gatekeeper.
  volume="$work/volume-$arch"
  mkdir -p "$volume"
  hdiutil attach -nobrowse -readonly -mountpoint "$volume" "$dmg" >/dev/null
  checked=0
  codesign --verify --strict --deep "$volume/Wandur.app" && spctl -a -t exec -vv "$volume/Wandur.app" && checked=1
  hdiutil detach "$volume" >/dev/null || hdiutil detach -force "$volume" >/dev/null
  [[ $checked -eq 1 ]] || fail "the app inside $dmg does not pass codesign and Gatekeeper"
  dmgs+=("$dmg")
done

sums="$out_dir/SHA256SUMS-macos.txt"
(cd "$out_dir" && shasum -a 256 "${dmgs[@]##*/}") > "$sums"

if [[ "$upload" != "1" ]]; then
  echo
  echo "Signed, notarized and stapled:"
  printf '  %s\n' "${dmgs[@]}"
  echo "Checksums: $sums"
  echo "To attach them to $tag (as $release_account), re-run with WANDUR_RELEASE_UPLOAD=1."
  exit 0
fi

# The release's SHA256SUMS.txt lists the ad-hoc images; replace those two lines.
gh release download "$tag" --repo "$repo" --pattern SHA256SUMS.txt --dir "$work/sums" --clobber
grep -v -F -e "  Wandur-$version-macos-arm64.dmg" -e "  Wandur-$version-macos-x64.dmg" \
  "$work/sums/SHA256SUMS.txt" > "$work/SHA256SUMS.txt" || true
cat "$sums" >> "$work/SHA256SUMS.txt"
sort -k2 -o "$work/SHA256SUMS.txt" "$work/SHA256SUMS.txt"
cp "$work/SHA256SUMS.txt" "$out_dir/SHA256SUMS.txt"
gh release upload "$tag" "${dmgs[@]}" "$out_dir/SHA256SUMS.txt" --repo "$repo" --clobber

# Only after the signed images are in place: the notes stop telling Mac users to right-click.
gh release view "$tag" --repo "$repo" --json body --jq .body > "$work/notes-before.md"
python3 "$src/scripts/macos/signed-notes.py" "$work/notes-before.md" "$work/notes.md"
gh release edit "$tag" --repo "$repo" --notes-file "$work/notes.md" >/dev/null
echo "Uploaded to $tag as $release_account, and updated the release notes:"
printf '  %s\n' "${dmgs[@]}" "$out_dir/SHA256SUMS.txt"
