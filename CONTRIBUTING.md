# Contributing to Mycel

Thank you for contributing to **Mycel**. This document outlines the build workflow, test execution, architectural invariants, and coding conventions taken from `AGENTS.md`.

---

## 1. Prerequisites & Building

Mycel targets **.NET 10.0** (`10.0.x`).

### Build for Development
```bash
dotnet restore
dotnet build --configuration Release
```

### Build Self-Contained Release Packages
Use the platform build scripts at the repository root to run the test suite and produce self-contained, single-file, ReadyToRun archives in `dist/`:
```bash
# Linux / macOS
./build.sh

# Windows (PowerShell)
./build.ps1
```

---

## 2. Running Tests

### Default Test Suite (Fast Unit & Integration Tests)
Run all verification tests while excluding long-running throughput benchmarks:
```bash
dotnet test --configuration Release --filter "Category!=Benchmark"
```

### Benchmarks & Scaling Suites
Run performance and scaling benchmarks explicitly:
```bash
dotnet test --configuration Release --filter "Category=Benchmark"
```

---

## 3. Specification & Verification Protocol

- **Tests are the specification. Never weaken, delete, or skip a test to make it pass; if a test looks wrong, stop and ask the maintainer.**
- Code without passing automated tests is considered broken and unverified.
- All physics and numerical algorithms (Hex8 stiffness, SIMP sensitivities, matrix-free PCG, density/sensitivity filters, and Constrained Surface Nets) must have unit test benchmarks against known analytical solutions (e.g., Euler-Bernoulli / Timoshenko beam theory, Kirsch plate, Lame sphere, central-difference finite-difference checks) or published literature values within defined tolerances.

---

## 4. Core Architecture & Development Conventions (`AGENTS.md`)

### Language & Documentation
- All code, identifiers, code comments, commit messages, and documentation files must be written in **English**.
- Preserve existing comments and docstrings unrelated to your changes.

### Physical Units & Coordinate Conventions
- **Units**: Millimetres (**mm**), Newtons (**N**), and Megapascals (**MPa**, where $1\text{ MPa} = 1\text{ N/mm}^2$).
- **Coordinates**: Right-handed system with **Z up** ($Z$ = 3D print build direction).
- **Structured Grid Indexing over Bounding Box**:
  - Element index: `e = ix + nx * (iy + ny * iz)` where `ix ∈ [0, nx-1], iy ∈ [0, ny-1], iz ∈ [0, nz-1]`.
  - Node index: `n = i + (nx + 1) * (j + (ny + 1) * k)` where `i ∈ [0, nx], j ∈ [0, ny], k ∈ [0, nz]`.
  - Node DOFs: `dof = 3 * n + {0, 1, 2}` representing $(u_x, u_y, u_z)$.
  - Active elements: Flat contiguous array of active element bounding-box indices.
- **Boundary Mapping**: Surface triangles map to Hex8 nodes via a volume dilation layer ($\delta = 1.0 \times h$). A protective passive solid shell ($\rho \equiv 1$) is enforced around loads and supports by default to prevent numerical point hinges.

### Architectural Separation & Numerical Rigor
- **UI-Free Core**: `Mycel.Core` **MUST NOT** reference any UI or graphics framework (no Avalonia, no WPF, no Silk.NET, no OpenGL).
- **Deterministic Numerics**: Enforce a fixed reduction order in parallel loops (e.g., chunked deterministic reductions in dot products and norms) so runs are bit-identical across executions.
- **Zero Allocations in Hot Loops**: Pre-allocate all scratch buffers, vectors, and arrays outside PCG and Optimality Criteria (OC) iteration loops.
- **Concurrency & UI Responsiveness**:
  - Long-running operations (mesh voxelisation, FEM solves, topology optimisation, iso-surface extraction) must accept `CancellationToken` and `IProgress<T>`.
  - Never block the UI thread with computation; run heavy jobs asynchronously on background threads.

### License & Dependencies Policy
- **Project License**: GNU General Public License v3.0 (**GPLv3**).
- **Authorized NuGet Packages Only**: `Avalonia`, `CommunityToolkit.Mvvm`, `Silk.NET.OpenGL`, `ScottPlot.Avalonia`, `Material.Icons.Avalonia`, `xunit`.
- No new NuGet package may be added without prior explicit approval recorded in `docs/DECISIONS.md`.

---

## 5. How to Add a Validation Test

1. **Choose the Matching Test Folder**: Place new tests in `tests/Mycel.Core.Tests/<Subsystem>/` matching the `Mycel.Core` namespace (`Geometry`, `Voxelization`, `Fem`, `Optimization`, `Workflow`, `Step6`, or `Diagnostics`).
2. **State the Analytical Reference**: In a comment or XML docstring above the `[Fact]` method, document the analytical formula, reference solution, physical dimensions (`mm`, `N`, `MPa`), and expected relative error tolerance.
3. **Keep Default Tests Deterministic and Fast**:
   - Construct synthetic meshes or `FeModel` instances directly in memory.
   - Assert exact invariants (symmetry, positive-definiteness, volume conservation, 2-manifold Euler characteristic $V - E + F = 2$, and relative error tolerances).
   - If adding a heavy multi-hundred-thousand-element performance test, mark it with `[Trait("Category", "Benchmark")]` so the default CI suite remains fast.
4. **Verify Locally**: Run `dotnet test --configuration Release` and ensure zero warnings and zero failures before committing.

