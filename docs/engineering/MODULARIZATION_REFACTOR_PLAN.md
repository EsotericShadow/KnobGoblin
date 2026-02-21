# KnobForge Modularization Refactor Plan

## Objective
- Reduce God files and mixed responsibilities.
- Enforce maintainability constraints:
  - Any `.cs` file over `1000` LOC must be decomposed.
  - Target module file size: `400-840` LOC where practical.
  - Preserve behavior and rendering parity while refactoring.

## Baseline (2026-02-21)
- Files scanned: `48`
- `>1000 LOC`: `9`
- `>840 LOC`: `11`
- Worst offenders:
  - `KnobForge.App/Controls/MetalViewport.cs` (`7189`)
  - `KnobForge.Rendering/PreviewRenderer.cs` (`2082`)
  - `KnobForge.Rendering/GPU/ImportedStlCollarMeshBuilder.cs` (`1638`)
  - `KnobForge.App/Views/MainWindow.axaml.cs` (`1537`)
  - `KnobForge.App/Views/RenderSettingsWindow.axaml.cs` (`1679`)

## Decision/Build Gate Policy
1. Decision gate before each structural split:
   - present options, tradeoffs, and recommendation.
2. Build gate after each breaking move:
   - `dotnet build KnobForge.sln`
3. Memory/resource gate after each major phase:
   - allocation + leak trace (macOS `xctrace`).
4. Regression smoke gate:
   - orbit/pan/zoom, gizmos, paint/scratch, export, project load/save.

## Phase Plan

### Phase 0: Guardrails and Instrumentation
- Add/refine scripts:
  - LOC audit
  - build + optional leak/alloc gate
- Record baseline output before code movement.

### Phase 1: Decompose `MetalViewport` (highest risk reducer)
- Create folder module at:
  - `KnobForge.App/Controls/MetalViewport/`
- Move code into partials by responsibility:
  - `MetalViewport.Input.cs`
  - `MetalViewport.Camera.cs`
  - `MetalViewport.PaintScratch.cs`
  - `MetalViewport.Resources.cs`
  - `MetalViewport.Render.cs`
  - `MetalViewport.OffscreenExport.cs`
  - `MetalViewport.Gizmos.cs`
  - `MetalViewport.Diagnostics.cs`
- Keep one thin entry file.

### Phase 2: Rendering/Export orchestration
- Decompose:
  - `KnobForge.Rendering/KnobExporter.cs`
  - `KnobForge.Rendering/GPU/MetalPipelineManager.cs`
- Separate:
  - validation/config
  - frame generation
  - downsample/sheet composition
  - output persistence
  - pipeline cache/resource lifecycle

### Phase 3: Geometry/Mesh builders
- Decompose:
  - `KnobForge.Rendering/GPU/ImportedStlCollarMeshBuilder.cs`
  - `KnobForge.Rendering/GPU/MetalMesh.cs`
  - `KnobForge.Rendering/GPU/OuroborosCollarMeshBuilder.cs`
- Separate:
  - source loading
  - topology generation
  - deformation/scaling
  - normals/tangents
  - bounds/reference radius

### Phase 4: UI orchestration cleanup
- Decompose:
  - `KnobForge.Rendering/PreviewRenderer.cs`
  - `KnobForge.App/Views/RenderSettingsWindow.axaml.cs`
  - `KnobForge.App/Views/MainWindow.axaml.cs`
- Preserve user behavior; focus on responsibility isolation.

### Phase 5: Enforcement
- Run LOC audit and confirm no file exceeds `1000`.
- Keep module files near `400-840` LOC unless intentionally small.
- Run full build + memory/resource gate + smoke gate.

## Refactor Safety Rules
- Mechanical moves first, behavior changes second.
- Keep signatures stable unless explicitly gated and approved.
- Prefer composition for cross-cutting logic.
- Keep GPU resource ownership explicit (`IDisposable` boundaries).
- Eliminate duplication only when tests/build/smoke remain green.

## Current Decision Gate 1 (MetalViewport split strategy)
- Option A (recommended): staged partial-class decomposition first.
  - Lowest regression risk; easiest diff review.
- Option B: immediate service extraction (`InputController`, `RenderPassOrchestrator`, etc.).
  - Better architecture faster, but high regression surface.
- Option C: hybrid.
  - Partial split first, then extract services in a second pass.

Recommendation: **Option C**.
