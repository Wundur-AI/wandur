Wandur {{VERSION}}, a desktop client for MUDs, MUSHes and other text games. More at [wandur.net](https://wandur.net).

## What's in this build

The Wandur client built from tag `{{TAG}}`, for Windows, macOS (Apple silicon and Intel) and
Linux. Each download is self-contained, so there is nothing else to install. The changes since the previous release are listed at the end of these notes.

## Downloads

| System | File |
| --- | --- |
| Windows 10 or 11 (x64) | `Wandur-{{VERSION}}-windows-x64.zip` |
| macOS, Apple silicon (M1 and later) | `Wandur-{{VERSION}}-macos-arm64.dmg` |
| macOS, Intel | `Wandur-{{VERSION}}-macos-x64.dmg` |
| Linux (x64) | `Wandur-{{VERSION}}-linux-x64.tar.gz` |

`SHA256SUMS.txt` lists the checksum of each file.

## First run

**Windows.** The zip holds one folder, `Wandur-{{VERSION}}-windows-x64`, and `Wandur.exe` is
inside it, next to the files it needs. Extract the whole zip somewhere you keep programs (not
just `Wandur.exe`), open the folder and run `Wandur.exe`. The build is not code-signed yet, so
SmartScreen may say "Windows protected your PC". Click **More info**, then **Run anyway**.
Windows asks once per download.

**macOS.** Open the `.dmg` and drag Wandur onto Applications.

<!-- macos-first-run:start -->
The macOS builds are signed and notarized, so Wandur opens with a double-click like any other app.
<!-- macos-first-run:end -->

**Linux.** Unpack and run it from the folder:

```sh
tar -xzf Wandur-{{VERSION}}-linux-x64.tar.gz
cd Wandur-{{VERSION}}-linux-x64
chmod +x Wandur    # tar keeps the bit; some archive tools drop it
./Wandur
```

To add Wandur to your application menu, run `./install-desktop-entry.sh` from that folder after
moving it where you want to keep it. Saved passwords use the Secret Service (`secret-tool`, from
`libsecret-tools` on Debian and Ubuntu).

## Checking a download

```sh
sha256sum -c SHA256SUMS.txt --ignore-missing      # Linux
shasum -a 256 -c SHA256SUMS.txt --ignore-missing  # macOS
```

On Windows: `Get-FileHash Wandur-{{VERSION}}-windows-x64.zip` in PowerShell, and compare with the file.

Source, issues and the full history: https://github.com/{{REPOSITORY}}/tree/{{TAG}}
