# Performance harness and results

How the client's speed and memory are measured, the exact commands to repeat it, and what the measurements were.
Everything runs locally: a loopback stand-in MUD on 127.0.0.1, a fake directory on 127.0.0.1 built from the
repository's fixture, and generated artwork. Nothing connects to a real world or to wandur.net.

## Pieces

- `bench/Wandur.Bench`: the harness, a console project that is not in `Wandur.sln` and is never packaged.
  - `micro`: in-process measurements of the client's own classes (ANSI parsing, transcript display, thumbnails,
    directory filter and sort, several sessions in a headless window). UI pieces use Avalonia's headless platform
    with Skia, as the Desktop tests do. Thumbnails run in a child process each, so peak resident memory is theirs.
  - `app`: launches the built client (Release) once per run with its probe on, a throwaway data folder under
    `.superpowers/perf/data-<scenario>` (deleted before the first run of each scenario, so run 0 is a cold start),
    and `WANDUR_DIRECTORY_URL` pointing at a closed loopback port or the fake directory.
  - `mud-server`, `directory-server`: the loopback servers on their own, for manual sessions.
- `src/Wandur.Desktop/PerfProbe.cs`: the probe. It does nothing unless `WANDUR_PERF_PROBE` names an output file. Then
  it records the time from process start to the first rendered frame of the main window plus a pass of the
  dispatcher (startup), optionally opens loopback sessions or the directory, samples memory, CPU, allocation rate,
  GC counts, transcript throughput and UI thread latency (a job posted at input priority every 50 ms), lists the
  loaded assemblies, and closes the app.
- `bench/summarize.py`: medians per scenario from `app` result files. `bench/speedscope-top.py`: the busiest frames of a
  `dotnet-trace` Speedscope file.
- `.config/dotnet-tools.json`: `dotnet-trace`, `dotnet-counters` and `dotnet-stack` as local tools (`dotnet tool restore`).

## Commands

From the repository root:

```sh
dotnet build Wandur.sln -c Release
dotnet build bench/Wandur.Bench/Wandur.Bench.csproj -c Release
# In-process measurements (about 4 minutes); results in .superpowers/perf/logs/micro-<label>.md and .json
dotnet bench/Wandur.Bench/bin/Release/net10.0/Wandur.Bench.dll micro --label mine
# The real app, 3 runs per scenario (about 15 minutes; a window opens and closes for each run)
dotnet bench/Wandur.Bench/bin/Release/net10.0/Wandur.Bench.dll app --runs 3 --label mine
dotnet bench/Wandur.Bench/bin/Release/net10.0/Wandur.Bench.dll app startup --runs 6 --label mine-startup
python3 bench/summarize.py .superpowers/perf/logs/app-mine.json .superpowers/perf/logs/app-mine-startup.json
# A CPU trace of the app under load: start a loopback MUD, start the app with the probe, attach dotnet-trace
dotnet tool restore
dotnet bench/Wandur.Bench/bin/Release/net10.0/Wandur.Bench.dll mud-server --port 4400 --rate 100000 &
WANDUR_PERF_PROBE=$PWD/.superpowers/perf/logs/probe-trace.jsonl WANDUR_PERF_SCENARIO=sessions WANDUR_PERF_SECONDS=25 \
  WANDUR_PERF_HOST=127.0.0.1:4400 WANDUR_DIRECTORY_URL=http://127.0.0.1:9/ \
  src/Wandur.Desktop/bin/Release/net10.0/Wandur --data-dir .superpowers/perf/data-trace &
dotnet dotnet-trace collect -p <pid> --duration 00:00:00:08 --format Speedscope -o .superpowers/perf/traces/flood.nettrace
python3 bench/speedscope-top.py .superpowers/perf/traces/flood.speedscope.json --thread <busiest thread id>
```

Scenarios (`app`): `startup` (no session, 5 s idle), `session-idle` (one session, a prompt every 2 s),
`flood-100k` and `flood-100k-nochat` (one session at 100 KB/s, with and without channel lines), `flood-1m-nochat`
(1 MB/s), `multi-4` and `multi-4-nochat` (four sessions at 50 KB/s each, three of them inactive tabs),
`multi-8-idle` (eight idle sessions), `directory` (300 listings, every artwork a 4000 by 3000 JPEG or PNG; the probe
scrolls the results to the end a page at a time and back).

Columns: Startup is process start to first usable frame. Working set is the process resident size; Footprint is
the macOS physical footprint (`footprint`, what Activity Monitor shows), read once at the end. GC heap is after a full
collection. CPU % is of one core over the measured 10 s (5 s for startup and directory). Chars/s is transcript
input actually taken in. UI p50/p95/max is how long an input-priority job waited for the UI thread.

Machine for the numbers below: Apple M3 Pro (11 cores), 18 GB, macOS 26.5.1, .NET SDK 10.0.101, Release build.

## Baseline (main at 69b6a73, before any change)

App, median of runs:

| Scenario | Runs | Startup ms | Working set MB | Footprint MB | GC heap MB | CPU % | Alloc MB/s | Chars/s | UI p50 ms | UI p95 ms | UI max ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| startup | 9 | 1077 | 211 | 226 | 26.9 | 2.6 | 0.0 | 0 | 0.1 | 0.1 | 16 |
| session-idle | 3 | 1176 | 176 | 261 | 43.8 | 7.1 | 0.1 | 14 | 0.1 | 0.1 | 7 |
| flood-100k | 3 | 1173 | 263 | 487 | 81.1 | 101.9 | 305.3 | 67070 | 506.3 | 3906.4 | 4556 |
| flood-100k-nochat | 3 | 1229 | 233 | 475 | 59.4 | 32.1 | 111.7 | 99651 | 0.0 | 6.7 | 32 |
| flood-1m-nochat | 3 | 1128 | 244 | 474 | 56.8 | 36.1 | 266.5 | 1000952 | 0.0 | 17.7 | 31 |
| multi-4 | 3 | 1184 | 327 | 536 | 132.0 | 96.7 | 477.8 | 199554 | 11.1 | 108.4 | 258 |
| multi-4-nochat | 3 | 1194 | 311 | 532 | 127.3 | 49.3 | 390.1 | 201020 | 0.0 | 7.0 | 38 |
| multi-8-idle | 3 | 1240 | 205 | 407 | 65.6 | 8.1 | 0.7 | 115 | 0.1 | 0.1 | 37 |
| directory | 3 | 1290 | 388 | 390 | 66.1 | 22.4 | 0.0 | 0 | 0.1 | 0.1 | 0 |

In-process (`micro`):

| Measurement | Unit | ms per unit | managed KB per unit | Note |
|---|---|---:|---:|---|
| Thumbnail 4000x3000 JPEG to 800x320 cover | image | 23.145 | 889.0 | peak RSS rise 8 MB; source 883 KB |
| Thumbnail 4000x3000 PNG to 800x320 cover | image | 91.406 | 2956.7 | peak RSS rise 35 MB; source 2365 KB |
| Thumbnail 4000x3000 JPEG to 80x60 | image | 18.764 | 1035.3 | peak RSS rise 7 MB; source 883 KB |
| Thumbnail 800x320 JPEG to 800x320 cover | image | 0.596 | 54.6 | peak RSS rise 2 MB; source 40 KB |
| AnsiTerminal.Append, 100k lines into 2,000 line scrollback | run | 113.078 | 180356.0 | 100,000 lines, 8.2 M chars in 4 KB chunks |
| AnsiTerminal.PlainText, full 2,000 line scrollback | call | 0.158 | 514.6 |  |
| AnsiTerminal.Lines after one append (full scrollback) | append+read | 0.002 | 18.9 |  |
| AnsiTerminal.Append of one 4 KB chunk (full scrollback) | chunk | 0.057 | 85.1 |  |
| TranscriptDisplay append (AnsiTerminal + xterm), 100k lines | run | 167.320 | 219034.4 |  |
| TranscriptDisplay.PlainText, full scrollback | call | 1.485 | 2202.7 |  |
| TranscriptDisplay.TopVisibleText | call | 0.001 | 0.9 |  |
| Directory filter, 500 worlds, search text changes | query | 2.183 | 772.0 |  |
| Directory sort, 500 worlds, cycling 6 sorts | query | 0.555 | 75.0 |  |
| 1 session(s) at 200 KB/s each, headless window | MB of output | 13350.430 | 4058623.7 | connected 1, 0.08 MB/s taken in, CPU 102% of one core, 303.7 MB/s allocated |
| 1 session(s) at 200 KB/s each, no channel lines, headless window | MB of output | 1833.988 | 675318.9 | connected 1, 0.19 MB/s taken in, CPU 35% of one core, 126.3 MB/s allocated |
| 4 session(s) at 100 KB/s each, no channel lines, headless window | MB of output | 1060.963 | 1125256.6 | connected 4, 0.38 MB/s taken in, CPU 40% of one core, 418.2 MB/s allocated |

Two corrections to this first baseline run, remeasured on the same code once the harness was fixed: the thumbnail
peak RSS figures above include drawing the test picture in the same process (the corrected baseline is a 69 MB rise
for either 4000x3000 JPEG case and 84 MB for the PNG), and retained memory must be read after a compacting
collection (the corrected baseline is 1,175 KB for an AnsiTerminal with 2,000 lines and 6,288 KB for a
TranscriptDisplay, of which the xterm buffer is about 5.1 MB). The harness now does both.

Loaded at the first usable frame (no Jint, Acornima, ONNX Runtime or Tokenizers): Avalonia (Base, Controls,
ColorPicker, Desktop, Dialogs, Fonts.Inter, HarfBuzz, Markup, Markup.Xaml, Metal, MicroCom, Native, OpenGL, Skia,
Themes.Fluent, Vulkan), AvaloniaEdit, CommunityToolkit.Mvvm, Dock (7 assemblies), HarfBuzzSharp,
Iciclecreek.Avalonia.Terminal, XTerm.NET, Microsoft.Data.Sqlite and SQLitePCLRaw, Microsoft.Extensions.DependencyInjection,
SkiaSharp, Wandur, Wandur.Core, Wandur.Models, Wandur.Protocol.

## Changes and their effect

Each change was measured before and after with the harness above; app figures are medians of 3 runs (startup of 6
to 10). "Before" is the state just before the change unless it says baseline.

| Change (commit) | Measure | Before | After | Rank |
|---|---|---:|---:|---|
| Transcript emptiness checks no longer build the transcript (b7f9c2e) | allocation, 1 session at 100 KB/s (no channel lines) | 111.7 MB/s | 23.4 MB/s | high |
| | allocation, 4 sessions at 50 KB/s, 3 inactive | 390 MB/s | 45.5 MB/s | |
| | CPU / UI p95, 4 sessions | 49.3% / 7.0 ms | 38.7% / 0.7 ms | |
| Thumbnails decode near their size (9c9ed69) | 4000x3000 JPEG to an 800 px row plate | 23.4 ms, +69 MB peak | 16.2 ms, +15 MB peak | medium |
| | 4000x3000 JPEG to a saved-world tile | 18.6 ms, +69 MB peak | 9.2 ms, +8 MB peak | |
| | 4000x3000 PNG (Skia cannot scale PNG) | 88 ms | 88 ms (unchanged) | none |
| | directory scenario footprint / working set | 390 / 388 MB | 300 / 212 MB | |
| ReadyToRun in runtime publishes (f2721fe) | packaged app, process start to usable window | 1,016 ms (same publish without it) | 683 ms | high |
| Optional styles behind first use (not committed) | startup, median of 9 | 1,026 ms | 1,025 ms | none |
| ANSI parser allocations (29545e5) | 100,000 lines through AnsiTerminal | 113 ms, 180 MB allocated | 56 ms, 43 MB | medium |
| | 2,000 lines retained | 1,175 KB | 914 KB | |
| | allocation, 1 session at 100 KB/s | 23.4 MB/s | 11.1 MB/s | |
| Session transcript model bounded to 500 lines (ee2464f) | retained per full session | 914 KB | 256 KB | low |
| Directory list virtualization (584cd9d, test only) | cards alive while paging through 300 | 5 | 5 (already virtualized) | none |
| Channels panel updated a row at a time (be5ebd6) | 1 session at 100 KB/s with channel lines: CPU | 101.9% (baseline) | 35.7% | high |
| | UI p95 / max | 3,906 ms / 4,556 ms | 3.9 ms / 19 ms | |
| | characters taken in per second (server sends 100,000) | 67,070 | 100,437 | |

## After all changes (be5ebd6)

App, median of runs (startup here is the plain Release build, without ReadyToRun; see the table above for packages):

| Scenario | Runs | Startup ms | Working set MB | Footprint MB | GC heap MB | CPU % | Alloc MB/s | Chars/s | UI p50 ms | UI p95 ms | UI max ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| startup | 9 | 1149 | 202 | 226 | 26.9 | 2.7 | 0.0 | 0 | 0.1 | 0.1 | 17 |
| session-idle | 3 | 1293 | 174 | 265 | 43.7 | 6.5 | 0.1 | 14 | 0.1 | 0.1 | 30 |
| flood-100k | 3 | 1213 | 270 | 498 | 81.8 | 35.7 | 23.1 | 100437 | 0.1 | 3.9 | 19 |
| flood-100k-nochat | 3 | 1197 | 266 | 484 | 60.1 | 24.9 | 11.1 | 100594 | 0.1 | 0.1 | 5 |
| flood-1m-nochat | 3 | 1234 | 245 | 472 | 56.0 | 29.6 | 69.8 | 1001576 | 0.0 | 12.8 | 26 |
| multi-4 | 3 | 1158 | 262 | 533 | 134.0 | 42.0 | 22.6 | 200603 | 0.0 | 0.6 | 6 |
| multi-4-nochat | 3 | 1225 | 277 | 520 | 114.4 | 36.3 | 20.6 | 200485 | 0.0 | 0.2 | 9 |
| multi-8-idle | 3 | 1213 | 189 | 406 | 65.5 | 8.3 | 0.6 | 112 | 0.1 | 0.1 | 20 |
| directory | 3 | 1197 | 255 | 297 | 37.1 | 19.3 | 0.0 | 0 | 0.1 | 0.1 | 0 |

In-process (`micro`):

| Measurement | Unit | ms per unit | managed KB per unit | Note |
|---|---|---:|---:|---|
| Thumbnail 4000x3000 JPEG to 800x320 cover | image | 15.843 | 892.4 | peak RSS rise 15 MB; source 883 KB |
| Thumbnail 4000x3000 PNG to 800x320 cover | image | 89.405 | 2374.8 | peak RSS rise 68 MB; source 2365 KB |
| Thumbnail 4000x3000 JPEG to 80x60 | image | 9.288 | 892.3 | peak RSS rise 8 MB; source 883 KB |
| Thumbnail 800x320 JPEG to 800x320 cover | image | 0.570 | 48.5 | peak RSS rise 3 MB; source 40 KB |
| AnsiTerminal.Append, 100k lines into 2,000 line scrollback | run | 54.910 | 42964.0 | 100,000 lines, 8.2 M chars in 4 KB chunks |
| AnsiTerminal.PlainText, full 2,000 line scrollback | call | 0.321 | 514.6 |  |
| AnsiTerminal.Lines after one append (full scrollback) | append+read | 0.001 | 16.4 |  |
| AnsiTerminal.Append of one 4 KB chunk (full scrollback) | chunk | 0.023 | 19.2 |  |
| AnsiTerminal.HasText (full scrollback) | call | 0.000 | 0.0 |  |
| AnsiTerminal retained, 2,000 lines of generated output | instance | 0.000 | 913.8 | retained, not allocated |
| AnsiTerminal(500) retained, the session transcript model since the bound | instance | 0.000 | 256.0 | retained, not allocated |
| TranscriptDisplay append (AnsiTerminal + xterm), 100k lines | run | 101.061 | 81636.7 |  |
| TranscriptDisplay.PlainText, full scrollback | call | 1.410 | 2202.7 |  |
| TranscriptDisplay.TopVisibleText | call | 0.001 | 0.9 |  |
| TranscriptDisplay.HasText, full scrollback | call | 0.002 | 1.6 |  |
| TranscriptDisplay retained (its AnsiTerminal and xterm buffer), 2,000 lines | instance | 0.000 | 6026.3 | retained, not allocated |
| Directory filter, 500 worlds, search text changes | query | 2.256 | 772.0 |  |
| Directory sort, 500 worlds, cycling 6 sorts | query | 0.573 | 75.0 |  |
| 1 session(s) at 200 KB/s each, headless window | MB of output | 2874.411 | 213075.9 | connected 1, 0.19 MB/s taken in, CPU 55% of one core, 39.5 MB/s allocated |
| 1 session(s) at 200 KB/s each, no channel lines, headless window | MB of output | 1611.206 | 91787.5 | connected 1, 0.19 MB/s taken in, CPU 31% of one core, 17.0 MB/s allocated |
| 4 session(s) at 100 KB/s each, no channel lines, headless window | MB of output | 842.204 | 86564.8 | connected 4, 0.38 MB/s taken in, CPU 32% of one core, 32.2 MB/s allocated |

## What is left

- Idle CPU with a session open is 6 to 8% of a core against about 2.6% without one. `dotnet-trace` shows no managed
  work over 20 ms in 8 s; a native sample shows the display link threads active, which points at rendering (caret or
  animation), not at the client's code. Not pursued.
- The Channels panel is a plain StackPanel of up to 500 TextBlocks; with steady chatter, arranging them is now the
  largest item on the UI thread (about 7%). A virtualized list would remove it.
- Footprint under load (470 to 530 MB) is mostly native: Skia, Metal and the runtime. The managed heap is 56 to
  134 MB of it. Each session's xterm buffer is about 5.1 MB at 2,000 lines of scrollback.
- PNG artwork is still decoded whole; a bounded disk cache of resized thumbnails would avoid repeat decodes if large
  PNG banners become common. The site serves 400 px row art, which decodes in under a millisecond.
- `AnsiTerminal.PlainText` (export path only) went from 0.15 to 0.32 ms per call after the parser change; the cause
  was not found and it is not on any routine path.
