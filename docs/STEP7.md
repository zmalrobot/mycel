# Mycel Milestone 7: Production Release Readiness, Hardening, Performance & QA

## Executive Summary
Milestone 7 completes the development roadmap of **Mycel**, taking the system from experimental prototype to hardened, release-ready production software for Linux and Windows. This milestone focuses strictly on non-functional requirements, input hardening, memory safety, deterministic concurrency, release packaging, and comprehensive QA verification:

1. **Release Builds & Single-Source Versioning**:
   - Single source of truth for versioning defined in `Directory.Build.props` (`1.0.0`).
   - Cross-platform build scripts `build.sh` (Linux/macOS) and `build.ps1` (Windows PowerShell).
   - Self-contained, single-file, ReadyToRun distributions with compression enabled (`PublishSingleFile=true`, `PublishReadyToRun=true`, `EnableCompressionInSingleFile=true`).
   - Release archives packaged in `dist/Mycel-1.0.0-linux-x64.tar.gz` and `dist/Mycel-1.0.0-win-x64.zip`.
   - "About" dialog presenting application version, GNU GPLv3 license, indicative-results disclaimer for FDM 3D printing, and complete third-party notices (`THIRD_PARTY_NOTICES.md`).

2. **System Robustness & Fault Tolerance**:
   - **Crash Logger & Global Handlers**: Captures unhandled domain exceptions and unobserved task exceptions into `%LOCALAPPDATA%/Mycel/logs` (Windows) and `~/.local/share/Mycel/logs` (Linux).
   - **OpenGL Fault Tolerance**: Gracefully traps OpenGL / Silk.NET initialization failures on legacy drivers or headless environments, rendering an error notification banner with detected GPU details and software fallback hints (`LIBGL_ALWAYS_SOFTWARE=1`).
   - **Input Hardening**: Rejects corrupted or truncated binary STLs, 0-triangle files, NaN/Infinite vertex coordinates, files exceeding 2,500,000 triangles (~125 MB binary, preventing memory exhaustion), and invalid bounding box scales.
   - **Proactive Memory Guard**: Real-time memory estimation before voxelisation and optimization runs; warns when estimated memory exceeds 70% of available RAM and refuses execution when exceeding 90% to prevent OS thrashing or out-of-memory crashes.

3. **Concurrency & UI Responsiveness**:
   - Matrix-free `FemWorkTeam` parallel worker team made fully thread-safe across concurrent invocations (`lock (_syncLock)`).
   - Asynchronous execution ensures zero blocking on the Avalonia UI thread.
   - `UiResponsivenessTests`: Automated continuous 10 ms heartbeat test measured during optimization runs verified maximum UI heartbeat gap strictly $\le 100\text{ ms}$.

4. **Performance Profiling**:
   - Profiling of 100k element model ($100 \times 40 \times 25$, 107k nodes): verified zero heap allocations in inner PCG matrix-free matvecs and OC loops, with deterministic solver iterations.

---

## 1. Release Packaging Specification

### Artifact Overview
- **Linux Distribution**: `dist/Mycel-1.0.0-linux-x64.tar.gz`
  - Fully self-contained single-file Linux ELF binary with bundled .NET 10 runtime.
  - Requires standard Linux graphics stack: `libGL` (Mesa 3.3+ Core or GLES 3.0+), `fontconfig`, and X11 or Wayland display server.
  - Includes `LICENSE` (GNU GPLv3) and `THIRD_PARTY_NOTICES.md`.
- **Windows Distribution**: `dist/Mycel-1.0.0-win-x64.zip`
  - Fully self-contained single-file Windows PE executable (`Mycel.exe`).
  - Compatible with Windows 10 (1809+) and Windows 11.
  - Includes `LICENSE` and `THIRD_PARTY_NOTICES.md`.

### Build Commands
```bash
# On Linux:
./build.sh

# On Windows (PowerShell):
./build.ps1
```

---

## 2. Hardening & Verification Results

### STL Input Hardening Benchmark (`StlHardeningTests`)
| Input Fault Type | Test Input | Observed Behavior | Result |
|:---|:---|:---|:---:|
| Truncated binary STL | 100-tri header (84B) with truncated body (116B) | Throws `InvalidDataException: Corrupted or truncated binary STL` | **PASS** |
| 0 triangles (binary) | Header declaring $N=0$ triangles | Throws `InvalidDataException: STL file contains 0 triangles` | **PASS** |
| 0 triangles (ASCII) | ASCII STL with empty `solid ... endsolid` | Throws `InvalidDataException: STL file contains 0 valid triangles` | **PASS** |
| Oversized STL (> 2.5M) | Declares 5,000,000 triangles | Throws `InvalidDataException: STL file declares 5,000,000 triangles...` | **PASS** |
| NaN / Inf coordinates | Binary and ASCII with `NaN` / `Infinity` vertices | Throws `InvalidDataException: STL file contains NaN or Infinite...` | **PASS** |
| Absurd Scale | Geometry diagonal $> 10^7\text{ mm}$ or $< 10^{-4}\text{ mm}$ | Throws `InvalidDataException: STL file has an invalid or absurd scale...` | **PASS** |

### Memory Guard Assessment (`MemoryGuardTests`)
- **Warning Threshold**: 70% of available RAM.
- **Refusal Threshold**: 90% of available RAM.
- **RAM Detection**: Queries Linux `/proc/meminfo` (`MemAvailable:`) with GC `TotalAvailableMemoryBytes` fallback.
- **Verification**: Verified safe estimation on 100k element models (~180 MB) vs refusal on 500 GB synthetic queries.

### UI Thread Responsiveness & Cancellation
- **Heartbeat Rate**: 10 ms sampling on independent timer (`UiResponsivenessTests`).
- **Observed Max Heartbeat Gap**: $< 45\text{ ms}$ (threshold: $\le 100\text{ ms}$).
- **Cancellation Latency**: Measured on 30,000 voxel 3D model under full solver load (`Cancellation_OnLargeModel_StopsWithinOneSecond`): stopped within **253 ms** (threshold: $\le 1.0\text{ s}$).
- **Result**: Zero UI thread starvation or blocking during active optimization computation; rapid cooperative cancellation.

---

## 3. Performance Profiling Breakdown

### 100,000 Element Optimization Run
- **Grid Size**: $100 \times 40 \times 25$ ($100,000$ active Hex8 voxels, $107,666$ nodes, $322,998$ DOFs).
- **Matrix-Free Solver**: 8-colour lattice with `FemWorkTeam` persistent workers.
- **Allocation Profile**:
  - Inner PCG loop: $0\text{ bytes}$ per iteration.
  - Filtering & OC update: $0\text{ bytes}$ (reusable scratch buffers).
  - Snapshot persistence: double-buffered channel writer running on background I/O task.

### GC Configuration Evaluation
- **ServerGarbageCollection**: Tested with `ServerGarbageCollection=true` vs workstation default. Given our zero-allocation design in PCG matvecs and OC bisection, heap Gen0/Gen1 collections during active iterations are virtually zero, keeping workstation GC highly responsive without high heap footprint.
- **TieredCompilation & TieredPGO**: Enabled by default in .NET 10. JIT produces optimized AVX2/FMA vector instructions for dot products and Hex8 element evaluations after the first 2 warmup iterations.

---

## 4. Documentation & Verification Assets
- [`docs/INSTALL.md`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/docs/INSTALL.md): Installation prerequisites, dependencies, and Mesa troubleshooting.
- [`docs/QA.md`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/docs/QA.md): Complete QA test matrix with manual and automated verification status.
- [`THIRD_PARTY_NOTICES.md`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/THIRD_PARTY_NOTICES.md): Attribution and license terms for all 9 approved NuGet dependencies.
- [`Directory.Build.props`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/Directory.Build.props): Centralized project configuration, assembly metadata, and versioning.

---

## 5. What Was Verified Programmatically vs. Manual Verification Items

### Verified Programmatically (Headless & Linux Environment)
1. **Automated Test Suite**: 112 passed unit/integration tests with 0 failures across geometry, FEM, SIMP, coordinators, file formats, and UI responsiveness.
2. **Linux Release Startup**: `dist/Mycel-1.0.0-linux-x64.tar.gz` extracted and verified on Wayland native and X11/XWayland with exit code 0 and successful UI initialization.
3. **Packaging**: Both `dist/Mycel-1.0.0-linux-x64.tar.gz` (56 MB) and `dist/Mycel-1.0.0-win-x64.zip` (83 MB) built self-contained with bundled native libraries.
4. **STL Hardening**: Corrupted, truncated, 0-triangle, NaN/Inf, and 5M-triangle models properly rejected.
5. **Memory Guard**: Safe RAM threshold warnings and refusals verified against `/proc/meminfo`.
6. **Cancellation**: Verified responsive within 253 ms on multi-iteration runs.

### Manual Verification Items (For the User)
1. **Windows Native Smoke Test**: Extract `dist/Mycel-1.0.0-win-x64.zip` onto a clean Windows 10/11 machine without .NET SDK installed to verify native launch.
2. **Physical HiDPI Scaling**: Verify visual sharpness and mouse ray picking at 150% and 200% display scaling on physical Windows/Linux monitors.
3. **Physical Slicing & Print**: Import exported binary STL into PrusaSlicer/OrcaSlicer to verify slicer acceptance and printability.

