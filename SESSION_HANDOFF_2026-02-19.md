# KnobForge Session Handoff (2026-02-19)

## What was completed

### 1) Imported STL snake collar workflow
- Imported STL collar is active and adjustable from Model -> Collar.
- Default front-face fix applied for STL:
  - `InvertImportedStlFrontFaceWinding = true`
- Added/kept controls for:
  - imported scale, body length/thickness, head length/thickness
  - imported rotation
  - imported XY offsets
  - inflate

### 2) Lighting/culling stability
- Knob and imported STL winding controls are separated in viewport context menu.
- Depth/culling issues were stabilized in the current path.

### 3) 3D surface painting (literal brush painting)
- Painting now uses pointer ray -> mesh triangle hit (knob + collar), not only flat screen projection.
- Fallback mapping is present if ray hit fails (prevents “painting does nothing” cases).
- Paint mask follows model rotation and exports through the same mask texture pipeline.

### 4) Paint channels and look tuning
- Channels:
  - Rust
  - Wear
  - Gunk
  - Scratch (new)
  - Erase
- Added `Brush Darkness` slider (global intensity character control).
- Tuning updates:
  - Wear less aggressive.
  - Rust more splotchy / varied (red-orange-brown mix).
  - Gunk darker / more matte.

### 5) Scratch system (carve behavior)
- Scratch now has dedicated controls:
  - Scratch Width (px)
  - Scratch Depth
  - Scratch Drag Resistance
  - Option Drag Depth Ramp
- Drag behavior:
  - scratch stroke follows cursor with resistance (lag).
  - hold Option/Alt while dragging to ramp scratch depth upward.
- Scratch mask is stored in paint mask alpha channel.
- Vertex shader displaces geometry inward using scratch mask (carved look in viewport).

---

## Important current limitation

- **Scratch carve displacement is currently GPU viewport vertex-displacement only.**
  - CPU preview/export path has scratch shading impact but does not yet mirror full geometric carve depth exactly.
  - If exact viewport/export carve parity is required, mirror scratch displacement in CPU render path or export through GPU-only path for final frames.

---

## Files touched (main)

- `KnobForge.Core/KnobProject.cs`
  - paint channels, brush darkness, scratch params, RGBA paint mask sampling/writes
- `KnobForge.App/Controls/MetalViewport.cs`
  - 3D ray-hit painting
  - paint fallback mapping
  - scratch drag resistance + depth ramp behavior
  - uniform packing updates
  - vertex texture binding for scratch carve
- `KnobForge.Rendering/GPU/MetalPipelineManager.cs`
  - rust/wear/gunk tuning
  - scratch carve displacement in vertex shader
- `KnobForge.Rendering/PreviewRenderer.cs`
  - weather tuning parity (non-geometry parts)
- `KnobForge.App/Views/MainWindow.axaml`
  - Brush tab controls for darkness + scratch controls
- `KnobForge.App/Views/MainWindow.axaml.cs`
- `KnobForge.App/Views/MainWindow.Initialization.cs`
- `KnobForge.App/Views/MainWindow.PaintBrushHandlers.cs`
- `KnobForge.App/Views/MainWindow.EnvironmentShadowReadouts.cs`

---

## Quick resume checklist (next session)

1. Validate scratch carve parity in export (GPU vs CPU path).
2. Decide whether scratch should affect:
   - only normal-facing/front surfaces
   - or all hit geometry including underside.
3. Add optional scratch profile hardness (CAD-like V vs rounded groove).
4. Continue requested realism pass for snake head shape + material.
5. Revisit “knob grips are messed up now” report (pending full audit).

---

## Build/run

```bash
dotnet build KnobForge.sln
dotnet run --project KnobForge.App
```

Current solution builds successfully (warnings only in `KnobExporter` about obsolete Skia filter quality API).
