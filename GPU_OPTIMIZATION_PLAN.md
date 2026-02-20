# KnobForge: GPU Optimization Plan

**Date:** 2026-02-19

This document outlines the findings from a code-level performance and memory audit and provides a phased plan to optimize 3D rendering paths, address CPU bottlenecks, and fix memory leaks.

---

## 1. Audit Verdict

The audit combined static code analysis and runtime profiling using Instruments (Leaks, Allocations, Metal System Trace).

### Key Findings:

*   **GPU Is Active:** The application correctly uses the Metal rendering backend for the main 3D viewport and for final exports. This is confirmed by runtime traces showing significant Metal activity.
*   **CPU Bottlenecks Identified:** The primary source of latency ("lag") during paint and scratch operations is CPU-bound.
    *   **Hit-Testing:** Pointer-to-UV coordinate mapping is performed on the CPU by iterating through all model triangles for every brush stamp.
    *   **CPU Churn:** The paint hot-path contains unnecessary work, including file timestamp checks and frequent native memory allocations (`Marshal.AllocHGlobal`) within the render loop.
*   **High-Confidence Memory Leak Risk:** There is a likely memory leak of unmanaged GPU resources (`IMTLBuffer`).
    *   The `IMTLBuffer` wrapper has no dispose/release contract.
    *   GPU buffers for meshes are replaced or nulled out during model updates without explicitly releasing the old buffer, leading to orphaned GPU memory.

### Bottom Line:

1.  You are successfully using the GPU for rendering.
2.  Your paint/scratch lag is primarily caused by CPU-bound hit-testing.
3.  There is a high risk of unmanaged GPU buffer memory leaks when the model or its resources are updated.

---

## 2. Target Architecture

The goal is to enforce a strict separation of concerns between the CPU and GPU.

*   **CPU:** Responsible for UI logic (Inspector, Scene Tree), input event wiring, and state management.
*   **GPU:** Exclusively responsible for all 3D rendering, including the viewport, exports, shading, shadows, and texture stamping (paint/scratch).

To achieve this, CPU-based raycasting and per-frame mesh analysis must be eliminated from the interactive rendering paths.

---

## 3. Phased Implementation Plan

The following sequenced plan will be executed to address the audit findings. Each phase is designed to be a testable, shippable checkpoint.

### Phase 0: Baseline + Guardrails
*   **Objective:** Define repeatable performance test cases and capture baseline metrics before making changes.
*   **Gate:** Project builds and all tests pass. Baseline performance (FPS, latency, memory) is captured and saved via `xctrace`.

### Phase 1: GPU Resource Lifetime Fixes (Breaking Change)
*   **Objective:** Implement deterministic cleanup for all GPU resources to fix the memory leak.
*   **Actions:**
    1.  Make the `IMTLBuffer` wrapper `IDisposable`.
    2.  Ensure all Metal buffers are explicitly released when no longer needed.
    3.  Dispose of old mesh and collar GPU resources on rebuild, project swap, or viewport teardown.
*   **Gate:** The application builds, tests pass, and runs correctly. Memory usage no longer climbs after repeated model swaps or resource updates.

### Phase 2: Remove Per-Draw Native Allocation Churn
*   **Objective:** Eliminate frequent native memory allocations from the main render loop.
*   **Actions:** Replace `Marshal.AllocHGlobal`/`FreeHGlobal` calls in uniform upload paths with reusable or stack-backed buffers.
*   **Gate:** The application builds and passes smoke tests. A new allocation trace confirms a significant reduction in transient native allocation rates during painting.

### Phase 3: Remove CPU Work from Paint Hit Hot Path
*   **Objective:** Decouple file I/O and mesh validation from the interactive paint loop.
*   **Actions:** Move mesh resource refreshing and file timestamp checks out of the pointer sampling path. These should only be triggered via a dirty-flag mechanism when the underlying assets change.
*   **Gate:** The application builds and passes interaction tests. Paint/scratch latency drops noticeably, and the UI no longer stalls during mouse drags.

### Phase 4: Fast Picking Interim (CPU BVH)
*   **Objective:** Drastically reduce the CPU cost of raycast hit-testing as an interim solution.
*   **Actions:** Implement a Bounding Volume Hierarchy (BVH) or similar acceleration structure for the CPU-side ray-triangle intersection tests.
*   **Gate:** The application builds and passes performance comparisons against the baseline. The number of triangle intersection tests per paint sample drops massively.

### Phase 5: GPU Picking Path (True 3D-on-GPU)
*   **Objective:** Move the entire paint hit-testing process to the GPU.
*   **Actions:**
    1.  Implement an offscreen rendering pass that writes object ID and UV coordinates to a texture.
    2.  When the user clicks, read the single pixel under the cursor to get the hit information.
    3.  Keep the CPU BVH from Phase 4 as a fallback path if needed.
*   **Gate:** The application builds and passes correctness checks for brush accuracy on all models.

### Phase 6: Stroke Throughput + Render Queue Tuning
*   **Objective:** Optimize the rendering workload during continuous brush strokes.
*   **Actions:**
    1.  Batch brush stamp submissions to the GPU.
    2.  Adapt the density of paint samples based on stroke speed and brush size.
    3.  Ensure shadow and paint passes do not overload the frame time during active painting.
*   **Gate:** The application passes a long-stroke stress test with stable interaction at the target FPS.

### Phase 7: Leak/Perf Certification
*   **Objective:** Run a final audit to verify that all initial goals have been met.
*   **Actions:** Execute the full suite of `xctrace` audits (Allocations, Leaks, Metal System Trace) and compare the results against the Phase 0 baseline.
*   **Gate:** The final Leaks trace shows zero leak rows. Memory usage is stable. Metal trace confirms all 3D rendering paths are on the GPU.

### Phase 8: Hardening + Rollback Safety
*   **Objective:** Ensure each phase is committed as a stable, shippable increment.
*   **Actions:** Land each phase as a separate commit. Add quick smoke-check scripts for core functionality (viewport orbit, paint, scratch, shadows, export) to run after every phase.
*   **Gate:** Every checkpoint builds and runs before work begins on the next phase.
