#!/usr/bin/env python3
"""Rewrites a release body for signed Mac builds.

  signed-notes.py <body-in> <body-out>

Replaces everything between <!-- macos-first-run:start --> and <!-- macos-first-run:end -->
(the right-click, Open instructions from docs/release-notes-template.md) with one sentence.
The markers stay, so running it again gives the same result. Fails unless there is exactly
one marked paragraph.
"""
import re
import sys

START = "<!-- macos-first-run:start -->"
END = "<!-- macos-first-run:end -->"
SIGNED = "The macOS builds are signed and notarized."

if len(sys.argv) != 3:
    sys.exit(__doc__)
body = open(sys.argv[1], encoding="utf-8").read()
new, count = re.subn(re.escape(START) + r".*?" + re.escape(END),
                     lambda _: f"{START}\n{SIGNED}\n{END}", body, flags=re.S)
if count != 1:
    sys.exit(f"expected one marked macOS paragraph in the release notes, found {count}")
open(sys.argv[2], "w", encoding="utf-8").write(new)
