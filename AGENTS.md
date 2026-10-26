# TopoOpt Agent Development Rules

## 0. Communication & Task Boot Protocol
- **Language**: Talk to the user in Italian. Write code, identifiers, code comments, commit messages, and documentation in English.
- **Task Startup Protocol**: At the start of every task, read:
  1. `AGENTS.md`
  2. `docs/ARCHITECTURE.md`
  3. `docs/DECISIONS.md`
  4. The latest `docs/STEPn.md` (where `n` is the most recent completed step).

## 1. Physical Units & Coordinate Conventions
- Units: **mm**, **N**, **MPa** ($1\text{ MPa} = 1\text{ N/mm}^2$).
- Coordinates: Right-handed system, **Z up** (Z = print build direction).
- Structured Indexing over Bounding Box:
  - Element index: `e = ix + nx * (iy + ny * iz)` where `ix ∈ [0, nx-1], iy ∈ [0, ny-1], iz ∈ [0, nz-1]`.
  - Node index: `n = i + (nx + 1) * (j + (ny + 1) * k)` where `i ∈ [0, nx], j ∈ [0, ny], k ∈ [0, nz]`.
  - Node DOFs: `dof = 3 * n + {0, 1, 2}` representing $(u_x, u_y, u_z)$.
  - Active elements: Flat contiguous array of active element bounding-box indices.
- Surface Mapping & Boundary Conditions:
  - Surface triangles map to Hex8 nodes via volume dilation layer ($\delta = 1.0 \times h$).
  - Region selection supports 3 types: `Fixed Support`, `Applied Load` (in Newtons with Normal/Axis/Custom direction), and `Preserve (Keep Solid)`.
  - A 1-voxel protective passive shell ($\rho \equiv 1$) is enforced around loads and supports by default to prevent numerical point hinges.

## 2. Core Architecture & Numerical Rigor
- `TopoOpt.Core` MUST NOT reference any UI framework (no Avalonia, no WPF, no Silk.NET, no OpenGL).
- Deterministic Numerics: Fixed reduction order in parallel loops (e.g. dot products, norm calculations).
- Zero Allocations in Hot Loops: Pre-allocate all scratch buffers, vectors, and matrices outside PCG and OC iteration loops.
- License & Dependencies:
  - Project License: **GNU General Public License v3.0 (GPLv3)**.
  - Authorized NuGet Packages only: `Avalonia`, `CommunityToolkit.Mvvm`, `Silk.NET.OpenGL`, `ScottPlot.Avalonia`, `Material.Icons.Avalonia`, `xunit`.
  - No new NuGet package may be added without prior explicit user approval in `docs/DECISIONS.md`.

## 3. Specification & Verification Protocol
- **Tests are the specification. Never weaken, delete or skip a test to make it pass; if a test looks wrong, stop and ask me.**
- Code without passing automated tests is considered broken and unverified.
- All physics and numerical algorithms (Hex8, SIMP, PCG, filters, Surface Nets) must have unit test benchmarks against known analytical solutions or published literature values within defined tolerances.
- Benchmark separation: Heavy benchmark tests (e.g., 100k+ elements) must be tagged with `[Trait("Category", "Benchmark")]` so default test execution (`--filter "Category!=Benchmark"`) runs fast.

## 4. Concurrency & UI Responsiveness
- Long-running tasks (FEM solves, topology optimization, mesh voxelisation) must accept `CancellationToken` and `IProgress<T>`.
- The UI thread must never be blocked by computation. All heavy jobs run asynchronously on the thread pool.
- Optimization iteration frames are saved every $N=5$ iterations by default (plus frame 0 and final) into `.topo` Brotli-compressed streams.

## 5. Development Cadence & Milestone Artifacts
- Commit after each green milestone.
- Write a retrospective/verification summary in `docs/STEPn.md` at the end of every roadmap step.

## 6. Robustness, Hardening & Concurrency Invariants
- **Thread-Safety of Shared Persistent Workers**: Shared singletons managing background thread loops (such as `FemWorkTeam.Shared`) must be fully thread-safe (`lock` around active dispatch loops) to prevent race conditions during concurrent test executions.
- **Avalonia XAML Number Formatting**: Never use scientific notation (e.g., `1e-12`, `1e-3`) in XAML attributes binding to `decimal` or `NumericUpDown.Minimum`/`Maximum`; always use explicit expanded decimal strings (e.g., `0.000000000001`, `0.001`).
- **Memory Guard Invariants**: Voxelisation and optimization routines must verify estimated heap/scratch memory against available system RAM (`/proc/meminfo` or GC fallback). Warn at $> 70\%$ available RAM and refuse execution at $> 90\%$ with actionable guidance.
- **STL Input Sanitization**: Reject files with 0 triangles, non-finite coordinates (NaN/Inf), truncated binary headers, or triangle counts exceeding 2,500,000 triangles to prevent denial of service and heap exhaustion.
- **Defensive Graphics Fallback**: Catch OpenGL/GLES context initialization failures cleanly without unhandled crashes, surfacing diagnostic messages and software rendering instructions (`LIBGL_ALWAYS_SOFTWARE=1`).
- **3D Viewport Render Invalidation**: After ANY camera modification (orbit, pan, zoom, fit to view) or visual state update in a render-on-demand viewport, explicitly call `RequestNextFrameRendering()`. Never let an input action complete without marking a frame request.
- **No-Swallow Render Exceptions**: Never swallow OpenGL render callback exceptions. Wrap `OnOpenGlRender` in a `try/catch` and log errors with full stack trace to both console and platform diagnostics logs (`CrashLogger.LogDir`).
- **Custom OpenGL Hit-Testing**: Any `OpenGlControlBase` derived control that handles pointer events must implement `Avalonia.Rendering.ICustomHitTest` (`HitTest(Point point) => new Rect(Bounds.Size).Contains(point);`) because native GL surfaces lack Avalonia `DrawingContext` drawing and will otherwise fail hit-testing.
