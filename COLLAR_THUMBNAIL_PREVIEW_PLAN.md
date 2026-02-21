# Collar Thumbnail Preview Plan

## Goal
Show visual thumbnail previews for collar model options before selection, without introducing UI instability or project-load regressions.

## Non-Goals
- No new collar geometry processing.
- No changes to collar transform/material behavior.
- No hot-loading redesign beyond thumbnail metadata refresh.

## Baseline
- Build gate: `dotnet build KnobForge.sln -v minimal` must pass before and after each phase.
- Current baseline is clean (0 warnings, 0 errors).

## Decision Gate 1: Preview UX Surface
Choose one:

1. `Dropdown + thumbnail rows` (Recommended)
   - Keep current `ComboBox` flow.
   - Each option shows image + label.
   - Fastest path, least UX disruption.
2. `Grid gallery + select button`
   - Larger visual cards.
   - Better discoverability, more layout work.
3. `Hybrid`
   - ComboBox selector plus side preview pane.
   - Good clarity, medium complexity.

## Phase 1: Data Contract + Discovery Hardening
Scope:
- Normalize thumbnail lookup contract:
  - source directory: `collar_models/collar_thumbnails`
  - filename matching rules for model-to-thumbnail pairing
  - supported image formats
- Define fallback behavior when thumbnails are missing/corrupt.
- Ensure option rebuild safely disposes thumbnail bitmaps.

Build gate:
- `dotnet build KnobForge.sln -v minimal`

Regression checks:
- Start app with empty/missing thumbnail directory.
- Start app with mixed valid + invalid thumbnail files.

## Phase 2: UI Rendering Layer
Scope:
- Implement selected UX surface from Decision Gate 1.
- Add visual states:
  - thumbnail available
  - missing thumbnail placeholder
  - loading/error-safe fallback
- Ensure consistent row height, text truncation, and no background artifacts.

Build gate:
- `dotnet build KnobForge.sln -v minimal`

Regression checks:
- Verify selection still binds to existing collar preset IDs.
- Verify keyboard navigation and mouse selection behavior.

## Decision Gate 2: Placeholder Strategy
Choose one:

1. `Neutral placeholder tile` (Recommended)
   - Consistent, low-noise UI.
2. `Model-name initials placeholder`
   - More informative, slightly busier.
3. `No placeholder (text only fallback)`
   - Simplest, lowest visual consistency.

## Phase 3: Hot-Add/Refresh Behavior
Scope:
- Ensure filesystem watch refreshes options when:
  - collar model added/removed
  - corresponding thumbnail added/removed/updated
- Debounce refresh bursts and avoid duplicate UI churn.
- Preserve current selection when possible after refresh.

Build gate:
- `dotnet build KnobForge.sln -v minimal`

Regression checks:
- Add/remove thumbnail while app runs.
- Add model first, thumbnail later; verify option updates without restart.

## Phase 4: Performance + Memory Safety Pass
Scope:
- Validate bitmap lifetime (no leaked `Bitmap` instances on rebuild/close).
- Validate UI responsiveness with large collar libraries.
- Add diagnostics logging for thumbnail load failures (non-fatal).

Build gate:
- `dotnet build KnobForge.sln -v minimal`

Regression checks:
- Repeatedly open/close selector and rebuild list.
- Swap projects and verify no stale thumbnails remain.

## Phase 5: QA Matrix + Rollout
Scope:
- Functional matrix:
  - built-in collars
  - imported collars
  - no thumbnail
  - malformed thumbnail
  - duplicate-like names
- Confirm save/load behavior unaffected by preview-only changes.
- Update docs with naming conventions for user-added thumbnails.

Build gate:
- `dotnet build KnobForge.sln -v minimal`

## Risk Register + Mitigations
- Risk: compiled binding type resolution errors in templates.
  - Mitigation: explicit binding strategy for item templates; validate during Phase 2.
- Risk: bitmap leaks on option refresh.
  - Mitigation: deterministic dispose on rebuild and window shutdown.
- Risk: file watcher event storms.
  - Mitigation: debounce and path filtering.
- Risk: selection resets unexpectedly.
  - Mitigation: preserve by stable key (preset/import path), not list index.

## Definition of Done
- User can visually identify collar options before choosing.
- Missing/corrupt thumbnails never break selector behavior.
- No build regressions; no known memory-lifetime regressions in thumbnail path.
- Documentation includes thumbnail placement and naming rules.
