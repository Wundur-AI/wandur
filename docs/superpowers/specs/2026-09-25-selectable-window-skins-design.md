# Selectable window skins

Date: September 25, 2026
Status: Proposed design for review, not implemented
Baseline: main at 4de1e66, confirmed pushed before starting this work

## Intent and scope

Offer three user-selected skins independently of existing color themes: Fleet
(the current look), Armored (the supplied industrial reference without scuffs),
and System (standard platform window decorations with colored app content).
Preserve sessions, docking, accessibility and readable terminal colors. The new
Armored skin uses drawing primitives, not generated pictures or sliced images.
Fleet stays the default. History changes are explicitly out of scope.

The approved direction is skin selection owned by the user. A MUD may supply
colors and supported materials but may not change the selected skin's geometry.
Implementation remains in the normal wandur-client checkout, not the old Fleet
directory. Existing running sessions and real profile data remain untouched.

## Approach and alternatives

Recommended: one shared shell and docking implementation, with a resolved skin
definition supplying metrics, materials and a small set of drawing strategies.
Reuse the existing resource-brush updates and normal Avalonia controls.

Alternatives rejected: three separate window/control trees would duplicate
behavior and invite regressions; an arbitrary downloadable skin scripting or
geometry language would expand the scope and validation burden unnecessarily.

## User experience and persistence

- Add a Skin choice alongside the color theme in Settings > Appearance.
- Add a separate Skin submenu to the existing appearance menu; expose the same
  action from a normal application menu when System hides custom title actions.
- Persist a stable local Skin identifier: Fleet, Armored or System. Missing or
  unrecognized values resolve to Fleet without discarding other preferences.
- Existing settings retain their exact palette, terminal colors and Fleet look.
- Settings preview applies skin and colors; Cancel restores both. Save and the
  quick menu persist the choice and update open windows without reconnecting.
- Localize labels and help in all five supported languages.
- Skin selection neither changes dock visibility/layout nor adds a Terminal
  heading. It never changes history retention or recording.

## Separation of concerns

ClientSettings owns the choice; neither world-theme schema nor the site needs
an update for this release. ThemeService continues resolving the existing color
precedence, then resolves a skin definition from the local choice. Its appearance
cache must include the skin so a skin-only change cannot be skipped.

A resolved definition owns title height, frame insets, panel/header metrics,
corner treatment and renderer identity. Fleet's existing geometry and materials
remain its implementation. Armored gets its own renderer and layout calculations.
System gets no ornamental frame and simple palette-derived control surfaces.
Avoid scattering new skin-name comparisons throughout individual views.

Replace Fleet-specific global assumptions in ThemeService, MainWindow title and
fullscreen handling, ThemeWindowSkinHost, ThemePlaque, ThemeDockSkinHost and dock
styles with the appropriate resolved behavior. Keep rendering out of session and
protocol code. Shared contrast resources must not depend on Fleet being active.
No recursive property/update callbacks or new recursive routines.

Legacy world geometry remains non-authoritative. Existing compatible materials
may tint or texture designated surfaces in the custom skins, but cannot replace
their frames, enlarge their insets or displace controls. System omits decorative
frame art. These three skins need no image assets to render correctly.

## Armored visual design

Preserve the reference's layered construction, recessed dark title module,
light metal plates, thin seams, small vent details and luminous side strips.
Remove scratches, dirt, labels, irregular panel subdivisions and random damage.
Use WANDUR plus the current world name, never the reference's branding.

- Draw beveled plates with paths, edge highlights and restrained gradients.
- Fit the centered title module to icon and text with approximately 40 logical
  pixels of padding on each side. Reserve platform buttons and action space;
  clamp width and ellipsize long titles before overlaps occur.
- Embed the title module into the toolbar with a receiving recess and shadow,
  rather than overlaying an unrelated rectangle on top of it.
- Use uninterrupted accent-light strips down the sides. Color follows the theme.
- Use modest bottom-corner shoulders contained within the frame, not protruding
  decorations that require transparent nonrectangular native windows.
- Keep straight sections extensible and corners/bevel widths fixed in logical
  units. Recompute paths on layout changes instead of scaling a whole drawing.
- Dock headers echo the metal and recess treatment but have thin separators,
  compact controls and no bulky independent armored frames.
- Retain existing palette-selected terminal/input backgrounds. Hull and Slate
  provide the intended dark work areas; explicit user color choices still win.

Initial layout targets are a 64-DIP decorative header and 8-DIP side/bottom
frame for Armored, with 32-DIP dock headers and 24-DIP header action hit areas.
Platform safe areas can increase header space. These are starting values for
rendered visual review, not reasons to crop native buttons or text. Fleet keeps
its current measurements. Avoid animated glows and per-frame timers.

## System and platform behavior

System restores standard platform window decoration and removes the custom title
plaque, accent rails and metal frame. It retains themed application controls,
compact dock headers, existing toolbars and palette-derived content surfaces.
It does not claim that Avalonia widgets become native AppKit/WinUI widgets.
The OS owns native titlebar colors, controls and window shadow.

Custom skins retain native button behavior and appropriate platform safe areas.
Window dragging, double-click, resizing, maximize, restore and fullscreen must
remain functional. Native Mac button positioning adjustments must be undone in
System mode, not leak from the previous skin.

Floating dock windows follow the selected skin at a reduced decorative density,
while retaining their own titles and platform window controls. They use the same
resolved resources rather than a separate preference. Modal/settings/history
windows retain their standard decorations and share the selected color palette.

Fullscreen suppresses custom title/frame space for either custom skin and uses
native fullscreen behavior for System. A visible exit command remains reachable
with the toolbar hidden. Restoring reloads the currently selected skin, not stale
metrics. Skin changes must preserve open sessions, draft input and dock state.

## Verification and acceptance

1. Settings round-trip, missing/unknown value fallback, preview cancellation and
   quick-menu persistence. No world theme can switch skin geometry.
2. Switch each skin to every other skin, with Hull, Slate, a light theme and a
   world palette. Verify no stale brushes, insets, native-button offsets or title
   actions; terminal text and disabled controls remain readable.
3. Verify main and floating windows, optional left/right docks, narrow and wide
   sizes, long titles, maximized and fullscreen states, and toolbar-hidden exit.
4. Capture actual Avalonia output at 1x and 2x scale with fictional MUD content.
   Compare Fleet against the baseline and visually inspect Armored and System.
   Generated concept images are not implementation acceptance evidence.
5. Exercise native Mac drag/resize/buttons and native Windows equivalents when
   an environment is available. Report unavailable platform checks explicitly;
   headless screenshots alone do not establish native titlebar correctness.
6. Run focused RED/GREEN tests, full Release tests/build, localization checks and
   independent review. Do not restart the owner's live client for verification.

## Out of scope

History improvements, embeddings, site changes, skin downloads/plugins, new MUD
schema fields, arbitrary user-authored geometry, animated machinery, bitmap frame
assets, and claims of pixel-identical rendering across platform font systems.

## Next step

Review this design, then write and review the implementation plan before product
code changes. The existing working baseline is already on origin/main.
