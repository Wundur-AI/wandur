# Releasing Wandur

A release is a git tag. Pushing `v<version>` runs `.github/workflows/release.yml`, which tests,
builds self-contained downloads for every platform and publishes a GitHub Release as the
`github-actions` bot. Signed and notarized Mac builds are an optional second step on your Mac.

## Before the first release

- The SDK submodule must be fetchable. `external/wandur-sdk` points at
  `https://github.com/YouCantGoThatWay/wandur-sdk.git`; that repository has to contain the commit
  the client pins (`git -C external/wandur-sdk rev-parse HEAD`). If it does not, every CI checkout
  fails before anything builds. Push `wandur-sdk` first.
- Optional: run the workflow by hand for a dry run. Actions, Release, Run workflow, pick the
  branch. It builds and tests everything with version `0.0.0-dryrun` and keeps the downloads as
  workflow artifacts for 14 days; it creates no release and no tag.

## Checklist

1. **Bump nothing by hand.** The tag is the version. The workflow passes it to
   `dotnet publish -p:Version=... -p:InformationalVersion=...` and writes it into the Mac
   `Info.plist` (`CFBundleShortVersionString` and `CFBundleVersion` get the numeric part, so
   `0.1.0-beta.1` becomes `0.1.0` there; macOS reads those as numbers only).
2. From an up-to-date `main` that has passed CI:

   ```sh
   git tag v0.1.0 && git push origin v0.1.0
   ```

   A tag with a `-` (`v0.1.0-beta.1`, `v0.2.0-rc.1`) is published as a prerelease.
3. **Wait for the workflow** (Actions, Release). It runs the full test matrix from
   `build.yml`, then builds four downloads, starts each one once (`--script-worker`, no window,
   no network), writes `SHA256SUMS.txt` and creates the release titled `Wandur 0.1.0`, with notes
   from `docs/release-notes-template.md` followed by GitHub's generated list of changes. If a
   job fails, no release is created; fix, delete the tag (`git push origin :refs/tags/v0.1.0` and
   `git tag -d v0.1.0`), and tag again.
4. **Optional: signed Mac builds.** On your Mac, with a Developer ID Application certificate in
   the login keychain and a notarytool profile:

   ```sh
   # once: store notarization credentials (prompts for an app-specific password)
   xcrun notarytool store-credentials wandur-notary --apple-id <apple id> --team-id <team id>

   # build, sign, notarize, staple; leaves the disk images in artifacts/release/
   scripts/release-macos-signed.sh 0.1.0

   # the same, then replace the two ad-hoc Mac images and SHA256SUMS.txt on the release,
   # and update the release notes
   WANDUR_RELEASE_UPLOAD=1 scripts/release-macos-signed.sh 0.1.0
   ```

   The upload step refuses to run unless `gh api user --jq .login` is `YouCantGoThatWay`,
   because an asset uploaded from another account shows that account on the release page. It
   prints the commands to switch (`gh auth switch`, `gh auth login`, or `GH_TOKEN=...` for one
   command). Other settings: `WANDUR_SIGN_IDENTITY` (default: the first "Developer ID
   Application:" identity), `WANDUR_NOTARY_PROFILE` (default `wandur-notary`).

   For each architecture the script signs the app inside out, builds the disk image
   (`scripts/macos/make-dmg.sh`), signs the image, notarizes and staples it, then mounts it
   and checks the app with `codesign` and `spctl`. After uploading, it rewrites the release
   notes with `gh release edit --notes-file`: the macOS first-run paragraph between
   `<!-- macos-first-run:start -->` and `<!-- macos-first-run:end -->` becomes "The macOS
   builds are signed and notarized." It checks for those markers before building, so keep
   them if you edit the notes by hand.

## What users see

- **Windows:** `Wandur-<version>-windows-x64.zip`, a folder with `Wandur.exe`. Unsigned, so
  SmartScreen shows "Windows protected your PC" until they click More info, Run anyway.
- **macOS:** `Wandur-<version>-macos-arm64.dmg` and `...-macos-x64.dmg`, each a disk image
  with `Wandur.app` and an Applications shortcut to drag it onto.
  From the workflow they are ad-hoc signed: Gatekeeper calls the app from an unidentified
  developer and the user right-clicks, Open (or on macOS 15, System Settings, Privacy & Security,
  Open Anyway). After the signed script has replaced them, the app opens with a double-click.
- **Linux:** `Wandur-<version>-linux-x64.tar.gz`, a folder with an executable `Wandur`, a
  `.desktop` file, its icon and `install-desktop-entry.sh` to add a menu entry.
- `SHA256SUMS.txt` for all four.

All downloads are folders rather than single-file executables: the app starts itself again as
its script worker, and several dependencies (ONNX Runtime, SQLite, Skia, HarfBuzz) ship native
libraries that a single-file build would unpack to disk on every start.

## Whose name is on the Mac signature

The signature carries the certificate's name, and anyone can read it (`codesign -dv`, or the
Gatekeeper dialog). An **individual** Apple Developer account's Developer ID certificate is
issued in the member's legal name: `Developer ID Application: <Your Legal Name> (TEAMID)`. An
**organization** account's is issued in the organization's name. Before the first signed
release, check which one yours is:

```sh
codesign -dv --verbose=2 artifacts/release/Wandur-<version>-macos-arm64.dmg 2>&1 | grep Authority
```

If the first `Authority=` line shows a personal name you do not want attached to Wandur, do not
upload; the ad-hoc builds from the workflow carry no name at all. The release itself is always
authored by `github-actions[bot]`.

## Notes

- Release builds restore in locked mode, then restore once more for the target runtime without
  the lock (NuGet cannot lock-restore a runtime the lock files do not list).
  `scripts/lock-guard.py` fails the build if that second restore picks a different version of
  anything, and puts the committed lock files back.
- The self-contained runtime is the newest .NET 10 patch on the runner (`dotnet-version:
  10.0.x`), so a release built later carries later runtime security fixes.
- Mac bundles from both the workflow and the signed script keep the managed `.dll` files in
  `Contents/MacOS`, where codesign treats them as nested code and stores their signatures in
  extended attributes. That is why the Mac downloads are disk images: an image keeps those
  attributes, whereas a zip keeps them only when macOS's own tools unpack it, and a bundle
  that lost them is reported as damaged.
