#!/usr/bin/env python3
"""Keeps a runtime-specific publish from changing the committed NuGet lock files.

The lock files are written without runtime identifiers, so `dotnet restore --locked-mode -r <rid>`
fails with NU1004, and NuGet refuses RestorePackagesWithLockFile=false while a lock file exists.
A RID publish therefore restores unlocked and rewrites every lock file in its graph. This script
brackets that restore:

  lock-guard.py snapshot <repo-root> <dir>   copy every packages.lock.json into <dir>
  lock-guard.py check <repo-root> <dir>      fail if the publish changed a pinned version,
                                             then put the original files back either way

"Changed" means: a framework section that existed before is not identical, or a package in a
new runtime section (for example net10.0/osx-arm64) resolved to a different version than the
same package in its framework section. Adding runtime sections is expected and is undone.
"""
import json
import pathlib
import shutil
import sys

SEARCH = ("src", "tests", "external")


def lock_files(root: pathlib.Path):
    for top in SEARCH:
        base = root / top
        if not base.is_dir():
            continue
        for path in base.rglob("packages.lock.json"):
            parts = set(path.relative_to(root).parts)
            if parts & {"bin", "obj", ".venv"}:
                continue
            yield path


def snapshot(root: pathlib.Path, store: pathlib.Path) -> None:
    store.mkdir(parents=True, exist_ok=True)
    count = 0
    for path in lock_files(root):
        target = store / path.relative_to(root)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        count += 1
    if count == 0:
        sys.exit(f"lock-guard: no packages.lock.json under {root}")
    print(f"lock-guard: saved {count} lock files")


def drift(original: dict, current: dict, name: str) -> list[str]:
    problems = []
    before = original.get("dependencies", {})
    after = current.get("dependencies", {})
    for framework, packages in before.items():
        if after.get(framework) != packages:
            problems.append(f"{name}: section {framework} changed")
    for framework, packages in after.items():
        if framework in before:
            continue
        base = before.get(framework.split("/", 1)[0], {})
        for package, entry in packages.items():
            pinned = base.get(package, {}).get("resolved")
            if pinned is not None and entry.get("resolved") != pinned:
                problems.append(f"{name}: {package} resolved {entry.get('resolved')} for {framework},"
                                f" locked at {pinned}")
    return problems


def check(root: pathlib.Path, store: pathlib.Path) -> None:
    problems = []
    restored = 0
    for saved in store.rglob("packages.lock.json"):
        relative = saved.relative_to(store)
        live = root / relative
        try:
            if live.is_file():
                problems += drift(json.loads(saved.read_text(encoding="utf-8")),
                                  json.loads(live.read_text(encoding="utf-8")), str(relative))
            else:
                problems.append(f"{relative}: removed by the publish")
        finally:
            shutil.copy2(saved, live)
            restored += 1
    print(f"lock-guard: restored {restored} lock files")
    if problems:
        print("lock-guard: the publish resolved packages differently from the lock files:", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    if len(sys.argv) != 4 or sys.argv[1] not in ("snapshot", "check"):
        sys.exit(__doc__)
    command, repo, directory = sys.argv[1], pathlib.Path(sys.argv[2]).resolve(), pathlib.Path(sys.argv[3])
    (snapshot if command == "snapshot" else check)(repo, directory)
