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

## 4. Concurrency & UI Responsiveness
- Long-running tasks (FEM solves, topology optimization, mesh voxelisation) must accept `CancellationToken` and `IProgress<T>`.
- The UI thread must never be blocked by computation. All heavy jobs run asynchronously on the thread pool.
- Optimization iteration frames are saved every $N=5$ iterations by default (plus frame 0 and final) into `.topo` Brotli-compressed streams.

## 5. Development Cadence & Milestone Artifacts
- Commit after each green milestone.
- Write a retrospective/verification summary in `docs/STEPn.md` at the end of every roadmap step.
