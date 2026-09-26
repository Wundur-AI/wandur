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
#   WANDUR_RELEASE_UPLOAD  1 uploads the zips and a refreshed SHA256SUMS.txt to release v<version>
#                          with gh, replacing the ad-hoc signed ones. Anything else only builds.
#   WANDUR_RELEASE_REPO    GitHub repository (default YouCantGoThatWay/wandur).
#
# Output: artifacts/release/Wandur-<version>-macos-{arm64,x64}.zip and SHA256SUMS-macos.txt.
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
entitlements="$project_root/scripts/macos/Wandur.entitlements"
out_dir="$project_root/artifacts/release"

# 1. Signing identity. find-identity lines look like:  1) <sha1> "Developer ID Application: Name (TEAM)"
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
[[ "$sign_name" == "Developer ID Application:"* ]] \
  || echo "warning: this is not a Developer ID Application identity; notarization will reject it." >&2

# 2. Tools and credentials, checked before a build that takes minutes.
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
fi

# 3. Build in a scratch directory on the internal disk: codesign rejects the AppleDouble
# files that exFAT and similar volumes keep next to every file with extended attributes.
work="$(mktemp -d "${TMPDIR:-/tmp}/wandur-signed.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out_dir"

sign() { codesign --force --timestamp --options runtime --sign "$sign_hash" "$@"; }

zips=()
for rid in osx-arm64 osx-x64; do
  arch="${rid#osx-}"
  echo "== $rid"
  bash "$project_root/scripts/package-macos.sh" --rid "$rid" --version "$version" --self-contained \
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

  echo "notarizing $arch (usually a few minutes)"
  submission="$work/Wandur-$arch-notary.zip"
  ditto -c -k --keepParent "$app" "$submission"
  result="$(xcrun notarytool submit "$submission" --keychain-profile "$profile" --wait --output-format json || true)"
  status="$(python3 -c 'import json,sys; print(json.loads(sys.stdin.read() or "{}").get("status",""))' <<<"$result" 2>/dev/null || true)"
  if [[ "$status" != "Accepted" ]]; then
    id="$(python3 -c 'import json,sys; print(json.loads(sys.stdin.read() or "{}").get("id",""))' <<<"$result" 2>/dev/null || true)"
    echo "error: notarization of $arch returned '${status:-no result}'." >&2
    [[ -n "$id" ]] && xcrun notarytool log "$id" --keychain-profile "$profile" >&2 || echo "$result" >&2
    exit 1
  fi
  xcrun stapler staple "$app"
  xcrun stapler validate "$app"
  spctl -a -t exec -vv "$app"

  zip="$out_dir/Wandur-$version-macos-$arch.zip"
  rm -f "$zip"
  ditto -c -k --keepParent "$app" "$zip"
  zips+=("$zip")
done

sums="$out_dir/SHA256SUMS-macos.txt"
(cd "$out_dir" && shasum -a 256 "${zips[@]##*/}") > "$sums"

if [[ "$upload" != "1" ]]; then
  echo
  echo "Signed and notarized:"
  printf '  %s\n' "${zips[@]}"
  echo "Checksums: $sums"
  echo "To attach them to $tag (as $release_account), re-run with WANDUR_RELEASE_UPLOAD=1."
  exit 0
fi

# The release's SHA256SUMS.txt lists the ad-hoc zips; replace those two lines.
gh release download "$tag" --repo "$repo" --pattern SHA256SUMS.txt --dir "$work/sums" --clobber
grep -v -E "  Wandur-$version-macos-(arm64|x64)\.zip$" "$work/sums/SHA256SUMS.txt" > "$work/SHA256SUMS.txt" || true
cat "$sums" >> "$work/SHA256SUMS.txt"
sort -k2 -o "$work/SHA256SUMS.txt" "$work/SHA256SUMS.txt"
cp "$work/SHA256SUMS.txt" "$out_dir/SHA256SUMS.txt"
gh release upload "$tag" "${zips[@]}" "$out_dir/SHA256SUMS.txt" --repo "$repo" --clobber
echo "Uploaded to $tag as $release_account:"
printf '  %s\n' "${zips[@]}" "$out_dir/SHA256SUMS.txt"
