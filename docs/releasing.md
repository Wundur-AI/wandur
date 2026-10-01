# Releasing Wandur

A release is a git tag. Pushing `v<version>` runs `.github/workflows/release.yml`, which tests,
builds self-contained downloads for every platform and publishes a GitHub Release as the
`github-actions` bot. Signed and notarized Mac builds are an optional second step on your Mac.

## Before the first release

- The SDK submodule must be fetchable. `external/wandur-sdk` points at
  `https://github.com/Wundur-AI/wandur-sdk.git`; that repository has to contain the commit
  the client pins (`git -C external/wandur-sdk rev-parse HEAD`). If it does not, every CI checkout
  fails before anything builds. Push `wandur-sdk` first.
- Optional: run the workflow by hand for a dry run. Actions, Release, Run workflow, pick the
  branch. It builds and tests everything with version `0.0.0-dryrun` and keeps the downloads as
  workflow artifacts for 14 days; it creates no release and no tag.

## Checklist

1. **Bump nothing by hand.** The tag is the version. The workflow passes it to
   `dotnet publish -p:Version=... -p:InformationalVersion=...` and writes it into the Mac
   `Info.plist`: `CFBundleShortVersionString` gets the numeric part (`0.1.0-beta.1` becomes
   `0.1.0`; macOS reads it as numbers only), and `CFBundleVersion` identifies the build: the
   workflow's run number in CI, the numeric part plus a UTC timestamp
   (`0.1.0.202609261730`) for a local or signed build.
2. From an up-to-date `main` that has passed CI:

   ```sh
   git tag v0.1.0 && git push origin v0.1.0
   ```

   A tag with a `-` (`v0.1.0-beta.1`, `v0.2.0-rc.1`) is published as a prerelease.
3. **Wait for the workflow** (Actions, Release). It runs the full test matrix from
   `build.yml`, then builds four downloads, starts each one once (`--script-worker`, no window,
   no network), writes `SHA256SUMS.txt` and creates the release titled `Wandur 0.1.0`, with notes
   from `docs/release-notes-template.md` followed by GitHub's generated list of changes.

   **If it goes wrong.** A failed build creates no release. A release that went out broken,
   or a tag on the wrong commit, is undone the same way: delete the release if there is one,
   delete the tag on GitHub and locally, fix, then tag and push again.

   ```sh
   gh release delete v0.1.0 --yes            # only if the release was created; run as YouCantGoThatWay
   git push origin :refs/tags/v0.1.0         # delete the tag on GitHub
   git tag -d v0.1.0                          # and locally
   # fix and commit on main, then:
   git tag v0.1.0 && git push origin v0.1.0
   ```

   Anyone who already downloaded the broken build keeps it; if that matters, use the next
   version number instead of reusing this one.
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
   Application:" identity; anything else is refused unless `WANDUR_ALLOW_NON_DEVELOPER_ID=1`,
   which is only useful for testing the signing steps), `WANDUR_NOTARY_PROFILE` (default
   `wandur-notary`).

   The script builds exactly the tag, not your working tree: `v0.1.0` must exist in the
   clone you run it from (`git fetch --tags` if you tagged elsewhere). It exports the tag's
   tree and the SDK commit the tag pins (from your local `external/wandur-sdk`) with
   `git archive` into a temporary directory, builds there, and deletes it afterwards, so
   uncommitted changes and other branches in your checkout do not matter.

   For each architecture the script signs the app inside out, builds the disk image
   (`scripts/macos/make-dmg.sh`), signs the image, notarizes and staples it, then mounts it
   and checks the app with `codesign` and `spctl`. After uploading, it rewrites the release
   notes with `gh release edit --notes-file`: the macOS first-run paragraph between
   `<!-- macos-first-run:start -->` and `<!-- macos-first-run:end -->` becomes "The macOS
   builds are signed and notarized." It checks for those markers before building, so keep
   them if you edit the notes by hand.

## What users see

- **Windows:** `Wandur-<version>-windows-x64.zip`, holding one folder,
  `Wandur-<version>-windows-x64`, with `Wandur.exe` inside it among the files it needs.
  Unsigned, so SmartScreen shows "Windows protected your PC" until they click More info, Run
  anyway.
- **macOS:** `Wandur-<version>-macos-arm64.dmg` and `...-macos-x64.dmg`, each a disk image
  with `Wandur.app` and an Applications shortcut to drag it onto.
  From the workflow they are ad-hoc signed: Gatekeeper calls the app from an unidentified
  developer. On macOS 15 and later the user opens it once, dismisses "Apple could not verify
  ...", then clicks Open Anyway in System Settings > Privacy & Security; on macOS 14 and earlier,
  right-click, Open. After the signed script has replaced them, the app opens with a
  double-click.
- **Linux:** `Wandur-<version>-linux-x64.tar.gz`, a folder with an executable `Wandur`,
  `install-desktop-entry.sh` to add a menu entry, the `.desktop.in` template it fills in with
  the folder's path, and the icon.
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
