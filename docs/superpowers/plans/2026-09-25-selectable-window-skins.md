# Selectable Window Skins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship independent Fleet, Armored and System skin selection without changing sessions, docking state or existing color choices.

**Architecture:** Preserve a single live shell and dock tree. Resolve the local skin into immutable metrics and rendering behavior after resolving colors; share that result across the main window and floating docks. Render Armored with geometry and gradients, while System releases custom titlebar ownership to the platform.

**Tech Stack:** Existing .NET 10, Avalonia 12, Dock, xUnit and Avalonia headless/Skia rendering. No new packages.

**Spec:** ../specs/2026-09-25-selectable-window-skins-design.md

## Global Constraints

- Fleet stays the default. History changes are explicitly out of scope.
- No recursive property/update callbacks or new recursive routines.
- These three skins need no image assets to render correctly.
- Implementation remains in the normal wandur-client checkout, not the old Fleet directory.
- Existing running sessions and real profile data remain untouched.
- Localize labels and help in all five supported languages.
- The OS owns native titlebar colors, controls and window shadow.
- Retain explicit user color choices; a MUD cannot select geometry.
- Stay on main in the existing checkout as agreed; no new worktree. The working baseline through 4de1e66 is already on origin/main. Commit implementation milestones locally, but do not infer permission to push unfinished skin work.
- Use the repository commit identity and sole trailer from CLAUDE_HANDOFF.md. No em dashes in source, docs or commits. No live MUD connections for tests.
- Read docs/verification.md and the spec before execution; record progress in a gitignored .superpowers ledger.

## Review Focus

1. A skin-only change with identical colors must invalidate the appearance cache; test in Task 1.
2. Cancel after changing skin while a session/world theme changes must restore the saved skin, not stale world colors; test in Task 2.
3. Enter fullscreen, change skin, hide the toolbar, then exit must restore current geometry and retain an accessible exit; test in Task 3.
4. Existing and newly opened floating docks must update without recreating sessions or accumulating event subscriptions; test in Task 5.
5. An extremely long title or high DPI at minimum window width must not overlap native buttons/actions, distort corners or produce invalid rectangles; test in Task 4.

## File and interface map

New Core file: `src/Wandur.Core/Settings/WindowSkinId.cs` owns stable identifiers and normalization. `ClientSettings.Skin` stores a string, not an enum that could make old/new settings fail deserialization.

New Desktop files:
- `WindowSkinDefinition.cs`: immutable per-skin metrics and capability flags.
- `ArmoredSkinRenderer.cs`: frame, plaque and toolbar drawing, no controls or session access.
- `ArmoredTitleLayout.cs`: finite/clamped title placement, no drawing or native calls.
- `MainWindow.Skin.cs`: idempotent custom/System decoration application, extracted from existing MainWindow title paths only where required.
- `SkinnedDockHostWindow.cs`: Dock HostWindow lifecycle and compact custom header integration.

Modify existing ThemeService, Fleet skin/hosts, MainWindow title actions, native Mac inset service, settings/menu models and Dock styles. Keep existing Fleet classes as the renderer implementation; do not rename unrelated code or rewrite the terminal.

New tests: `WindowSkinSettingsTests.cs` in Core tests; `WindowSkinSelectionTests.cs`, `WindowSkinTransitionTests.cs`, `ArmoredSkinTests.cs`, `WindowSkinDockTests.cs` and `WindowSkinCaptureTests.cs` in Desktop tests. Extend existing Fleet, preferences, menu and Mac inset tests rather than replacing their assertions with weaker ones.

All code snippets below are focused contracts or regression seeds. Production methods additionally preserve existing documented behavior; the action steps specify required integration.

### Task 1: Persist skin choice and resolve independent appearance

**Files:** Create WindowSkinId.cs, WindowSkinDefinition.cs and both WindowSkinSettingsTests.cs/WindowSkinSelectionTests.cs above. Modify `src/Wandur.Core/Settings/ClientSettings.cs`, `src/Wandur.Desktop/ThemeService.cs`, `FleetSkin.cs` and `DefaultSkin.cs`.

**Interfaces:**

```csharp
// Wandur.Core.Settings
public static class WindowSkinId
{
    public const string Fleet = "Fleet", Armored = "Armored", System = "System";
    public static string Normalize(string? value) => value switch
    {
        Armored => Armored, System => System, _ => Fleet
    };
}
// ClientSettings addition
public string Skin { get; init; } = WindowSkinId.Fleet;

// Wandur.Desktop
internal sealed record WindowSkinDefinition(string Id, bool CustomChrome,
    double TitleHeight, Thickness FrameInset, double DockHeaderHeight)
{
    internal static WindowSkinDefinition Resolve(string? id) =>
        WindowSkinId.Normalize(id) switch
        {
            WindowSkinId.Armored => new(WindowSkinId.Armored, true, 64,
                new Thickness(8, 0, 8, 8), 32),
            WindowSkinId.System => new(WindowSkinId.System, false, 0,
                default, 30),
            _ => new(WindowSkinId.Fleet, true, FleetTitleLayout.BandHeight,
                new Thickness(6, 0, 6, 6), 38)
        };
}
```

Resolve Fleet frame/header metrics from the actual baseline resources if they differ from the seed above; preserve the baseline exactly and pin actual values in tests before refactoring. Expose `ThemeService.ActiveWindowSkin` as the resolved definition. Keep `UsesFleetSkin` true only for Fleet; audit consumers and replace usages that really mean custom chrome or readable terminal surfaces, not Fleet geometry.

- [ ] Write Core RED tests: missing/null/unknown Skin resolves to Fleet, each known ID persists through JSON settings and SQLite settings, and unknown Skin does not discard Theme, profiles or font size. Use temporary stores and existing SettingsTests patterns.

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("FutureSkin")]
public void UnknownSkinFallsBackWithoutChangingOtherSettings(string? skin)
{
    var settings = new ClientSettings { Skin = skin!, Theme = "Slate", FontSize = 17 };
    settings.Validate();
    Assert.Equal(WindowSkinId.Fleet, WindowSkinId.Normalize(settings.Skin));
    Assert.Equal("Slate", settings.Theme);
    Assert.Equal(17, settings.FontSize);
}
```

- [ ] Write Desktop RED test for repeated ThemeService.Apply with the same palette and different Skin; assert ActiveWindowSkin changes and Applied fires once for each real change, never for an identical repeat. Assert applying world materials does not alter the selected ID/metrics.
- [ ] Run `dotnet test Wandur.sln -c Release --no-restore --filter 'FullyQualifiedName~WindowSkinSettingsTests|FullyQualifiedName~WindowSkinSelectionTests'` and confirm the intended failure.
- [ ] Implement normalization and add normalized skin to ThemeService's appearance cache key. Resolve geometry independently of `DefaultSkin.Merge` world geometry. Preserve palette precedence and brush reuse. Publish the resolved definition before Applied fires. Keep map/channel/input contrast based on actual surface, not skin identity.
- [ ] Run the new tests plus `FleetPaletteTests`, `ThemeSwitchContrastTests`, `SettingsTests` and `SqliteSettingsScriptTests`. Confirm existing missing-Skin fixtures still look like Fleet. Commit only this task's files with message `Separate local skin selection from color themes`.

### Task 2: Preferences and accessible appearance menus

**Files:** Modify `src/Wandur.Desktop/ViewModels/PreferencesViewModel.cs`, `Views/OptionsDialog.axaml`, `ThemeMenuButton.cs`, `DesktopMenus.cs`, `SessionWorkspace.Appearance.cs` if needed, and all five `src/Wandur.Core/Localization/Strings*.resx` resources plus generated Strings.cs. Extend `PreferencesTests.cs`, `ThemeMenuButtonTests.cs` and WindowSkinSelectionTests.cs.

**Interfaces:** Consumes ClientSettings.Skin and WindowSkinId.Normalize. PreferencesViewModel exposes observable `string Skin`, persisted by its existing settings-copy builder and preview callback. Menus call `active.SaveSettings(active.Settings with { Skin = id })`; this must not turn off UseWorldThemes or change Theme. Share the list of stable IDs and localized labels between both menu presentations.

- [ ] Add RED preferences tests using the existing `Store : IClientSettingsStore` fixture. Assert preview changes resolved skin, save persists, and Dispose/Cancel restores. Add a connected fake/demo session with a command draft and assert neither draft nor transcript changes during preview.

```csharp
[AvaloniaFact]
public void CancellingSkinPreviewRestoresSavedSkin()
{
    var store = new Store { Settings = new() { Skin = WindowSkinId.Fleet } };
    using (var model = new PreferencesViewModel(store, ThemeService.Apply))
    {
        model.Skin = WindowSkinId.Armored;
        Assert.Equal(WindowSkinId.Armored, ThemeService.ActiveWindowSkin.Id);
    }
    Assert.Equal(WindowSkinId.Fleet, ThemeService.ActiveWindowSkin.Id);
    Assert.Equal(WindowSkinId.Fleet, store.Settings.Skin);
}
```

- [ ] Add menu RED tests for selection against the active controller at click time, checked state on reopening, preserved world color following, and Skin availability through the standard View menu when custom title actions are absent. Append menu items where possible to preserve unrelated positional test assumptions.
- [ ] Run focused preferences/menu tests and confirm missing behavior fails.
- [ ] Add observable preference, selection UI, Skin submenu and View menu counterpart. Add keys `Skin`, `SkinFleet`, `SkinArmored`, `SkinSystem`, `SkinHelp` with complete translations. Regenerate with `python3 scripts/generate-localization.py`.
- [ ] Add the review-focus regression: preview Armored, switch active world/session, then cancel. Call `EndAppearanceSettingsPreview` through the existing dialog lifecycle and verify the current world's colors plus saved skin, not the original world's palette.
- [ ] Run focused tests and `python3 scripts/generate-localization.py --check`. Commit with message `Expose independent skin selection in appearance controls`.

### Task 3: Native/System chrome and safe window transitions

**Files:** Create `src/Wandur.Desktop/MainWindow.Skin.cs` and WindowSkinTransitionTests.cs. Modify `MainWindow.cs`, `MainWindow.TitleActions.cs`, `ThemeWindowSkinHost.cs`, `ThemeBezelHost.cs`, `ThemeOrnamentLayer.cs`, `ThemePlaque.cs`, `Services/MacTrafficLightInset.cs` and the existing `Styles/Fleet.axaml` selectors as required. Extend TitleActionsTests and Mac traffic-light tests.

**Interfaces:** `MainWindow.ApplyWindowSkin()` is a private idempotent transition called by the existing appearance update path. It consumes ThemeService.ActiveWindowSkin. Native button inset eligibility becomes `ActiveWindowSkin.CustomChrome && normalWindowState`; no titlebar height writes are added to MacTrafficLightInset.

- [ ] Add RED tests for Fleet -> System -> Fleet and every pair of skin IDs. System must have client-area extension off, the decorative frame/plaque/title actions hidden, native decorations enabled and no decorative content insets. Changing only colors must not alter native titlebar metrics.

```csharp
// Inside a window fixture using the same temporary stores as TitleActionsTests.
window.Sessions.PreviewAppearanceSettings(new ClientSettings { Skin = WindowSkinId.System });
Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
Assert.False(window.ExtendClientAreaToDecorationsHint);
var plaque = window.GetVisualDescendants().OfType<Border>()
    .Single(b => b.Name == "PlaqueTitleHost");
Assert.False(plaque.IsEffectivelyVisible);
window.Sessions.PreviewAppearanceSettings(new ClientSettings { Skin = WindowSkinId.Fleet });
Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
Assert.True(window.ExtendClientAreaToDecorationsHint);
Assert.True(plaque.IsEffectivelyVisible);
```

- [ ] Run the new transition tests RED. Add a deterministic MacTrafficLightPosition test that applies inset=true, then inset=false and verifies the original coordinates on both flipped/unflipped parents.
- [ ] Implement decoration routing. System uses Avalonia 12's normal decoration configuration and clears all custom frame state, including legacy bitmap/ornament hosts. Move native-vs-custom decisions into ApplyWindowSkin; leave Fleet drawing intact. Ensure simplified System brushes do not inherit metallic Fleet styles after a switch.
- [ ] Update fullscreen restoration to consult the current definition. Add tests for entering fullscreen from each skin, switching while fullscreen, hiding/showing toolbar, then restoring. Reuse the existing footer exit behavior and verify the native menu remains available.
- [ ] Check event subscriptions and finite transition counts with repeated resize/theme changes; no recursive Apply calls and no window recreation. Run TitleActionsTests, FleetSkinTests, ThemeSwitchTests and new transition tests GREEN. Commit `Support standard system chrome and safe skin transitions`.

### Task 4: Clean vector Armored renderer

**Files:** Create `src/Wandur.Desktop/ArmoredTitleLayout.cs`, `ArmoredSkinRenderer.cs` and ArmoredSkinTests.cs. Modify ThemeWindowSkinHost.cs, ThemePlaque.cs, MainWindow.Skin.cs, the title placement portion of MainWindow.cs and FleetToolbarSurface.cs only to route shared toolbar ownership appropriately.

**Interfaces:** Reuse the existing `FleetTitlePlacement(Rect Bounds, bool PlainTitle)` result initially to avoid changing Fleet calculations. New `ArmoredTitleLayout.Calculate(double windowWidth, double leftExclusion, double rightExclusion, double measuredTextWidth)` returns that result. Renderer entrypoints:

```csharp
internal static void DrawFrame(DrawingContext context, Size size, Rect title,
    IBrush metal, IBrush edge, IBrush highlight, IBrush accent);
internal static void DrawPlaque(DrawingContext context, Rect bounds,
    IBrush metal, IBrush inset, IBrush edge, IBrush accent);
internal static IBrush CreateToolbar(Size size, Rect title,
    IBrush metal, IBrush edge, IBrush highlight);
```

The renderer is a static class with no window, settings, network or session access. Geometry is recomputed from bounds; cache only by size/title/material revision, never allocate a timer. Existing title text/icon remain real controls and noninteractive decoration stays out of hit testing.

- [ ] Add RED geometry tests for widths 0, 320, 800 and 1536, unequal native/action exclusions, empty title, a 4000-DIP measured title and nonfinite inputs. Assert nonnegative finite bounds, centered placement when space permits and PlainTitle fallback before text/actions collide.

```csharp
[Fact]
public void ArmoredTitleLeavesActionAndCaptionSpace()
{
    var place = ArmoredTitleLayout.Calculate(800, 100, 160, 4000);
    Assert.True(double.IsFinite(place.Bounds.Width));
    Assert.True(place.Bounds.Left >= 100);
    Assert.True(place.Bounds.Right <= 640);
    Assert.InRange(place.Bounds.Center.X, 399.5, 400.5);
}
```

- [ ] Run geometry tests RED; implement fixed cap/bevel measurements and clamped center width with 40-DIP interior text padding, a 64-DIP band and a restrained toolbar overlap. The fallback must remain usable even when no plaque fits.
- [ ] Draw clean nested plate paths with dark edge then bright inner edge, a recessed title rectangle, paired vertical accent lights and continuous side rails. Anchor bottom shoulders within the frame/footer safe space; repeat only small fixed-size vent marks inside those shoulders. No scratches, random seams, labels or separate bitmap panels.
- [ ] Draw the toolbar's receiving notch from the same title rectangle translated to toolbar coordinates. Use an edge shadow plus highlight to convey depth. Do not put a rectangular image behind the plaque or distort the corner details when resizing.
- [ ] Add headless RED/GREEN integration assertions: title labels/actions unobscured, lights visible down both sides, no central Terminal heading, no image needed by frame/plaque hosts, no frame overlay intercepting clicks. Use representative pixel samples away from text/antialiasing boundaries plus layout assertions, not one brittle full-screen hash.
- [ ] Render actual Armored Hull and Slate at narrow/wide sizes. Inspect with view_image and tune geometry while preserving fixed cap sizes. Run FleetReferenceCaptureTests and Fleet title tests to prove unchanged geometry. Commit `Draw the clean scalable Armored window skin`.

### Task 5: Compact matching docks and floating-window lifecycle

**Files:** Create `src/Wandur.Desktop/SkinnedDockHostWindow.cs` and WindowSkinDockTests.cs. Modify `WorkspaceFactory.cs` (existing HostWindowLocator), `ThemeDockSkinHost.cs`, `Styles/ThemeDockSkin.axaml`, `Styles/Fleet.axaml`, `Converters/DockChromeConverter.cs` and App.axaml resources if necessary.

**Interfaces:** SkinnedDockHostWindow derives from the same Dock HostWindow type currently constructed in WorkspaceFactory. Its constructor takes no parameters; it subscribes to ThemeService.Applied on open and unsubscribes on close. Its custom header uses the resolved definition and real native window controls, not a second user preference. The existing dock content remains the child.

- [ ] Add RED tests using existing DockChromeTests floating fixtures: create dock, change skin, close it, create another. Verify both old and new windows follow the selected skin, title/close actions remain usable, and closed windows stop receiving theme callbacks.
- [ ] Add RED tests for left only, right only, both and neither dock. Assert no orphan decorative bars and no double-thick outer edge at the window boundary. Dock layout and content object identity must remain unchanged during switching.
- [ ] Route HostWindowLocator to SkinnedDockHostWindow. System floating windows use normal platform decorations; custom floating windows use a compact matching header, not the large main-window plaque. Scope the selected skin to its header/control styles and make the current title accessible.
- [ ] Drive dock header height and border resources from the definition. Armored gets a clean recessed header and thin separator; System gets a simple flat header; Fleet retains its current resources. Keep compact 24-DIP action targets and meaningful focus/hover states. Dock drag gestures continue using the existing Dock mechanisms.
- [ ] Run dock, floating, title and transition tests GREEN, including all six cross-skin transitions while a floating dock is open. Commit `Apply selected skins consistently to docked and floating panels`.

### Task 6: Visual acceptance, platform verification and final review

**Files:** Create WindowSkinCaptureTests.cs; update `docs/client-architecture.md`, `docs/verification.md`, `CLAUDE_HANDOFF.md` and the plan checklist. Capture output goes to an explicit temporary directory, not versioned binary assets.

**Interfaces:** Use existing `WANDUR_CAPTURE_DIR` capture convention and temporary/mock session setup from FleetReferenceCaptureTests and site screenshot tests. Capture fixtures must use fictional MUD content and never read the owner's database.

- [ ] Add a theory matrix over Fleet/Armored/System with Hull/Slate/Paper and a test world palette. Reuse the existing ThemeSwitchContrastTests checks for terminal, map, command input, diagnostics and disabled controls. Include switching back to the first skin to expose stale resource state.
- [ ] Capture all three skins at 800 and 1536 logical pixels, short/long titles, optional docks, and scale factors 1 and 2. Use the headless platform's render scaling support; verify output pixel dimensions rather than enlarging an existing bitmap. Include a floating dock. Inspect actual screenshots and correct clipping, excess padding and mismatched seams.

```csharp
// Existing headless capture idiom, after Show/UpdateLayout/ForceRenderTimerTick.
using var frame = window.CaptureRenderedFrame();
Assert.NotNull(frame);
frame.Save(Path.Combine(directory, $"{skin}-{theme}-{width}.png"),
    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
```

- [ ] Use an isolated native Mac test instance, with temporary settings and mock/demo content, for drag, double-click, resize, native buttons, floating docks and fullscreen. Read the computer-use skill before GUI interaction. Do not replace or restart the owner's app bundle. If no Windows host is available, explicitly record native Windows checks as unverified.
- [ ] Run `WANDUR_DIRECTORY_URL=http://127.0.0.1:1 dotnet test Wandur.sln -c Release --no-restore`, `dotnet build Wandur.sln -c Release --no-restore`, `python3 scripts/generate-localization.py --check`, and `git diff --check`. Use scoped elevation when required for .NET pipes/loopback fixtures.
- [ ] Request a fresh independent review focused on the five failure modes above, native lifecycle, settings cancellation, contrast and Fleet preservation. Reproduce actionable findings with failing regressions before fixing them. Rerun affected tests and the final full suite after fixes.
- [ ] Update docs with actual counts, capture paths, native checks performed and limitations. Commit exact owned files with `Verify and document selectable window skins`. Confirm clean main and report how to choose each skin; no automatic push or owner-session restart.

## Plan self-review

Spec coverage: settings and user ownership (Tasks 1-2), pure drawing and Armored design (Task 4), native/System transitions and fullscreen (Task 3), optional/floating docks (Task 5), localization (Task 2), visual/DPI/native verification and regression preservation (Task 6). All five review-focus cases are assigned to explicit tests. No SDK/site schema or history work is included.

Recommended execution: Native, with this agent implementing the dependent tasks in sequence and one fresh independent reviewer at the end. These tasks share appearance resources and window lifecycle code, so parallel editing would create unnecessary integration risk.
