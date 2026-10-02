# Releasing Wandur

A release is a git tag. Pushing `v<version>` runs `.github/workflows/release.yml`, which tests,
builds self-contained downloads for every platform, Developer ID signs, notarizes and staples the
two Mac disk images, and publishes a GitHub Release as the `github-actions` bot.

## Before the first release

- The SDK submodule must be fetchable. `external/wandur-sdk` points at
  `https://github.com/Wundur-AI/wandur-sdk.git`; that repository has to contain the commit
  the client pins (`git -C external/wandur-sdk rev-parse HEAD`). If it does not, every CI checkout
  fails before anything builds. Push `wandur-sdk` first.
- The Mac signing secrets. These are Wundur-AI organization secrets, shared with this repository
  (organization Settings, Secrets and variables, Actions, each secret's Repository access):

  | Secret | Value |
  | --- | --- |
  | `MACOS_CERT_P12` | the Developer ID Application certificate with its private key, exported from Keychain Access as .p12, then `base64 -i cert.p12` |
  | `MACOS_CERT_PASSWORD` | the password chosen when exporting that .p12 |
  | `ASC_KEY_P8` | the whole `AuthKey_<id>.p8` file of an App Store Connect API key (Users and Access, Integrations, App Store Connect API), role Developer |
  | `ASC_KEY_ID` | that key's 10-character id |
  | `ASC_ISSUER_ID` | the issuer id shown above the key list |

  The package job runs in the `release` environment. GitHub creates it on the first run if it
  does not exist; to have each signed release wait for your approval, add yourself as a
  required reviewer there. A deployment branch rule on that environment must allow `v*` tags,
  and the branches you start dry runs from.
- Optional: run the workflow by hand for a dry run. Actions, Release, Run workflow, pick the
  branch. It builds and tests everything with version `0.0.0-dryrun` and keeps the downloads as
  workflow artifacts for 14 days; it creates no release and no tag, and it reads no secret: its
  Mac images are ad-hoc signed.

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
   `build.yml`, then builds four downloads. Each Mac leg first checks the signing secrets (each
   one's length, that the .p12 opens with its password and holds a private key, and that the
   key id and issuer id have the right shape), imports the certificate into a temporary keychain
   and confirms it holds a Developer ID Application identity. It then packages the app unsigned
   and runs `scripts/macos/sign-and-notarize.sh` with the API key, which signs every file in
   `Contents/MacOS` with the hardened runtime, sixteen at a time, checks each one, signs the
   bundle with `scripts/macos/Wandur.entitlements`, builds and signs the disk image, notarizes
   it and staples the ticket. The keychain and the key file are deleted at the end of the job,
   whatever happened. Every download is started once (`--script-worker`, no window, no network);
   the Mac images are also checked with `spctl` (the image and the app inside it) and
   `xcrun stapler validate`. Then it writes `SHA256SUMS.txt` and creates the release titled
   `Wandur 0.1.0`, with notes from `docs/release-notes-template.md` followed by GitHub's
   generated list of changes.

   **Reading a failure.** Every failure in the signing steps is an annotation on the run's
   summary page, so you can read it without opening the job log. A notarization that is not
   Accepted also prints Apple's log for the submission, which names each file Apple objected to.

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
4. **Signing on your Mac instead.** Only needed to replace the ad-hoc images on a release made
   before the workflow signed them (v0.1.2 and earlier), or when the workflow cannot sign. With
   the Developer ID Application certificate in your login keychain and a notarytool profile:

   ```sh
   # once: store notarization credentials (prompts for an app-specific password)
   xcrun notarytool store-credentials wandur-notary --apple-id <apple id> --team-id <team id>

   # build, sign, notarize, staple; leaves the disk images in artifacts/release/
   bash scripts/release-macos-signed.sh 0.1.0

   # the same, then replace the two ad-hoc Mac images and SHA256SUMS.txt on the release,
   # and update the release notes
   WANDUR_RELEASE_UPLOAD=1 bash scripts/release-macos-signed.sh 0.1.0
   ```

   It builds exactly the tag, not your working tree: `v0.1.0` must exist in the clone you run it
   from (`git fetch --tags` if you tagged elsewhere). It exports the tag's tree and the SDK commit
   the tag pins with `git archive` into a temporary directory and builds there. The signing is
   the same `scripts/macos/sign-and-notarize.sh` the workflow runs, taken from your checkout
   rather than the tag, so a tag older than that script can still be signed; locally it uses the
   keychain profile (`WANDUR_NOTARY_PROFILE`, default `wandur-notary`) instead of an API key.
   The script also starts the signed app once and checks the app inside the mounted image with
   `codesign` and `spctl`.

   The upload step refuses to run unless `gh api user --jq .login` is `YouCantGoThatWay`,
   because an asset uploaded from another account shows that account on the release page. It
   prints the commands to switch. After uploading it rewrites the release notes with
   `scripts/macos/signed-notes.py`: the paragraph between `<!-- macos-first-run:start -->` and
   `<!-- macos-first-run:end -->` becomes the sentence the template now carries for signed
   builds. Other settings: `WANDUR_SIGN_IDENTITY` (default: the first "Developer ID
   Application:" identity; anything else is refused unless `WANDUR_ALLOW_NON_DEVELOPER_ID=1`,
   which is only useful for testing the signing steps).

   Run the scripts with `bash` (or `zsh`) as shown: this repository lives on an exFAT drive,
   which does not keep the executable bit.

## What users see

- **Windows:** `Wandur-<version>-windows-x64.zip`, holding one folder,
  `Wandur-<version>-windows-x64`, with `Wandur.exe` inside it among the files it needs.
  Unsigned, so SmartScreen shows "Windows protected your PC" until they click More info, Run
  anyway.
- **macOS:** `Wandur-<version>-macos-arm64.dmg` and `...-macos-x64.dmg`, each a disk image
  that opens as an installer window: `Wandur.app` on the left, an Applications shortcut on the
  right, and a background that says to drag one onto the other. The layout is
  `scripts/macos/dmg-settings.py`, built with dmgbuild; the background is
  `scripts/macos/dmg-background.tiff`, drawn by `scripts/macos/dmg-background.py` (run it on a
  Mac and commit the result after changing the design, keeping both files' positions in step).
  Signed with the Developer ID of Wundur AI Learning, LLC and notarized, so the app opens with
  a double-click. (Releases up to v0.1.2 were ad-hoc signed until replaced: Gatekeeper called
  the app from an unidentified developer, and users had to use Open Anyway in System Settings >
  Privacy & Security, or right-click, Open on macOS 14 and earlier.)
- **Linux:** `Wandur-<version>-linux-x64.tar.gz`, a folder with an executable `Wandur`,
  `install-desktop-entry.sh` to add a menu entry, the `.desktop.in` template it fills in with
  the folder's path, and the icon.
- `SHA256SUMS.txt` for all four.

All downloads are folders rather than single-file executables: the app starts itself again as
its script worker, and several dependencies (ONNX Runtime, SQLite, Skia, HarfBuzz) ship native
libraries that a single-file build would unpack to disk on every start.

## Whose name is on the Mac signature

The signature carries the certificate's name, and anyone can read it (`codesign -dv`, or the
Gatekeeper dialog). Wundur's Apple Developer account is an organization account, so its
Developer ID certificate reads `Developer ID Application: Wundur AI Learning, LLC (TEAMID)`; no
personal name appears. The workflow prints the identity it signs with as a notice on the run.
If the company is renamed, Apple issues certificates in the new name only after the developer
account's legal entity is updated; replace `MACOS_CERT_P12` and `MACOS_CERT_PASSWORD` then. The
release itself is always authored by `github-actions[bot]`.

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
