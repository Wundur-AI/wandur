#!/usr/bin/env python3
# usage: check-deps-manifest.py <publish-dir>
# A publish has emitted a deps.json missing its own project references. The host builds
# its assembly list from that manifest, so Wandur.Core.dll sat in the bundle where nothing
# would ever look at it and the app aborted on launch with FileNotFoundException. Every
# file was present and the bundle looked correct, which is exactly why this is checked:
# a complete set of files is not the same thing as an app that starts.
import json, pathlib, sys
publish = pathlib.Path(sys.argv[1])
manifest = publish / "Wandur.deps.json"
if not manifest.is_file():
    sys.exit("publish produced no Wandur.deps.json")
deps = json.loads(manifest.read_text(encoding="utf-8"))
listed = {pathlib.PurePosixPath(path).name
          for target in deps.get("targets", {}).values()
          for library in target.values()
          for path in (library.get("runtime") or {})}
missing = sorted(dll.name for dll in publish.glob("Wandur*.dll") if dll.name not in listed)
if missing:
    sys.exit("deps.json does not list " + ", ".join(missing)
             + "; the app would abort on launch. Re-run; this is a bad build, not a code error.")
