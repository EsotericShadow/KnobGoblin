# KnobForge UX/UI Overhaul - Sequenced Agent Plan

## Objective
Ship a power-user UX/UI refinement pass for KnobForge that improves speed, clarity, and precision without reducing capability.

This plan is optimized for a single expert user (no beginner onboarding requirements).

## Scope Baseline (Current State)
- Inspector complexity is high and increasing (`~234` named controls, `~100` sliders).
- New systems now in play:
  - User reference profile save/load/apply.
  - Scratch painting mode (abrasion type, width, depth, drag resistance, Option/Alt depth ramp, exposed color).
  - Expanded collar presets (custom imported + Meshy preset paths).
- Primary files are concentrated in:
  - `KnobForge.App/Views/MainWindow.axaml`
  - `KnobForge.App/Views/MainWindow.axaml.cs`
  - `KnobForge.App/Views/MainWindow.Initialization.cs`
  - `KnobForge.App/Views/MainWindow.PaintBrushHandlers.cs`
  - `KnobForge.App/Views/MainWindow.ReferenceProfiles.cs`
  - `KnobForge.App/Views/MainWindow.CollarIndicatorMaterialHandlers.cs`
  - `KnobForge.App/Views/MainWindow.EnvironmentShadowReadouts.cs`
  - `KnobForge.App/Controls/MetalViewport.cs`
  - `KnobForge.Core/KnobProject.cs`
  - `KnobForge.Core/Scene/CollarNode.cs`

## Orchestration Rules For The Next Agent
1. Execute phases in order. Do not skip dependencies.
2. Keep each phase shippable and testable on its own.
3. Do not remove parameters; reorganize presentation and interaction.
4. Preserve existing render behavior unless explicitly targeted.
5. After each phase:
   - build (`dotnet build KnobForge.sln`)
   - smoke test impacted flows
   - append concise status note to this file under `Execution Log`.

## Phase Sequence

## Phase 0 - Baseline And Instrumentation
### Goal
Create a reliable baseline before layout changes.

### Tasks
- Capture current inspector structure and control mapping.
- Add lightweight logging points for mode/state transitions only where needed.
- Record known UX pain points as reproducible scenarios.

### Exit Criteria
- Baseline checklist completed.
- No behavior changes yet.

---

## Phase 1 - Unified Inspector Architecture
### Goal
Replace tab-hunting with a faster single inspector flow.

### Tasks
- Consolidate right-side editing into one primary inspector surface with collapsible sections.
- Add pinned `Favorites` section at top.
- Add `Recent Tweaks` section (last changed controls).
- Keep existing section semantics (Lighting, Model, Material, Brush, Environment, Shadows), but in one scroll model.

### File Focus
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.axaml.cs`
- `KnobForge.App/Views/MainWindow.Initialization.cs`

### Exit Criteria
- All existing parameters still reachable.
- No critical workflow requires tab switching.

---

## Phase 2 - Parameter Search And Jump
### Goal
Instant access to any control by name.

### Tasks
- Implement command-style search (`Cmd+K`) for control/parameter jump.
- Focus target control and auto-expand containing section.
- Add aliases for common terms (`rough`, `scratch`, `collar`, `shadow`, `env`, etc.).

### File Focus
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.axaml.cs`

### Exit Criteria
- Any commonly used parameter is reachable in <= 2 interactions.

---

## Phase 3 - Brush/Scratch Contextual Editing
### Goal
Reduce paint-related noise and surface scratch controls only when relevant.

### Tasks
- Make Brush panel context-aware by active channel.
- When `PaintChannel == Scratch`, prioritize scratch controls at top.
- When non-scratch channels are active, collapse/de-emphasize scratch-only controls.
- Keep advanced paint controls grouped behind one expander.

### File Focus
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.axaml.cs`
- `KnobForge.App/Views/MainWindow.PaintBrushHandlers.cs`

### Exit Criteria
- Scratch workflow is visibly optimized with fewer irrelevant controls shown.

---

## Phase 4 - Viewport Paint HUD And State Feedback
### Goal
Expose paint state live in viewport so interaction intent is always visible.

### Tasks
- Add compact viewport HUD with:
  - active channel
  - brush/abrasion type
  - active size and opacity
  - live scratch depth indicator
  - Option/Alt modifier state for depth ramp
  - hit mode indicator (`mesh hit` / `fallback`)
- Ensure HUD updates do not degrade paint responsiveness.

### File Focus
- `KnobForge.App/Controls/MetalViewport.cs`
- `KnobForge.App/Views/MainWindow.axaml` (if overlay host updates are needed)

### Exit Criteria
- Paint interactions are self-describing without reading inspector text.

---

## Phase 5 - Reference Profile Manager UX
### Goal
Evolve from simple save/apply to full profile lifecycle management.

### Tasks
- Keep quick-save, add profile management actions:
  - rename
  - overwrite
  - delete
  - duplicate
- Visually separate built-in styles from user profiles.
- Add safety confirm for destructive actions.

### File Focus
- `KnobForge.App/Views/MainWindow.ReferenceProfiles.cs`
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.ModelHandlers.cs`

### Exit Criteria
- Profile operations are complete without manual file edits.

---

## Phase 6 - Collar Preset UX Cleanup
### Goal
Clarify imported path semantics across custom and Meshy presets.

### Tasks
- Rename ambiguous label (`Imported STL Path`) to neutral (`Mesh Path`).
- For Meshy presets:
  - keep path read-only
  - show resolved source path clearly
- For custom import preset:
  - keep path editable
  - provide inline path validity state.

### File Focus
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.axaml.cs`
- `KnobForge.App/Views/MainWindow.CollarIndicatorMaterialHandlers.cs`
- `KnobForge.Core/Scene/CollarNode.cs` (only if API adjustment is needed)

### Exit Criteria
- Collar preset/path behavior is predictable and obvious at a glance.

---

## Phase 7 - Precision Control Row Standardization
### Goal
Standardize editing quality for all numeric controls.

### Tasks
- For priority controls, provide:
  - slider
  - direct numeric entry
  - reset-to-default
  - consistent unit formatting
- Normalize decimal precision in readouts.
- Add fine/coarse nudge behavior via modifiers.

### File Focus
- `KnobForge.App/Views/MainWindow.axaml`
- `KnobForge.App/Views/MainWindow.EnvironmentShadowReadouts.cs`
- `KnobForge.App/Views/MainWindow.axaml.cs`

### Exit Criteria
- High-frequency controls support both rapid and precise edits.

---

## Phase 8 - Performance-Aware Update Policy
### Goal
Keep interactivity smooth as control complexity grows.

### Tasks
- Classify control updates:
  - live (cheap)
  - debounced drag (medium)
  - apply-on-release (heavy)
- Implement policy for heavy geometry-affecting controls first.
- Verify no regressions in painting and camera manipulation.

### File Focus
- `KnobForge.App/Views/MainWindow.*.cs`
- `KnobForge.App/Controls/MetalViewport.cs`

### Exit Criteria
- No noticeable stutter in common workflows.

---

## Phase 9 - Export UX Parity Messaging
### Goal
Expose rendering parity caveats clearly at export time.

### Tasks
- Add concise export-side note/status for scratch carve parity expectations.
- Keep wording technical and brief (power-user tone).

### File Focus
- `KnobForge.App/Views/RenderSettingsWindow.axaml`
- `KnobForge.App/Views/RenderSettingsWindow.axaml.cs`

### Exit Criteria
- Export behavior caveats are visible before long render jobs.

## Validation Matrix
- Shape editing still works end-to-end.
- Light editing + shadow tuning still works end-to-end.
- Paint channels (rust/wear/gunk/scratch/erase) still stamp correctly.
- Scratch drag behavior still respects resistance and Option/Alt ramp.
- Reference profile save/load/apply still round-trips.
- Collar presets still resolve intended source paths.
- Render dialog still validates and exports.

## Recommended Delivery Slices
1. Slice A: Phase 1 + Phase 2.
2. Slice B: Phase 3 + Phase 4.
3. Slice C: Phase 5 + Phase 6.
4. Slice D: Phase 7 + Phase 8 + Phase 9.

## Execution Log
- 2026-02-20: Slice A completed (Phase 1 + Phase 2). Replaced tabbed inspector with a unified scroll inspector; added Favorites + Recent Tweaks; implemented `Cmd+K` parameter search/jump with section auto-expand; `dotnet build KnobForge.sln` passed.
- 2026-02-20: Slice B completed (Phase 3 + Phase 4). Brush inspector made context-aware by active channel (scratch essentials prioritized and scratch-only advanced controls collapsed when not in Scratch); added viewport Paint HUD with live channel/tool state, size/opacity, scratch depth, Option/Alt ramp state, and hit mode (`mesh hit` / `fallback` / `idle`); `dotnet build KnobForge.sln` passed.
- 2026-02-20: Slice C completed (Phase 5 + Phase 6). Reference Profile Manager now supports explicit rename/overwrite/delete/duplicate actions from UI (quick-save preserved) with safety confirm prompts for destructive overwrite/delete; style menu now visually groups built-in styles vs user profiles. Collar mesh-path UX updated to neutral `Mesh Path` labeling, clear resolved source path display, read-only preset-managed paths for Meshy presets, editable path for custom import preset, and inline path validity status. `dotnet build KnobForge.sln` passed.
- 2026-02-20: Slice D completed (Phase 7 + Phase 8 + Phase 9). Added standardized precision rows for high-frequency Environment/Shadow controls (slider + direct numeric entry + reset defaults) with modifier nudging (`Alt` fine, `Shift` coarse) and normalized 3-decimal readouts for priority controls. Added performance-aware update policy for heavy geometry sliders via debounced refresh with apply-on-release flush; environment updates now render-only (no full scene refresh). Added export dialog parity note clarifying scratch carve expectations for GPU viewport export path. `dotnet build KnobForge.sln` passed.
