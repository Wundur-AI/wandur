#!/usr/bin/env python3
"""Median per scenario across the app runs in one or more harness result files (.superpowers/perf/logs/app-*.json).

Usage: python3 bench/summarize.py FILE.json [FILE.json ...]
"""
import json
import statistics
import sys
from collections import OrderedDict

columns = [("StartupMs", "Startup ms", 0), ("WorkingSetMb", "Working set MB", 0), ("FootprintMb", "Footprint MB", 0),
           ("GcHeapMb", "GC heap MB", 1), ("CpuPercent", "CPU %", 1), ("AllocMbPerSec", "Alloc MB/s", 1),
           ("CharsPerSec", "Chars/s", 0), ("UiP50", "UI p50 ms", 1), ("UiP95", "UI p95 ms", 1), ("UiMax", "UI max ms", 0)]
groups = OrderedDict()
for path in sys.argv[1:]:
    for run in json.load(open(path)):
        groups.setdefault(run["Scenario"], []).append(run)
print("| Scenario | Runs | " + " | ".join(c[1] for c in columns) + " |")
print("|---|---:|" + "---:|" * len(columns))
for scenario, runs in groups.items():
    cells = [f"{statistics.median(r[key] for r in runs):.{digits}f}" for key, _, digits in columns]
    print(f"| {scenario} | {len(runs)} | " + " | ".join(cells) + " |")
