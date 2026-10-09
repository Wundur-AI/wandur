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

## Lessons from the Rust prototype (October 8, 2026, branch `perf/rust-lessons`)

The Rust rebuild experiment (`wandur-client-rust`, `docs/verdict.md` there) suggested five lessons for this client.
Each was checked here with this harness before anything changed. Same machine as above, the same day for every
comparison; app figures are medians of 3 runs (startup 6), against a build of 27f66d2 (main before this branch).

### New instruments (f9bb1bf)

- The probe turns on Avalonia's own diagnostic meters (`Avalonia.Diagnostics.Diagnostic.IsEnabled`, only when the probe
  is on) and records, per second, UI render passes, compositor frames, layout passes, the time spent in the first two,
  the active dispatcher timers and what holds the keyboard. The harness prints them in its notes and `summarize.py`
  adds a Frames/s column.
- `WANDUR_PERF_FOCUS=1` puts the keyboard in the command box (as when someone plays); `WANDUR_PERF_NO_TICK=1` turns off
  the latency ticker.
- The loopback MUD can write every N ms (`--write-ms`, default 100). `steady-1m` (one session, 1 MB/s) and
  `steady-4x250k` (four sessions, 250 KB/s each) write every 2 ms, like a busy world: the bursty scenarios send ten
  bursts a second, which hides any redraw cap.
- `WANDUR_BENCH_DATA_ROOT` moves the throwaway data folders.

### 1. Idle redraws and the caret: no change

- Frames when idle: 0 per second with no session, 0.5 with an idle session (the prompt every 2 s), 2.5 with the
  command box focused (its caret blinks twice a second). The transcript's own cursor blink is off
  (`MudTerminalSurface`), and there are no running animations. The Rust prototype drew about ten frames a second for its
  caret because egui redraws the whole window; Avalonia redraws only the caret's rectangle.
- The caret costs nothing measurable: with the JIT's background work removed (`DOTNET_TieredCompilation=0`, see below),
  an idle session used 2.06% and 2.08% of a core with the caret blinking, 2.17% and 2.02% with it stopped, and 1.93%
  unfocused. So the caret was left alone.
- What the idle CPU really is: a Time Profiler capture (`xctrace`) of an idle session 8 to 18 s after launch spent 341
  of 585 ms of running time in the runtime's tiered compilation worker (recompiling warm methods at full optimization),
  and still 353 of 597 ms at 50 to 62 s. Idle session CPU: 4.5 to 8% in the harness window, 3.5 to 3.9% after 50 s,
  2.6% after two minutes, and 1.7% with tiered compilation off. The 7.5% "idle" figure quoted against Rust's 1.8% is
  mostly start-up warm-up, not drawing.
- Tried and not kept: turning off tiered PGO (`DOTNET_TieredPGO=0`) cuts the warm-up (Release build, medians of 3:
  idle session 7.4 to 4.6%, flood-1m-nochat 28.9 to 26.1%, multi-4-nochat 36.6 to 30.2%, steady-1m 33.6 to 30.7%;
  the ReadyToRun publish behaves the same), but after 90 s of a steady 1 MB/s stream it made no difference (27.5 and
  28.6% with PGO, 30.3 and 26.7% without). It changes how the shipped runtime optimizes for a gain that lasts a minute
  or two, so it is left as an option (`<TieredPGO>false</TieredPGO>` in `Wandur.Desktop.csproj`), not a default.
  Turning tiered compilation off altogether brings idle to 2% but costs 50 to 130 ms at startup and gives mixed
  flood results.

### 2. Output redraw cadence: already capped; one per-flush cost removed (5cf5cb3)

- Each session drains its output on a 60 ms UI timer (`WorkspaceController`), so a visible transcript redraws at most
  about 16 times a second whatever the server sends. Measured: 10 frames a second under the bursty floods, 14 under
  a steady 1 MB/s stream, 12 to 18 with four sessions. That is already below the Rust prototype's cap of 30, so no cap
  was added.
- Waking only when output is due (the other half of the Rust lesson) was measured with eight idle sessions and the JIT
  noise removed: 60 ms timers 2.78, 2.51, 2.51%; 1 s timers 2.08, 2.27, 1.66%. About half a point for eight sessions;
  the same timer also flushes history, saves the map and ticks scripts, so it was not changed.
- A trace of the steady 1 MB/s stream (`dotnet-trace`, 8 s) showed the cost per flush was not drawing: the UI thread
  spent 17% of its time in `FlushOutput`, and about 730 ms of the 8 s went to the agent's view of the output. Every
  chunk rebuilt the agent's last 60 lines, split them and ran each through the channel rules, with no agent run going.
  Now a chunk only marks that text stale unless a run is going, and the next observation rebuilds it once.

| Scenario (27f66d2 against the branch) | CPU % | Alloc MB/s | UI p95 ms | Chars/s |
|---|---:|---:|---:|---:|
| steady-1m | 33.7 / 32.0 | 86.9 / 74.1 | 9.0 / 7.6 | 1.00 M / 1.01 M |
| steady-4x250k | 70.0 / 64.2 | 127.6 / 81.2 | 3.5 / 1.4 | 1.00 M / 1.00 M |

  Risk: an agent run that starts sees the text rebuilt at its first observation, which is what it read before; while a
  run is going the text and its revision are kept current per chunk as before. Agent tests pass unchanged, including
  the private-input ones (a privacy change still clears the stale text).

### 3. Terminal text layout reuse: already done by the control

- `Iciclecreek.Avalonia.Terminal` keeps each row's shaped runs on the row (`BufferLine.Cache`, read in
  `RenderNormalLine`) and drops them only when a cell changes, so rows that scroll are not laid out again. That is the
  Rust prototype's glyph mesh lesson, already in place.
- In the steady 1 MB/s trace, the whole terminal `Render` was 0.2% of the UI thread's time and glyph run creation
  another 0.2%; the compositor thread was about 2.6% of a core. Nothing to fix here; the control's optional
  `UseSkiaRenderer` path was not tried for that reason. The UI thread's flood work is text processing (the session's
  own `AnsiTerminal`, channel rules with regex timeouts, the room observer) and garbage collection.

### 4. Directory search normalized once (3aca7ed); thumbnail budget not needed

- `WorldCatalog.Search` normalized four strings per world with a regex on every query. The normalized text is now
  kept per catalog snapshot and rebuilt when the world list changes. Directory filter (500 worlds, the view model
  included): 2.32 ms and 772 KB to 0.61 ms and 81 KB per query; `WorldCatalog.Search` alone is 0.15 ms (new micro
  row; Rust 0.023 ms). Sort is unchanged (0.6 ms). A test covers a refreshed catalog.
- A byte budget for thumbnails would not change anything here: directory rows own and dispose their bitmaps and at most
  five are alive while paging through 300 (the Rust prototype's problem was a 96 MB texture cache), and saved-world
  tiles are 80 by 60 pixels, about 19 KB each. Row-by-row PNG decoding is not possible with Skia (see above).

### Full harness, same day (27f66d2 / branch)

| Scenario | Startup ms | Working set MB | Footprint MB | GC heap MB | CPU % | Alloc MB/s | UI p95 ms | Frames/s (branch) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| startup | 882 / 889 | 210 / 209 | 244 / 244 | 26.9 / 27.0 | 2.5 / 2.7 | 0.0 / 0.1 | 0.1 / 0.1 | 0 |
| session-idle | 1194 / 1162 | 168 / 175 | 263 / 243 | 43.7 / 43.7 | 7.0 / 7.2 | 0.1 / 0.1 | 0.1 / 0.1 | 0.5 |
| flood-100k | 1101 / 1065 | 274 / 263 | 496 / 495 | 82.3 / 82.2 | 36.6 / 36.7 | 23.5 / 23.0 | 3.7 / 5.3 | 10.0 |
| flood-100k-nochat | 1067 / 1101 | 201 / 204 | 482 / 483 | 60.0 / 60.0 | 24.7 / 23.6 | 11.0 / 10.4 | 0.1 / 0.1 | 9.9 |
| flood-1m-nochat | 1225 / 1140 | 213 / 223 | 466 / 471 | 55.3 / 56.6 | 28.9 / 28.3 | 69.1 / 64.9 | 13.0 / 10.2 | 9.9 |
| multi-4 | 1051 / 1102 | 251 / 258 | 532 / 542 | 134.0 / 128.9 | 41.8 / 43.1 | 22.7 / 21.3 | 0.5 / 1.1 | 11.9 |
| multi-4-nochat | 1172 / 1117 | 241 / 253 | 523 / 522 | 110.3 / 114.3 | 35.6 / 37.1 | 20.6 / 19.4 | 0.2 / 0.2 | 11.9 |
| multi-8-idle | 1116 / 1162 | 187 / 180 | 401 / 404 | 65.2 / 65.5 | 7.4 / 7.9 | 0.6 / 0.6 | 0.1 / 0.1 | 2.5 |
| directory | 1137 / 1125 | 242 / 226 | 309 / 301 | 37.2 / 37.2 | 20.7 / 23.8 | 0.0 / 0.1 | 0.1 / 0.1 | 0 |
| steady-1m | 1130 / 1118 | 245 / 231 | 457 / 471 | 54.1 / 56.8 | 33.7 / 32.0 | 86.9 / 74.1 | 9.0 / 7.6 | 13.9 |
| steady-4x250k | 1212 / 1164 | 291 / 287 | 527 / 530 | 107.1 / 108.5 | 70.0 / 64.2 | 127.6 / 81.2 | 3.5 / 1.4 | 17.9 |

Every flood kept up in both builds. The bursty scenarios move within run-to-run noise (about 1 to 3 points of CPU);
the steady streams show the agent change. Memory is unchanged.

### What is left (after this round)

- Under a steady stream the C# client uses about twice the Rust prototype's CPU with one session (32% against 15.7%)
  and three times with four (64% against 19.1%). The difference is not drawing: it is per-line text processing on the
  UI thread (the session's `AnsiTerminal` building runs and strings for every line, channel classification with regex
  timeouts, the room observer) and the garbage it makes. Moving that work off the UI thread, or making the channel
  rules cheaper per line (the trace shows `RegexRunner.InitializeTimeout` and the clock reads it causes as a visible
  share), is the next lever.
- Idle CPU is start-up JIT warm-up for a minute or two; tiered PGO off is the owner's call (above).

## Output drawn as it arrives (perf/rust-lessons)

Side by side against the bench ticker (`wandur-bench mud-server --tick-ms 10` in the Rust prototype), the C# client
printed a stream in clumps of about six lines while the Rust prototype scrolled smoothly, and ran a little behind.
The cause was the 60 ms output timer: every line waited for the next tick, up to 60 ms, about 30 on average.

Change: queuing output now asks the UI thread for a flush. The first flush after a quiet spell runs on the next UI
pass; while output keeps coming, flushes stay at least 33 ms apart (`OutputFlushGap`, about 30 a second). The 60 ms
timer keeps its other work (history, map save, script ticks) and only flushes as a fallback when no requested flush is
pending and none ran within the gap.

| Scenario (3 runs, medians) | CPU % before / after | Alloc MB/s | UI p95 ms | Frames/s | Chars/s |
|---|---:|---:|---:|---:|---:|
| steady-1m | 32.0 / 36.4 | 74.1 / 87.8 | 7.6 / 3.1 | ~14 / 30.6 | 1.0 M / 1.0 M |
| steady-4x250k | 64.2 / 68.0 | 81.2 / 82.5 | 1.4 / 1.4 | ~14 / 32.7 | 1.0 M / 1.0 M |
| flood-1m-nochat (bursty) | 28.3 / 29.6 (first version) | | | | |

Cost: about 4 points of CPU during a sustained flood, for twice the frame rate and output that appears within a frame
instead of up to 60 ms late. A first version without the timer fallback rule drew 46 frames a second (both paths
flushing) at 42.9% CPU; the rule fixed that.

## UI review measurements (October 9, 2026, branch `ui/polish`)

New instruments: `micro --only switch` alternates the active session between two sessions that each took in about
300 KB of chatty output (so the Channels panel holds a full history) in a 1440 by 900 headless window, and times the
switch, its queued work and a layout pass. The probe takes `WANDUR_PERF_SKIN` and `WANDUR_PERF_THEME` to measure a
skin and palette other than the throwaway data folder's default (Fleet and Hull).

| Change | Measure | Before | After |
|---|---|---:|---:|
| Channels keep each session's history (item 3) | switch, ms | 25.2 | 54.0 |
| Session panels kept, hidden, per session (item 17) | switch, ms (alloc MB) | 54.0 (12.1) | 16.1 (1.9) |
| same | multi-4 CPU % / UI p95 ms | 35.9 / 1.5 | 30.7 / 0.2 |
| Virtualized channel list (item 18, not kept) | switch, ms | 54.0 | 29.0 |
| same | flood-100k CPU % / UI p95 ms / layouts per s | 26.3 / 0.2 / 12 | 34.0 / 2.7 / 26 |
| Button fades, System skin (item 19) | idle frames per s, session-idle / startup | 0.5 / 0.0 | 0.5 / 0.0 |

flood-100k and steady-1m otherwise stayed within the run-to-run spread on this machine (flood-100k single runs ranged
from 24 to 40 percent CPU in one set). The terminal library repaints on its own shared throttle,
`TerminalRenderThrottle.TargetFrameRate`, 30 frames a second by default, alongside the 33 ms output flush.
