# Step 8 Audit: UI Responsiveness, Operation Runner & Parallel Optimization

## 1. Executive Summary & Diagnostic Instrumentation

In accordance with Phase A requirements, diagnostic probes and an automated scenario harness were developed and executed to empirically quantify latency gaps on the Avalonia UI thread:

1. **`UiResponsivenessMonitor`** (`src/TopoOpt.App/Diagnostics/UiResponsivenessMonitor.cs`):
   - High-resolution `DispatcherTimer` (25 ms tick) running on the Avalonia UI thread.
   - Measures inter-tick wall clock intervals using `Stopwatch.GetTimestamp()`.
   - Records any interval $> 100\text{ ms}$ with timestamp, active operation name, and duration to `~/.local/share/TopoOpt/logs/ui_gaps.log` and console.
   - Binds `MaxGapMs` to the status bar in debug builds.

2. **`RenderLoopProbe`** (`src/TopoOpt.App/Diagnostics/RenderLoopProbe.cs`):
   - Inspects frame invalidations vs frame renders in `Viewport3DControl`.
   - Differentiates:
     - **`Idle (on-demand)`**: No frame requested for $> 600\text{ ms}$, 0 FPS expected by design.
     - **`Active (XX FPS)`**: Active user manipulation (orbit, pan, zoom) or simulation playback.
     - **`STALLED (XX ms)`**: A frame was requested via `RequestNextFrameRendering()`, but `OnOpenGlRender()` has not been invoked for $> 100\text{ ms}$.

3. **Clarification of "0 FPS" Status Bar Phenomenon**:
   - In Avalonia `OpenGlControlBase`, rendering is **on-demand by design**: frames are only rendered when `RequestNextFrameRendering()` is explicitly invoked (mouse drag, camera navigation, model loading, live iteration display).
   - When the user is idle, no render frames are requested to minimize GPU/CPU utilization (0% GPU idle).
   - The status bar now displays `Idle (on-demand)` when stationary, switching dynamically to `60 FPS` during camera movement, and flagging `STALLED` if a requested render is ever delayed.

4. **`ScenarioRunner` & Automated In-Window Test** (`src/TopoOpt.App/Diagnostics/ScenarioRunner.cs`):
   - Automated CLI scenario invocation via `--scenario <name>` (e.g. `--scenario obj_1_Assemblaggio --exit-after-scenario`).
   - Drives the full workflow programmatically at the ViewModel layer on `examples/obj_1_Assemblaggio.stl` (3,112 triangles, $44.0 \times 45.4 \times 47.5\text{ mm}$, watertight).

---

## 2. Anti-Pattern Code Audit (Core & App)

| # | Anti-Pattern Category | Finding / Location | Severity | Description |
|---|-----------------------|-------------------|----------|-------------|
| 1 | **`.Result` / `.Wait()` / `GetAwaiter().GetResult()`** | [`src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs:308`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs#L308) | **High** | `channel.Writer.WriteAsync(snapshot, ...).AsTask().GetAwaiter().GetResult();` synchronously blocks the optimizer thread while pushing live snapshots. Must be replaced with non-blocking try-write or async dispatch. |
| 2 | **Synchronous `Dispatcher.UIThread.Invoke` from workers** | *None found* | Pass | Only `Dispatcher.UIThread.Post` is used (`Viewport3DControl.cs:231`, `Viewport3DControl.cs:1237`). No synchronous deadlock risks on UI dispatch. |
| 3 | **`async void` outside event handlers** | [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1198`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1198)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1443`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1443) | Low | `OnAutosaveTick` and `OnLiveDisplayTimerTick` are `DispatcherTimer` tick handlers (`EventHandler`). To improve robustness, internal exceptions are trapped and logged. |
| 4 | **Heavy work in constructors, property setters, or bindings** | [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:756`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L756)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:767`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L767) | Medium | Changing `SelectedResolutionPreset` or `TargetSolidElements` triggers `Workflow.SetResolution(...)` synchronously which invalidates prepared models and autosaves. |
| 5 | **Heavy calls reachable synchronously from click handlers** | [`src/TopoOpt.App/Views/MainWindow.axaml.cs:456`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/Views/MainWindow.axaml.cs#L456)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:575`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L575) | **High** | `OnViewportPointerClicked` synchronously performs BVH raycasting (`bvh.RayCast`) and BFS region growing (`selector.GrowRegion`) on the UI thread before calling `ModifyActiveSelectionTriangles`. |
| 6 | **Per-mouse-move work on UI thread** | [`src/TopoOpt.App/Views/MainWindow.axaml.cs:368-419`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/Views/MainWindow.axaml.cs#L368-L419) | **Critical** | `OnViewportPointerMoved` executes `bvh.RayCast` and full BFS `selector.GrowRegion` on EVERY mouse move event when picking is active. For 100k–1M triangle meshes, this induces persistent UI latency spikes $> 100\text{ ms}$. |
| 7 | **`ObservableCollection` item-by-item additions** | [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:810`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L810) | Low | `ValidationIssues.Add(issue)` called in loop. Collections are currently small ($< 10$ items), but should use batched assignment or bulk reset. |
| 8 | **Locks held across UI calls** | [`src/TopoOpt.App/Controls/Viewport3DControl.cs:220-229`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/Controls/Viewport3DControl.cs#L220-L229) | Medium | `_pendingMeshTcs` inside `UpdateMeshAsync` was awaited by ViewModels without timeout; if the window is occluded or render loop paused, callers await indefinitely. |
| 9 | **`Thread.Sleep` or busy spin waits** | [`src/TopoOpt.Core/Fem/FemWorkTeam.cs:50`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.Core/Fem/FemWorkTeam.cs#L50) | Pass | Persistent workers use OS kernel primitives (`ManualResetEventSlim.Wait()`) rather than busy spinning or `Thread.Sleep`. |
| 10 | **GL calls off render thread** | *None found* | Pass | All Silk.NET OpenGL calls are strictly confined to `OnOpenGlInit`, `OnOpenGlRender`, `OnOpenGlDeinit`, and `SyncPendingGpuUploads` inside `Viewport3DControl`. |
| 11 | **Render callback work scaling with mesh size** | [`src/TopoOpt.App/Controls/Viewport3DControl.cs:907-917`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/Controls/Viewport3DControl.cs#L907-L917) | Low | `gl.BufferData` uploads vertex arrays staged in background. Vertex packing occurs off the render thread, only GPU transfer occurs during render. |

---

## 3. Operations Measurement Table

Measurements taken in live OpenGL window on test model `examples/obj_1_Assemblaggio.stl` (3,112 triangles):

| User Operation | Wall Time | Execution Thread | Max UI Gap | Root Cause / Performance Characteristic |
|---|---|---|---|---|
| **1. Open STL** | 96.7 ms | Background (`Task.Run`) | **0.0 ms** | Fast parsing & BVH construction in background; UI remains completely responsive. |
| **2. Region Selection (Load & Support)** | 27.1 ms | UI Thread | **130.0 ms** | **Root cause:** Synchronous glyph generation (`RefreshBoundaryGlyphs`) and 3D arrow mesh generation for every node in patch executed on UI thread. |
| **3. Material Selection** | 1.5 ms | UI Thread | **0.0 ms** | Direct property update and workflow synchronization; negligible latency. |
| **4a. Prepare (Draft, 50k solid)** | 212.5 ms | Background (`Task.Run`) | **0.0 ms** | Parallel voxelization and boundary condition patch assembly off-thread. |
| **4b. Prepare (Fine, 250k solid)** | 491.8 ms | Background (`Task.Run`) | **0.0 ms** | High resolution grid voxelization off-thread; non-blocking. |
| **4c. Prepare (Standard, 100k solid)** | 213.6 ms | Background (`Task.Run`) | **0.0 ms** | Standard resolution preparation off-thread; non-blocking. |
| **5. Static Analysis (FEA, 316k DOFs)** | 70,146.5 ms (70.1 s) | Background (`Task.Run`) | **0.0 ms** | Multi-threaded PCG solver running on worker team; UI thread completely free. |
| **6. Topology Optimisation (20 iters)** | 711,765.3 ms (~35.5 s/iter) | Background (`Task.Run`) | **295.7 ms** | **Root cause:** Live snapshot extraction timer (`_liveDisplayTimer` @ 125 ms) polling and extracting iso-mesh while FEM worker threads execute PCG iterations, creating thread contention and UI message pump delays. |
| **7. Verify Design (Thresholded FEA)** | ~35,000 ms (35.0 s) | Background (`Task.Run`) | **0.0 ms** | Solves 0/1 binary density field and extracts final Surface Nets iso-surface. |
| **8. Export STL** | 2.6 ms | Background (`Task.Run`) | **0.0 ms** | Binary STL streaming off-thread. |

---

## 4. Scalability Projections for Stress Meshes

| Triangle Count | Open STL & BVH | Hover / Picking Raycast + Flood Fill | Prepare (Standard) | Static Analysis | Expected UI Gap without Phase B/C Fix |
|---|---|---|---|---|---|
| **3,112** (Base) | 96.7 ms | $< 1\text{ ms}$ | 213 ms | 70.1 s | 130.0 ms (Glyph sync) |
| **~100,000** | ~450 ms | 15–35 ms | ~850 ms | 70.1 s | 130–180 ms |
| **~500,000** | ~2,100 ms | 80–150 ms | ~2,800 ms | 70.1 s | **150–250 ms (Severe UI freeze on mouse move)** |
| **~1,000,000** | ~4,200 ms | 180–350 ms | ~5,500 ms | 70.1 s | **350–500 ms (Unresponsive viewport interaction)** |

---

## 5. Remediation Plan & Phase B/C Architecture

To fulfill the Step 8 objectives (UI thread gap strictly $< 100\text{ ms}$, cooperative cancellation, smooth viewport interaction, and $\ge 1.3\times$ speedup):

### Phase B: Unified `IOperationRunner` & UI Offloading
1. **`IOperationRunner` Implementation**:
   - Single entry point for all long-running tasks with `RunAsync(...)`.
   - Unified progress card overlay appearing only if operation takes $> 250\text{ ms}$ (smooth fade, no flicker).
   - Unified cancellation token and non-blocking state recovery.
2. **Hover & Picking Offloading**:
   - Offload `bvh.RayCast` and `selector.GrowRegion` to background channel/throttler (`Channel<Vector2>` or 30 Hz debounced task) so pointer move events NEVER block the UI thread.
   - Background BVH caching to avoid re-instantiation on project load.
3. **Glyph & Overlay Generation Offloading**:
   - Compute boundary condition arrow meshes and glyph geometry in background tasks before posting GPU uploads to `Viewport3DControl`.

### Phase C: Selective Multithreading ($\ge 1.3\times$ Speedup, Bit-Identical)
1. **Thread Team Configuration**:
   - Configure thread count $N = \max(1, \text{Environment.ProcessorCount} - 1)$ to always reserve at least one physical core for the UI thread and render loop.
2. **Parallel Candidates**:
   - Surface Nets iso-surface extraction (`SurfaceNetsExtractor.cs`).
   - Sensitivity filtering and mesh dilation.
   - Voxel raycasting / surface triangle-box intersection passes.
3. **Determinism Verification**:
   - Bit-identical result verification across single-threaded vs multi-threaded execution.

---

> [!IMPORTANT]
> **Phase A is complete.**
> As instructed, I am stopping here for your review and OK on `docs/STEP8_AUDIT.md` before proceeding to implement Phase B, Phase C, and Phase D.

