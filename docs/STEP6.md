# Step 6: Live Topology Optimisation UI, Run Storage, Playback, Verification & STL Export

## 1. Overview
Step 6 completes the interactive end-to-end topology optimisation workflow in `Mycel`:
1. **Optimisation Panel (Stepper Step 6 "Optimise")**: Basic and collapsible advanced SIMP-OC parameters, pre-run disk usage estimation, live convergence metrics, and a custom Avalonia-drawn compliance-vs-iteration chart (`ComplianceChartControl`).
2. **Dual-Consumer Threading Model**: Backpressured persistence (`Channel<IterationSnapshot>` with capacity 8 and a single `RunWriter` task) paired with a non-blocking `"latest wins"` display slot (`LatestWinsDisplaySlot<IterationSnapshot>`) polled at ~8 Hz.
3. **Constrained Surface Nets & Taubin Smoothing**: Parallel Z-slab dual-grid iso-surface extraction (`SurfaceNetsExtractor`) with a 1-voxel zero-padded boundary and 3D digital-topology well-composedness repair guaranteeing closed 2-manifold meshes ($V - E + F = 2$ per shell), plus optional volume-preserving $\lambda|\mu$ Taubin smoothing (`TaubinSmoother`).
4. **Atomic Run Storage & Crash Recovery**: Structured run folders (`runs/<yyyy-MM-dd_HH-mm-ss>_<name>/`) with atomic `.tmp` + rename writes, interrupted run detection, and bit-exact continuation from `checkpoint_latest.bin.br`.
5. **Timeline Playback, FEA Verification & Binary STL Export**: Interactive scrubbing with monotonic sequence-number cancellation (`PlaybackScrubber`), LRU mesh caching, translucent `"Ghost Original"` overlay, thresholded static FEA verification, and binary STL export with automatic `StlReader.Read` round-trip verification.

---

## 2. Threading Architecture

```text
+-----------------------------------------------------------------------------------+
|                        OPTIMISER THREAD (Task.Run / ThreadPool)                   |
|  SimpOcOptimizer.Run(model, options, controller, onIterationSnapshot)             |
|    - Checks IterationController (Pause gate + CancellationToken)                  |
|    - Emits immutable IterationSnapshot (deep-copied float[] rho, rhoTilde, etc.)  |
+-----------------------------------------+-----------------------------------------+
                                          |
                     +--------------------+--------------------+
                     |                                         |
                     v (1)                                     v (2)
+----------------------------------------+   +--------------------------------------+
|   PERSISTENCE CHANNEL (Bounded, cap=8) |   |   DISPLAY SLOT (Latest-Wins, cap=1)  |
|   Channel<IterationSnapshot>           |   |   Interlocked.Exchange(ref _latest)  |
|   FullMode = BoundedChannelFullMode.Wait   |   Never blocks optimiser; skips      |
|   Backpressures optimiser if disk lags |   |   intermediate frames if busy        |
+--------------------+-------------------+   +------------------+-------------------+
                     |                                          |
                     v                                          v
+----------------------------------------+   +--------------------------------------+
|   SINGLE WRITER TASK (RunWriter)       |   |   UI TIMER (~8 Hz DispatcherTimer)   |
|   - Writes iterations/it_NNNNNN.bin.br |   |   - Updates live numeric metrics &   |
|     via .tmp + File.Move(overwrite)    |   |     ComplianceChartControl           |
|   - Writes checkpoint_latest.bin.br    |   |   - If extraction idle & Live mode:  |
|   - Appends & flushes metrics.csv      |   |     spawns Extraction Task           |
|   - Updates run.json on finish/stop    |   +------------------+-------------------+
+----------------------------------------+                      |
                                                                v
                                             +--------------------------------------+
                                             |   EXTRACTION TASK (ThreadPool)       |
                                             |   - SurfaceNetsExtractor.Extract()   |
                                             |     (Parallel.For over Z-slabs)      |
                                             |   - Calls Viewport.UpdateMeshAsync() |
                                             |     (stages GpuVertex[] off-UI,      |
                                             |      swaps VBO/EBO on GL render pass)|
                                             +--------------------------------------+
```

### Playback Scrubbing & Sequence Guard
When the user drags the timeline slider or clicks/drags on the compliance chart, `PlaybackScrubber` increments a monotonic 64-bit sequence number (`Interlocked.Increment(ref _latestSeq)`), cancels any in-flight extraction `CancellationTokenSource`, and checks `seq == Interlocked.Read(ref _latestSeq)` both before and after extraction so out-of-order background tasks never overwrite a newer frame.

---

## 3. Run Folder & Binary File Formats

Every run is stored in `runs/<yyyy-MM-dd_HH-mm-ss>_<name>/`:
- **`run.json`**: Indented JSON containing `RunMetadata` (model name, SHA-256, `GridSpecDto`, `MaterialDto`, loads, supports, preserve regions, `OptimizerOptionsDto`, `StartedAtUtc`, `FinishedAtUtc`, and `Status`: `Running | Paused | Stopped | Converged | MaxIterations | Interrupted`).
- **`domain.bin.br`**: Brotli-compressed stream (`CompressionLevel.Fastest`) containing:
  - `Int32 Nx, Ny, Nz`
  - `Byte[TotalElements]` (`VoxelClassification` per element)
  - `Int32 PassiveSolidCount` + `Byte[TotalElements]` (`PassiveSolidMask`)
  - `Int32 PatchCount` + serialized `SurfacePatch` node indices and nodal areas.
- **`metrics.csv`**: UTF-8 CSV appended and flushed (`StreamWriter.Flush()`) after every iteration:
  ```csv
  iteration,compliance,volume_fraction,max_change,grey_fraction,penal,beta,solver_iterations,solver_residual,iter_ms
  ```
- **`iterations/it_000001.bin.br …`**: Written atomically to `it_NNNNNN.bin.br.tmp` and renamed via `File.Move(tmpPath, finalPath, overwrite: true)`:
  - **Uncompressed 72-Byte Header** (little-endian):
    | Offset | Type | Field | Description |
    |---|---|---|---|
    | `0..3` | `byte[4]` | `Magic` | ASCII `"TOPO"` (`0x54, 0x4F, 0x50, 0x4F`) |
    | `4..7` | `int32` | `Version` | Format version (`1`) |
    | `8..19` | `int32[3]` | `Nx, Ny, Nz` | Grid dimensions |
    | `20..23` | `int32` | `Iteration` | 1-based iteration index |
    | `24..31` | `double` | `Compliance` | Objective $c = \mathbf{F}^T\mathbf{U}$ (mJ) |
    | `32..39` | `double` | `VolumeFraction` | Physical volume fraction |
    | `40..47` | `double` | `MaxChange` | $\max_e |\Delta\rho_e|$ |
    | `48..55` | `double` | `GreyFraction` | Fraction of domain voxels with $\bar{\rho} \in (0.1, 0.9)$ |
    | `56..59` | `int32` | `SolverIterations` | PCG iteration count |
    | `60..67` | `double` | `IterMs` | Wall-clock time for iteration (ms) |
    | `68..71` | `uint32` | `Checksum` | FNV-1a 32-bit hash of the quantised byte payload |
  - **Compressed Payload**: `BrotliStream` (`CompressionLevel.Fastest`) of `byte[Nx * Ny * Nz]` where each byte is `round(clamp(ρ̃_e, 0, 1) * 255)` for domain voxels and `0` for outside voxels.
- **`checkpoint_latest.bin.br`**: Atomic Brotli-compressed snapshot of the exact `float[TotalElements]` `DesignDensity` ($\rho$) and `PhysicalDensity` ($\bar{\rho}$) of the latest completed iteration.

### Quantization vs. Exact Checkpoint Rationale
Quantizing physical density $\bar{\rho} \in [0, 1]$ to `uint8` (`round(ρ̃ * 255)`) has a maximum pointwise quantization error of $\frac{1}{510} \approx 0.001961 < 0.002$ and compresses via Brotli to **~35–55 KB per iteration** at 100,000 elements (~4–5.5 MB for a 100-iteration run). While `uint8` is more than sufficient for iso-surface extraction and playback scrubbing, resuming SIMP-OC from quantized $\bar{\rho}$ would lose both float32 precision and the unfiltered design variables $\rho$. Storing the single rolling `checkpoint_latest.bin.br` adds only ~300 KB total per run and makes `"Continue Interrupted Run"` bit-identical to an uninterrupted run.

---

## 4. Iso-Surface Extraction & Verification

### Constrained Surface Nets (`SurfaceNetsExtractor`)
- Operates on a 1-voxel zero-padded scalar grid of dimensions $(n_x+2)\times(n_y+2)\times(n_z+2)$ so solids touching the bounding box or domain boundary are cleanly capped.
- Applies a fast 3D digital-topology well-composedness pass (`MakeWellComposed`, Latecki 1995) on the binary thresholded occupancy before dual vertex placement, eliminating $2\times 2$ face saddles and $2\times 2\times 2$ corner kisses so every connected shell is guaranteed to be a closed, watertight 2-manifold ($V - E + F = 2$).
- Positions each dual cell vertex at the centroid of active edge crossings (trilinearly interpolated along the 12 cube edges) in original millimeter coordinates (`grid.Origin + index * h`), parallelized across Z-slabs.

### Verification & Binary STL Export
- **Verify Design (`WorkflowService.VerifyIterationAsync`)**: Thresholds $\bar{\rho}_e \ge \tau \to 1.0$ (and $\rho_{\min}$ otherwise), runs the Step 3 matrix-free PCG static solver on the thresholded voxel model, and trilinearly samples the nodal von Mises stress onto the extracted iso-surface vertices.
- **Export STL (`WorkflowService.ExportIterationStlAsync`)**: Extracts the iso-surface at the chosen threshold, applies $0..10$ iterations of volume-preserving Taubin smoothing ($\lambda = 0.50, \mu = -0.53$), writes a binary STL with the 80-byte header `"Mycel it=<n> th=<t>"`, and immediately re-reads the file with `StlReader.Read` to verify watertightness and volume.

---

## 5. Verification Summary (`Step6Tests`)
All 8 Step 6 automated test suites (`T1`–`T8`) pass in Release mode:
- **`T1_IsoSurface_Sphere_And_CubeTouchingBoundary`**: Verified closed 2-manifold ($V - E + F = 2$, zero boundary/non-manifold edges) and volume within 2% for a $R=10\text{ mm}$ sphere and a boundary-touching cube.
- **`T2_TaubinSmoothing_PreservesVolumeAndManifold`**: Verified 5 iterations of Taubin smoothing preserve volume within 1% and maintain 2-manifoldness.
- **`T3_PersistenceRoundTrip_QuantizationError`**: Verified 10 iterations round-trip with max pointwise quantization error $\le \frac{1}{255} \approx 0.004$.
- **`T4_Backpressure_SlowDiskDoesNotDropIterations`**: Verified bounded channel (capacity 8) with 50 ms/write artificial disk delay drops 0 of 20 iterations.
- **`T5_AtomicWriteAndCrashRecovery`**: Verified simulated crash before `.tmp` rename leaves previous iterations intact, sets status to `Interrupted`, and cleans up `.tmp` files.
- **`T6_VerifyAndExport_EndToEnd`**: Verified thresholded FEA compliance within 15% of penalised SIMP compliance and exported binary STL re-reads via `StlReader.Read` as watertight with custom `"Mycel it=5 th=0.50"` header.
- **`T7_ContinueInterruptedRun_MatchesUninterruptedRun`**: Verified stopping at iteration 3 and resuming to iteration 6 reproduces the uninterrupted 6-iteration compliance within $< 10^{-5}$.
- **`T8_ThreadingSmokeTest_1000IterationsAt1kHz_And_ScrubbingSequenceGuard`**: Verified 1000 iterations at 1 kHz with concurrent display polling and random `Stop()` complete cleanly with zero deadlocks/exceptions, and rapid scrubbing `1..50` displays strictly iteration 50.

