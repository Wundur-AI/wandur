#!/usr/bin/env python3
"""Summarise a dotnet-trace Speedscope file: the busiest thread's inclusive and exclusive time per frame.

Usage: python3 bench/speedscope-top.py TRACE.speedscope.json [--thread SUBSTRING] [--match REGEX] [--top N]
"""
import json
import re
import sys
from collections import defaultdict


def main():
    args = sys.argv[1:]
    path = args[0]
    thread = None
    match = r"Wandur|XTerm|Iciclecreek|Avalonia|SkiaSharp|System\.(Text|Linq|Collections|String)"
    top = 40
    if "--thread" in args:
        thread = args[args.index("--thread") + 1]
    if "--match" in args:
        match = args[args.index("--match") + 1]
    if "--top" in args:
        top = int(args[args.index("--top") + 1])
    data = json.load(open(path))
    frames = [f["name"] for f in data["shared"]["frames"]]
    profiles = data["profiles"]
    totals = []
    for p in profiles:
        if p["type"] == "evented":
            continue
        weight = sum(p["weights"])
        totals.append((weight, p))
    if not totals:
        # Evented profiles: compute durations from open and close events.
        for p in profiles:
            totals.append((p["endValue"] - p["startValue"], p))
    totals.sort(key=lambda t: -t[0])
    print("threads:")
    for weight, p in totals[:8]:
        print(f"  {weight:12.1f}  {p['name']}")
    chosen = None
    for weight, p in totals:
        if thread is None or thread in p["name"]:
            chosen = p
            break
    inclusive = defaultdict(float)
    exclusive = defaultdict(float)
    total = 0.0
    if chosen["type"] == "sampled":
        for sample, weight in zip(chosen["samples"], chosen["weights"]):
            total += weight
            seen = set()
            for index in sample:
                name = frames[index]
                if name not in seen:
                    inclusive[name] += weight
                    seen.add(name)
            if sample:
                exclusive[frames[sample[-1]]] += weight
    else:
        stack = []
        last = chosen["startValue"]
        for event in chosen["events"]:
            at = event["at"]
            if stack:
                span = at - last
                total += span
                seen = set()
                for index in stack:
                    if frames[index] not in seen:
                        inclusive[frames[index]] += span
                        seen.add(frames[index])
                exclusive[frames[stack[-1]]] += span
            last = at
            if event["type"] == "O":
                stack.append(event["frame"])
            else:
                if stack:
                    stack.pop()
    pattern = re.compile(match)
    print(f"\nthread {chosen['name']} total {total:.1f}")
    print("\ninclusive:")
    for name, weight in sorted(inclusive.items(), key=lambda kv: -kv[1]):
        if pattern.search(name):
            print(f"  {100 * weight / total:5.1f}%  {name[:170]}")
            top -= 1
            if top <= 0:
                break
    print("\nexclusive (any frame):")
    for name, weight in sorted(exclusive.items(), key=lambda kv: -kv[1])[:25]:
        print(f"  {100 * weight / total:5.1f}%  {name[:170]}")


if __name__ == "__main__":
    main()
