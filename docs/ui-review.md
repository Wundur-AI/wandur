# UI review, October 2026

A designer's pass over the client as the owner uses it: System skin, Linen palette, macOS, Workspace on the left,
transcript in the centre, Map over Channels on the right. Dark (System + Midnight) and the drawn skins (Fleet + Hull,
Armored + Hull) were captured for comparison. Nothing here is implemented; this is a proposal list for the owner to
choose from.

## How the pictures were made

Headless Avalonia captures at 1440 by 900, render scale 2, of the real window playing The Lantern Road over a loopback
connection (the fixture behind the help screenshots). The pictures are local, in
`.superpowers/ui/before/<Skin>-<Palette>/` (gitignored). The capture class is parked uncommitted at
`.superpowers/ui/UiReviewCaptureTests.cs.txt`; copy it into `tests/Wandur.Desktop.Tests/` as a `.cs` file and run

    WANDUR_UI_CAPTURE_DIR="$PWD/.superpowers/ui/before" dotnet test tests/Wandur.Desktop.Tests -c Release \
      --filter "FullyQualifiedName~UiReviewCaptureTests"

| File | Scene |
|---|---|
| 01-first-launch | No saved worlds, directory fixture |
| 02-find-a-mud | Find a MUD results with artwork |
| 03-world-details | A world's details page |
| 04-session | Connected session: ANSI transcript, vitals, map, channels |
| 05-composer | Composer with typed text |
| 06-second-session | A second world opened (no map data) |
| 07-two-sessions-activity | Back on the first world while the second keeps talking |
| 08-workspace-panel | Workspace panel close up |
| 09-map, 10-channels | Map and Channels close up |
| 11, 12 | Settings: General, Appearance |
| 13 to 16 | World editor: Login, Macros (empty and with two), Connection |
| 17-history | Session history window with a search |

Effort: S under half a day, M one to two days, L more. Risk is the chance of regressing behaviour, other skins or
performance.

## Status after the build (October 9, 2026)

The owner chose items 1 to 15, 17, 18, 19 and 23 to build, 16 not to build, 20 to investigate only, and 21 and 22 to
hold for separate design conversations. One commit per item on `ui/polish`:

| Item | Commit | Result |
|---|---|---|
| 1 Transcript gutter (System) | fd37f1d | Built |
| 2 Palette accent for Fluent | 0b5056a | Built |
| 3 Channels keep each session's conversation | d0c9e05 | Built |
| 4 Readable channel rows | f6308d1 | Built |
| 5 Localized dock tooltips | 5168d6f | Built |
| 6 No standing hint lines | df059f4 | Built |
| 7 Quiet dock headers (System) | ebf89ea | Built |
| 8 Empty Map and Channels | 40dc112 | Built |
| 9 Workspace session rows | 36d32a5 | Built |
| 10 System toolbar glyphs | d1e0269 | Built |
| 11 Settings measure | 77f6fc5 | Built |
| 12 History window | 23b66ee | Built |
| 13 World editor details | b44e5e8 | Built |
| 14 Find a MUD title and status | 0aaed38 | Built |
| 15 Map status line and north marker | 3edd497 | Built |
| 17 Keep session panels | 9bdb418 | Built, measured |
| 18 Virtualized channel list | none | Built, measured, not kept (below) |
| 19 Button feedback (System) | 4fcff7d | Built, measured |
| 23 Composer | b98906b | Built (quiet Send is System only) |
| 16, 21, 22 | none | Not built, by the owner's choice |
| 20 Transcript line height | none | Investigated (below) |

Measurements, with the harness in `docs/perf.md` on the same machine and day (app figures are medians of 3 runs):

- Item 17. Item 3 made a session's Channels panel keep its history, so rebuilding the panel on every switch became the
  cost: the new `micro --only switch` (two sessions with a full channel history, 1440 by 900) went from 25.2 ms per
  switch before item 3 to 54 ms after it. Keeping each session's views, hidden, while another session is in front
  brings it to 16 ms (1.9 MB allocated per switch instead of 12.1). multi-4 CPU 35.9 to 30.7 percent, UI p95 1.5 to
  0.2 ms; flood-100k and steady-1m are within this machine's run-to-run spread.
- Item 18. A virtualized channel list (only the rows in view exist) cut the switch to 29 ms on its own, but under
  steady chatter it doubled layout passes (12 to 26 a second) and raised flood-100k CPU from about 26 to 34 percent and
  UI p95 from 0.2 to 2.7 ms, because every arrival re-measures the estimated panel while it follows the newest
  message. Item 17's hidden panels made the switch faster than virtualization did, so item 18 was left out. The patch
  is kept at `.superpowers/ui/item18-rejected/virtualized-channels.patch` for a later look.
- Item 19. With the System skin and Linen (the probe's new `WANDUR_PERF_SKIN` and `WANDUR_PERF_THEME`), idle frames are
  unchanged: 0.5 a second with an idle session and 0.0 at startup, before and after. The transitions run once per
  hover or press and never continuously.
- Item 16 stays as it is: output still flushes at most every 33 ms. Worth knowing for the stutter question: the
  terminal library also repaints on a shared throttle that defaults to 30 frames a second
  (`TerminalRenderThrottle.TargetFrameRate`), so raising the flush rate alone would not have made scrolling smoother.

## Prioritised proposals

### First: correctness and the "cramped" feeling

**1. Give the transcript room to breathe (System and Armored).**
What: `TerminalView` sets the transcript margin to `4,2,2,2` for every skin but Fleet, which gets `12,12,8,8`
(`TerminalView.cs`, line 521). Use about 16 left, 12 top, 8 right and bottom for System
too. Why: in System the first line touches the toolbar and the text hugs the left splitter; this, more than the
removed border, is why the centre reads as cramped. Screens: 04, 06. Effort S. Risk low (NAWS reports about
3 fewer columns; transcript tail tests may need their offsets checked).

**2. Stop Fluent's blue leaking into themed screens.**
What: `ThemeService` never sets `SystemAccentColor` (and its Light1 to 3 and Dark1 to 3 steps), so Fluent's default
blue shows in the selected item of the Settings and World editor section lists, the History results list, checked
check boxes and the History tab underline. Derive those keys from the palette accent. Why: on Linen the light blue
selection is the loudest colour on screen and clashes with the teal accent; it looks unfinished. Screens: 11, 12, 13,
15, 17. Effort S. Risk low (needs the contrast tests over all presets, and a check of the drawn skins).

**3. The Channels panel forgets a session's messages when you switch sessions (bug).**
What: the Channels tool is an `ActiveSessionView` that builds a new `ChannelsViewModel` on every session change, and
`WorkspaceController` keeps no channel history, so coming back to a world shows an empty panel. Keep a bounded
per-session message buffer on the controller (or cache the view model per controller). Why: 04 shows five channels
with messages; 07, the same world after visiting another, shows "Channel messages appear here". Effort M. Risk low.

**4. Make channel messages readable.**
What: rows are monospace 14 with no gap between messages and no hanging indent; the top row is half hidden under
the tab strip and the last row is cut by the reply bar. Use the UI font at 13, 4 to 6 px between messages, a hanging
indent after the time, and padding top and bottom. Why: chat is prose, not terminal output; wrapped monospace in a
narrow column is the densest text in the app. Screens: 04, 10. Effort S. Risk low.

**5. Localise the dock header tooltips.**
What: `App.axaml` hard-codes "Drag to move or undock this panel", "Close panel · reopen from the View menu" and the
automation name "Close panel" in English. Why: every UI string should go through the tables; these slip past the
localisation test because they live in XAML. Effort S. Risk low.

### Second: clutter and hierarchy

**6. Drop the permanent hint text.**
What: the status bar always shows "Ctrl+Tab switch workspace views · Drag panel headers to arrange", and the session
footer always shows "↑ ↓ command history · Enter send". Move them to tooltips, the Help menu or a first-run tip that
goes away. Why: they repeat on every screen, add a second line of small grey text to read past, and make the chrome
look busy. Screens: 01, 04. Effort S. Risk low (help screenshot tests assert the footer hint in the private-input
case, which keeps its own text).

**7. Quieter dock headers.**
What: each panel header carries a grip, a collapse chevron, a pin and a close button at full ink, with a semibold
title. Show the three buttons in the muted colour and at full ink only on header hover, and make the title regular
weight at 13. Why: three headers times four controls is twelve strong glyphs around the content; the eye lands on
chrome instead of the transcript. Screens: 01, 04. Effort M (Dock template styles; Fleet and Armored keep their own).
Risk low.

**8. Better empty states for Map and Channels.**
What: with no session, or a world without room data, the Map still draws its grid, a dark "North" badge, four tool
buttons and "GMCP: Not negotiated · MSDP: Not ne..." beside a zoom slider; the empty message sits over grid lines.
Hide the grid, badge, tools and status when there are no rooms and centre the message on the panel surface; same
for Channels. Why: on first launch a quarter of the window is inert machinery. Screens: 01, 03, 06. Effort S.
Risk low.

**9. Workspace panel: say what matters about a session.**
What: an open session row shows `127.0.0.1:59375 / UTF-8`, a bare dot whose meaning is not explained, and "New
activity" in small text; there is a large gap between Open sessions and Saved worlds, which is pinned to the bottom.
Show the character and the state (Connected, Reconnecting) instead of address and encoding, use an accent dot or an
unread count for activity, and let the two sections flow from the top. Why: the panel is the session switcher, and
it currently reads like a connection log. Screens: 07, 08. Effort M. Risk low.

**10. One icon language in the System toolbar.**
What: Play and Stop are heavy filled glyphs, the rest are thin outlines; with no session both still look enabled.
Use outline icons of one stroke weight, disable Play and Stop when there is nothing to act on, and give each a
tooltip with its shortcut. Why: mixed weights read as assembled rather than designed. Screen: 01. Effort S. Risk low.

**11. Settings: narrower content and quieter help text.**
What: dropdowns stretch to the full 1260 px content width and the help paragraphs are almost as prominent as the
labels. Cap the form at about 560 px and set help text at 12 to 13 in the muted colour with a small gap under its
control. Why: long full-width fields and grey paragraphs make the dialog feel like a form letter. Screens: 11, 12.
Effort S. Risk low.

**12. Session history window tidy-up.**
What: each result repeats the date twice and starts a line with "·" when there is no character; the header reads
"The Lantern Road · · date"; the transcript labels every line "Received" and shows blank received lines; two help
paragraphs say nearly the same thing; the button says "Search / refresh"; the date fields show `<M/d/yyyy>`. Why: the
window works but looks like a debug view. Screen: 17. Effort S to M. Risk low.

**13. World editor details.**
What: new macros all show "New macro / Disabled"; a stray "Disabled" sits under the macro list; the list's scroll
track draws as a pale bar the full height; the top bar's "New" button next to the world picker is ambiguous (new
world?). Why: small inconsistencies add up to "amateur". Screens: 13, 14, 15. Effort S. Risk low.

**14. Find a MUD details.**
What: the page has two titles ("Find a MUD" in the document header and "Browse MUDs" as the heading); the search
placeholder is truncated; online status is "142 online ●" on one row and "Online ●" mixed in with the tags on
another. Keep one title, shorten the placeholder, and put status in the same place on every row. Screens: 01, 02.
Effort S. Risk low.

**15. Map panel status line.**
What: the protocol status truncates ("MSDP: Not negotia...") and runs into the zoom slider; the "North" badge is a
near-black pill on a light map. Show protocol status in a tooltip or the Diagnostics page, and make the badge the
panel colour. Screen: 09. Effort S. Risk low.

### Third: the "feels slow, a little stutter" part

**16. Streaming text moves at 30 frames a second.**
What: output flushes are at least 33 ms apart (`WorkspaceController.OutputFlushGap`). On a 120 Hz Mac display a
steady stream scrolls in visible steps, which matches "a little stutter". Try 16 ms and measure with the perf
harness (`docs/perf.md`): the last change from 60 to 33 ms cost about 4 CPU points under a flood. Effort S. Risk
medium (CPU trade; must be measured, not guessed).

**17. Switching sessions rebuilds the Map and Channels panels.**
What: `ActiveSessionView` builds a new `MapView` and `ChannelsView` each time the active session changes. Cache one
view per session and swap it. Why: a rebuild on every switch is a hitch at exactly the moment the user is watching;
it also loses scroll position and is the root of item 3. Effort M. Risk medium (lifetime and disposal of views per
session).

**18. Virtualise the Channels message list.**
What: already noted in `docs/perf.md`: up to 500 TextBlocks in a StackPanel, the largest UI-thread item under
steady chatter. Effort M. Risk low.

**19. Feedback on hover and press.**
What: every `app-button` carries a `0 2 3` drop shadow and changes state instantly. Flatten the shadow on light
palettes and add a 100 ms brush transition on hover (one-shot, never continuous). Why: soft, quick transitions read
as responsive; hard switches with heavy shadows read as dated. Effort S. Risk low to medium (transitions on many
controls; keep them off the transcript and lists).

### Larger or subjective (for the owner to decide; not quick wins)

**20. Transcript line height and font (investigated, October 9).**

What the terminal control allows (Iciclecreek.Avalonia.Terminal 4.0.2, checked against its README and by reflection
over the shipped assembly):

- Font family, size, style and weight are ordinary properties (`FontFamily`, `FontSize`, `FontStyle`, `FontWeight`),
  plus opt-in `Ligatures`. The client sets the family to `Menlo, Consolas, DejaVu Sans Mono` and the size from
  Settings.
- The cell is measured from the font: a private, non-virtual `UpdateTextMetrics` lays out a sample with
  `FormattedText` and stores `_charWidth`, `_charHeight` and `_baseline`. `CharHeight` and `CharWidth` are read-only.
  There is no line-height, line-spacing or padding property and no protected hook, so a subclass such as
  `MudTerminalSurface` cannot change the row pitch.
- There is an optional Skia renderer (`UseSkiaRenderer`, off in this client) with its own `SkiaFontCache` and
  per-snapshot `CellHeight`. It shares the measured cell, so it does not help either.
- Repaints run on a shared throttle, `TerminalRenderThrottle.TargetFrameRate`, 30 frames a second by default.

Measured rows (headless, Linen, the real transcript): Menlo 16 gives an 18.6 px row, 1.16 times the size, which is
why paragraphs feel dense next to the 1.4 to 1.5 that body text usually gets. Menlo 15 is the same ratio. PT Mono 16 is
1.12, Courier New 17 is 1.13, and Monaco 15 is 1.33, because Monaco's own line metrics are taller.

Options, cheapest first:

1. A font choice in Settings > Terminal (family as well as size). No library change. Monaco alone moves the row from
   1.16 to 1.33 of the size, and a reader who prefers a dense grid keeps Menlo. Effort S to M, risk low (NAWS rows
   change with the font; the transcript tests use the default).
2. An upstream pull request adding a `LineHeight` (multiplier) styled property to `TerminalView`: scale `_charHeight`
   in `UpdateTextMetrics`, centre the glyphs by moving `_baseline` by half the added space, and leave everything else
   (selection, hit testing, NAWS, the `CSI 16 t` cell report, the Skia snapshot's `CellHeight`) on `CharHeight`, which
   already drives them. The library is MIT and actively maintained, and its README already documents a member parity
   test the change would have to satisfy. Effort M, plus the wait for a release.
3. A fork with the same change, consumed as a project reference or a private package, if upstream declines or is slow.
   Effort M, and a library to keep current.
4. Setting `_charHeight` by reflection after the library measures is possible but fragile (private names, and the
   glyphs would sit at the top of taller cells). Not recommended.

Mock pictures (local, `.superpowers/ui/after/20-typography/`): `a-menlo-16-current`, `b-menlo-15`, `c-monaco-15`,
`d-pt-mono-16` and `e-courier-new-17` are the real transcript in each font; `f-mock-line-1.0` and `g-mock-line-1.35`
are a plain-text stand-in (no ANSI colours) at the terminal's own spacing and at 1.35, to show what option 2 would buy.
Recommendation: offer the font choice first, and open the upstream pull request in parallel.

**21. A compact session switcher.** Sessions switch only through the Workspace panel and the toolbar picker. Tabs
above the transcript, or a pill strip in the toolbar, would make two-session play faster. New navigation; owner's
call. Effort M to L.

**22. A "studio" visual system for the System skin.** One icon set at one stroke weight, a 4 and 8 px spacing scale,
a five-step type scale (11, 12, 13, 15, 20) and three surface levels, applied across panels, dialogs and the
directory. Items 6 to 15 are steps toward it; doing it as one pass is a redesign. Effort L.

**23. Composer.** The Look (eye) and Commands (grid) buttons are unlabelled and unrelated in shape; the solid Send
button is the heaviest element in the window although Enter does the same. A quiet send icon inside the field and
labelled tooltips would calm the bottom edge (05). Subjective. Effort S.

## Other skins

Fleet + Hull and Armored + Hull render every scene without breakage. Fleet already has the transcript gutter that
System lacks (compare `Fleet-Hull/04-session.png` with `System-Linen/04-session.png`). Items 3, 4, 6, 8, 9, 12, 16
to 18 apply to every skin; items 1, 7, 10 and 19 should be scoped to System (and Armored where noted) so the drawn
skins keep their look. System + Midnight shows the same issues as Linen; the Fluent blue (item 2) is less jarring on
dark but still off-palette.
