#!/usr/bin/env bash
# Developer ID signs a finished Wandur.app, wraps it in a disk image, signs the image, notarizes it
# and staples the ticket. Used by the release workflow and by scripts/release-macos-signed.sh.
#
#   scripts/macos/sign-and-notarize.sh <path/to/Wandur.app> <out.dmg>
#
# Environment:
#   WANDUR_SIGN_IDENTITY   codesign identity, by SHA-1 hash or common name (required). It must be
#                          in the keychain search list.
#   Notarization credentials, one of:
#     WANDUR_NOTARY_KEY, WANDUR_NOTARY_KEY_ID, WANDUR_NOTARY_ISSUER
#                          an App Store Connect API key: the .p8 file's path, its key id and the
#                          issuer id (the release workflow).
#     WANDUR_NOTARY_PROFILE
#                          a notarytool keychain profile (a Mac with one stored).
#
# Order: every file in Contents/MacOS is signed on its own with the hardened runtime (the managed
# .dll files included; their signatures go into extended attributes), then the bundle with the
# entitlements, which seals the main executable. No --deep: it would re-sign the nested files
# without the hardened runtime options. Then the disk image is built, signed, notarized and
# stapled; notarizing the image covers the app inside it.
set -euo pipefail

# Failures are annotations in the release workflow: a job's log needs a signed-in viewer, its
# annotations show on the run's summary page.
fail() {
  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then echo "::error title=macOS signing::$*"; else echo "error: $*" >&2; fi
  exit 1
}

[[ $# -eq 2 ]] || fail "usage: sign-and-notarize.sh <Wandur.app> <out.dmg>"
app="${1%/}"
dmg="$2"
here="$(cd "$(dirname "$0")" && pwd -P)"
entitlements="$here/Wandur.entitlements"
[[ -d "$app/Contents/MacOS" && -f "$app/Contents/MacOS/Wandur" ]] || fail "not a Wandur app bundle: $app"
[[ -f "$entitlements" ]] || fail "missing $entitlements"
identity="${WANDUR_SIGN_IDENTITY:-}"
[[ -n "$identity" ]] || fail "WANDUR_SIGN_IDENTITY is not set"

if [[ -n "${WANDUR_NOTARY_KEY:-}" ]]; then
  [[ -f "$WANDUR_NOTARY_KEY" ]] || fail "WANDUR_NOTARY_KEY does not name a file"
  [[ -n "${WANDUR_NOTARY_KEY_ID:-}" && -n "${WANDUR_NOTARY_ISSUER:-}" ]] \
    || fail "WANDUR_NOTARY_KEY needs WANDUR_NOTARY_KEY_ID and WANDUR_NOTARY_ISSUER"
  notary=(--key "$WANDUR_NOTARY_KEY" --key-id "$WANDUR_NOTARY_KEY_ID" --issuer "$WANDUR_NOTARY_ISSUER")
elif [[ -n "${WANDUR_NOTARY_PROFILE:-}" ]]; then
  notary=(--keychain-profile "$WANDUR_NOTARY_PROFILE")
else
  fail "no notarization credentials: set WANDUR_NOTARY_KEY, WANDUR_NOTARY_KEY_ID and WANDUR_NOTARY_ISSUER, or WANDUR_NOTARY_PROFILE"
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/wandur-sign.XXXXXX")"
trap 'rm -rf "$work"' EXIT

# 1. The nested files, sixteen codesign processes at a time. Each file is signed on its own, so
# the order among them does not matter; only the bundle has to come after all of them.
xattr -cr "$app"
nested="$work/nested"
find "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/Wandur" -print0 > "$nested"
count="$(tr -cd '\0' < "$nested" | wc -c | tr -d ' ')"
[[ "$count" -gt 0 ]] || fail "no files to sign in $app/Contents/MacOS"
if ! xargs -0 -P 16 -n 16 codesign --force --timestamp --options runtime --sign "$identity" \
    < "$nested" > "$work/sign.log" 2>&1; then
  cat "$work/sign.log" >&2
  fail "codesign failed on nested files in ${app##*/} (the log above names them)"
fi

# Every file, checked afterwards, so a signature a parallel run left half written cannot ship.
xargs -0 -P 16 -n 16 sh -c 'for f; do codesign --verify --strict "$f" >/dev/null 2>&1 || printf "%s\n" "$f"; done' sh \
  < "$nested" > "$work/unverified"
if [[ -s "$work/unverified" ]]; then
  sed "s|^$app/||" "$work/unverified" >&2
  fail "$(wc -l < "$work/unverified" | tr -d ' ') nested files did not verify after signing (listed above)"
fi

# 2. The bundle, with the entitlements the runtime needs under the hardened runtime (JIT).
codesign --force --timestamp --options runtime --entitlements "$entitlements" --sign "$identity" "$app" \
  > "$work/bundle.log" 2>&1 || { cat "$work/bundle.log" >&2; fail "codesign failed on the bundle ${app##*/}"; }
codesign --verify --strict --deep "$app" > "$work/verify.log" 2>&1 \
  || { cat "$work/verify.log" >&2; fail "${app##*/} does not verify after signing"; }
echo "Signed $count nested files and the bundle"

# 3. The disk image, signed.
rm -f "$dmg"
bash "$here/make-dmg.sh" "$app" "$dmg"
codesign --force --timestamp --sign "$identity" "$dmg" > "$work/dmg.log" 2>&1 \
  || { cat "$work/dmg.log" >&2; fail "codesign failed on ${dmg##*/}"; }

# 4. Notarized and stapled. Anything but Accepted prints Apple's log, which names each problem.
echo "Notarizing ${dmg##*/} (usually a few minutes)"
result="$(xcrun notarytool submit "$dmg" "${notary[@]}" --wait --output-format json 2>"$work/notary.err" || true)"
field() { python3 -c 'import json,sys
try: print(json.loads(sys.stdin.read() or "{}").get(sys.argv[1], ""))
except ValueError: print("")' "$1" <<<"$result"; }
status="$(field status)"
if [[ "$status" != "Accepted" ]]; then
  id="$(field id)"
  cat "$work/notary.err" >&2
  if [[ -n "$id" ]]; then
    echo "notarytool log $id:" >&2
    xcrun notarytool log "$id" "${notary[@]}" >&2 || true
  else
    echo "$result" >&2
  fi
  fail "notarization of ${dmg##*/} returned '${status:-no result}'${id:+ (submission $id)}"
fi
xcrun stapler staple "$dmg" >/dev/null || fail "stapling ${dmg##*/} failed"
xcrun stapler validate "$dmg" >/dev/null || fail "the stapled ticket on ${dmg##*/} does not validate"
spctl --assess --type open --context context:primary-signature -vv "$dmg" \
  || fail "Gatekeeper does not accept ${dmg##*/}"
echo "Signed, notarized and stapled: $dmg"
