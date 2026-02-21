# Inspector Overhaul Plan

## Goal
Make the inspector task-first, node-aware, and compact so it feels like a control surface instead of a long document.

## Non-Goals (for this plan)
- No rendering pipeline changes.
- No material/geometry behavior changes.
- No profile format version change unless required by a specific migration.

## Principles
- Scene selection drives inspector content.
- Show essentials first, advanced controls collapsed.
- Keep one control-row pattern: label + knob + numeric input + reset.
- Separate concerns by node type and domain.
- Preserve current functionality while improving structure.

## Target IA
- Tabs: `Node`, `Lighting`, `Environment`, `Shadows`, `Brush`, `Render`.
- `Node` tab is dynamic by selected scene node:
  - `Model`: Transform, Body, Grip, Indicator.
  - `Collar`: Source, Placement, Mirror/Scale, Material.
  - `Material`: Base, Region overrides, Surface aging.
  - `Light`: Type, Position/Direction, Intensity/Falloff, Color.

## Phased Execution

### Phase 1: UX Spec + Control Inventory
- Inventory all existing inspector controls.
- Map each control to a single target section.
- Mark each control as `Essential` or `Advanced`.
- Define section ordering for each node type.

Decision Gate A:
- Choose presentation style:
  1. Card sections in tabbed inspector (recommended).
  2. Dense property grid.
  3. Left section-nav + right detail pane.

Build Gate:
- No code changes required.

### Phase 2: Inspector Shell Refactor (Behavior-Preserving)
- Create reusable section container and control-row templates.
- Build the new tab shell and section hosts.
- Keep existing handlers and bindings; move only layout.

Regression Mitigation:
- Run side-by-side feature flag (`legacy inspector` / `new inspector`) during migration.

Build Gate:
- `dotnet build KnobForge.sln` must pass.

### Phase 3: Node-Aware Routing
- Route scene selection to correct Node inspector view.
- Remove tab-jump behavior and selection/inspector desync.
- Keep undo/redo from changing active inspector context unexpectedly.

Build + Regression Gate:
- Scene selection stability, tab persistence, undo/redo context tests.

### Phase 4: Incremental Section Migration
- Migrate in this order:
  1. Model essentials
  2. Collar
  3. Material
  4. Light
  5. Environment + Shadows
  6. Brush
- Move each section with behavior parity only.

Build Gate after each subsection:
- Build + smoke check the moved section.

### Phase 5: Performance + QoL Polish
- Lazy-create advanced sections.
- Debounce heavy geometry refresh on slider drag.
- Add section reset buttons and clearer readouts.

Build + Perf Gate:
- No regressions in viewport interaction latency.

### Phase 6: Legacy Cleanup
- Remove dead legacy inspector code/controls.
- Normalize naming and ownership boundaries.
- Finalize docs for inspector architecture.

Final Gate:
- Build, save/load, profiles, render, brush, and collar smoke pass.

## Regression Checklist
- Scene tree selection always matches inspector target.
- Undo/redo does not force unrelated tab/content jumps.
- Collar/material/light settings persist correctly through save/load.
- Reference profiles preserve expected sections.
- GPU viewport behavior unchanged by inspector layout changes.

## Deliverables
- This plan document.
- Control inventory matrix (current control -> target section).
- Inspector shell components.
- Migration checklist with pass/fail status per phase.
