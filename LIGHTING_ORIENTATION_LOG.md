# Lighting / Camera / Gizmo Orientation (Locked)

This file defines the canonical orientation contract for KnobForge.
These values are intentionally locked and must not be changed without an explicit migration.

## Locked Conventions

- Gizmo orientation inversion:
  - `InvertX = true`
  - `InvertY = true`
  - `InvertZ = true`
- Camera orientation:
  - `FlipCamera180 = true`
- Default first light:
  - `X = 200`
  - `Y = -200`
  - `Z = -400`

## Source Anchors

- Locked orientation definition:
  - `KnobForge.Rendering/PreviewRenderer.cs`
- Preview camera basis uses the locked camera flip:
  - `KnobForge.Rendering/PreviewRenderer.cs`
  - `KnobForge.App/Controls/ViewportControl.cs`
- Export camera is aligned to the same front-facing convention:
  - `KnobForge.Rendering/KnobExporter.cs`
- Default light initialization:
  - `KnobForge.Core/KnobProject.cs`

## Runtime Mutability Policy

- Right-click orientation debug toggles were removed from `ViewportControl`.
- Orientation values are no longer runtime-editable through UI.
- Z slider now maps directly to world Z (no UI-side sign inversion).

## Do Not Change Without Re-Validation

Do not modify any of the following unless you intentionally perform a full orientation migration:

- Camera forward sign or flip behavior
- Gizmo axis inversion mapping
- Export camera basis
- Default light-side convention

If you intentionally change orientation conventions later, update this file in the same change.
