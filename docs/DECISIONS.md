# Architectural Decision Records (ADRs)

## ADR-001: 3D Viewport via OpenGlControlBase + Silk.NET
- **Status**: ACCEPTED
- **Context**: Need a responsive, cross-platform 3D viewport in Avalonia (Windows & Linux) capable of rendering $\ge 1\text{M}$ triangles at 60 FPS with dynamic mesh/color updates.
- **Decision**: Implement a custom viewport control inheriting from Avalonia's `OpenGlControlBase`, using `Silk.NET.OpenGL` (v2.22.0) function bindings via `GL.GetApi(gl.GetProcAddress)` and GLSL ES 3.00 shaders. Provide software/CPU fallback if OpenGL initialization fails.
- **Consequences**: Avoids heavyweight/Windows-only dependencies (SharpDX). Ensures native execution on both Windows (ANGLE) and Linux (Mesa). Requires careful context synchronization within `OnOpenGlRender`.

## ADR-002: Matrix-Free 8-Colour Hex8 FEM Solver with Jacobi-PCG
- **Status**: ACCEPTED
- **Context**: Global sparse matrix assembly for $\ge 500\text{k}$ elements ($\ge 1.6\text{M}$ DOFs) consumes gigabytes of memory and memory bandwidth.
- **Decision**: Implement an element-by-element matrix-free operator with 8-colour regular lattice domain decomposition. Compute $(\mathbf{K}\mathbf{p})$ on the fly using precomputed base element stiffness $\mathbf{K}_0$ scaled by $h$ and penalized density. Solve via Jacobi-preconditioned Conjugate Gradients with warm start.
- **Consequences**: Drastically cuts memory footprint to $O(N)$ vectors ($< 250\text{ MB}$ for 1M DOFs). Enables race-free parallel evaluation without atomic locks. Multigrid-CG deferred until benchmarks justify its algorithmic complexity.

## ADR-003: SIMP + OC Bisection with Hybrid Continuation & Heaviside Projection
- **Status**: ACCEPTED
- **Context**: Minimum compliance under single volume constraint. Need robust, fast, dependency-free optimization algorithm with crisp boundaries.
- **Decision**: Use Solid Isotropic Material with Penalization (SIMP, $p=3, E_{min}=10^{-9}$) with a linear density cone filter and chain-rule sensitivities. Update densities via Optimality Criteria (OC) with bisection on volume multiplier $\lambda$. Default to 'High Quality (Crisp)' mode with penalty continuation ($p: 1 \to 2 \to 3$) and Heaviside projection starting at iteration 40, switchable by the user to 'Fast Draft' ($p=3$ fixed).
- **Consequences**: Zero third-party solver dependencies. Eliminates grey-scale intermediate boundaries, producing clean solid members ready for 3D printing.

## ADR-004: Surface Nets + Taubin Smoothing for Mesh Reconstruction
- **Status**: ACCEPTED
- **Context**: Extracting a 3D-printable, smooth, watertight boundary mesh from the optimized voxel density grid.
- **Decision**: Use Constrained Surface Nets (Gibson 1998) at threshold $\rho = 0.5$, followed by Taubin volume-preserving smoothing ($\lambda = 0.5, \mu = -0.53$). Provide an interactive smoothing slider in the export dialog (default 10 iterations, range 0–30) with real-time 3D preview before saving to STL.
- **Consequences**: Produces clean, watertight, 2-manifold triangle meshes with significantly fewer degenerate slivers than classical Marching Cubes, while completely avoiding volume shrinkage.

## ADR-005: Project Persistence in `.topo` Zip Archive with Brotli Stream & N=5 History Default
- **Status**: ACCEPTED
- **Context**: Need to store full project setup and optimization iterations for timeline scrubbing/playback without consuming excessive disk space or runtime memory.
- **Decision**: Package the project as a `.topo` zip container (`System.IO.Compression.ZipArchive`) holding `manifest.json`, `model.stl`, and `history.bin`. Quantize iteration densities to 1 byte (`uint8`) and compress using .NET 10 built-in `BrotliStream` (Fastest level). Default to saving every $N=5$ iterations (plus iteration 0 and the final iteration) to save memory during live runs, configurable by the user ($N \in \{1, 2, 5, 10\}$).
- **Consequences**: Extremely compact files (~10–15 MB for 500k elements over 200 iterations). Timeline scrubbing remains responsive.

## ADR-006: Directional Safety Factor for FDM Anisotropy & On-Screen Disclaimer
- **Status**: ACCEPTED
- **Context**: 3D printed parts have lower tensile strength in the Z (build) direction due to layer adhesion.
- **Decision**: Perform optimization using isotropic linear elasticity. In the post-processing inspection phase, evaluate two separate safety factors: $SF_{XY} = \sigma_{uts,XY} / \sigma_{vM}$ and $SF_Z = \sigma_{uts,Z} / \max(0, \sigma_{zz})$. Display the minimum $SF = \min(SF_{XY}, SF_Z)$ with color codes (Green $\ge 3.0$, Yellow $2.0\text{--}3.0$, Red $< 2.0$). Display a permanent on-screen disclaimer banner: *"Indicative simulation for FDM; physical testing required for structural/load-bearing use"*.
- **Consequences**: Keeps the FE solver fast and strictly isotropic while providing the user with realistic, safety-critical 3D printing failure insights.

## ADR-007: Surface Patch to Hex8 Node Mapping via Volume Dilation
- **Status**: ACCEPTED
- **Context**: When mapping user-selected STL surface triangles to voxel nodes for boundary conditions, ensure discrete voxels do not leave gaps.
- **Decision**: Volume dilation layer: map all Hex8 nodes within Euclidean distance $\delta = 1.0 \times h$ (where $h$ is the voxel size) of any selected surface triangle.
- **Consequences**: Eliminates disconnected or missing boundary condition nodes on diagonal or curved surfaces.

## ADR-008: Non-Design Solid Shell around Boundary Conditions
- **Status**: ACCEPTED
- **Context**: SIMP optimizer can erode elements directly adjacent to applied loads or supports, creating single-voxel point hinges.
- **Decision**: Enforce an automatic protective passive solid shell ($\rho \equiv 1.0$, frozen from optimization) of 1-voxel thickness around all loaded and fixed nodes. Provide an advanced checkbox to toggle this feature off if desired.
- **Consequences**: Prevents unphysical numerical point hinges and artificial stress singularities at load introduction points.

## ADR-009: Target Time Reactive Budgeting & Resolution Estimation
- **Status**: ACCEPTED
- **Context**: Hardware capabilities (20 threads, 62 GB RAM) allow flexible scaling.
- **Decision**: Adopt a reactive temporal threshold model: the user sets their target maximum computation duration (e.g. 1 minute, 3 minutes, 5 minutes), and the system automatically calculates the optimal voxel resolution $h$ to fit that time budget. Also provide manual override for exact voxel size in mm.
- **Consequences**: Intuitive user experience tailored to available compute power, avoiding manual voxel count trial-and-error.

## ADR-010: Third-Party Dependencies Whitelist
- **Status**: ACCEPTED
- **Context**: Strictly permissive licenses only; explicit package authorization.
- **Decision**: Authorized NuGet packages:
  - `Avalonia` (12.1.4, MIT)
  - `Avalonia.Desktop` (12.1.4, MIT)
  - `Avalonia.Themes.Fluent` (12.1.4, MIT)
  - `CommunityToolkit.Mvvm` (8.4.0, MIT)
  - `Silk.NET.OpenGL` (2.22.0, MIT)
  - `ScottPlot.Avalonia` (5.x, MIT)
  - `Material.Icons.Avalonia` (MIT)
  - `xunit` (2.9.x, Apache-2.0)
  - `xunit.runner.visualstudio` (2.8.x, MIT)
- **Consequences**: Complete coverage for UI, 3D viewport, metrics charting, and dark-theme iconography without any viral or incompatible licenses.

## ADR-011: Project License (GNU GPL v3)
- **Status**: ACCEPTED
- **Context**: Repository licensing and distribution terms.
- **Decision**: License Mycel under the **GNU General Public License v3.0 (GPLv3)**.
- **Consequences**: Strong copyleft; ensures the codebase and all derivatives remain free and open source. Fully compatible with all chosen MIT/Apache-2.0 dependencies.

## ADR-012: UI Localization Strategy
- **Status**: ACCEPTED
- **Context**: Application user interface language.
- **Decision**: English-only for v1, but with all UI strings extracted and managed in standard .NET resource files (`.resx`), enabling clean future localization (e.g. Italian) without code refactoring.
- **Consequences**: Rapid v1 delivery while maintaining professional, localization-ready architecture.

## ADR-013: Load Definition & Region Selection Types
- **Status**: ACCEPTED
- **Context**: Defining boundary conditions and geometry preservation.
- **Decision**:
  - Loads are defined as a Total Force vector ($F$ in Newtons) distributed across the selected patch nodes. Direction can be specified as: Surface Normal (Inward / Outward), Global Axis ($\pm X, \pm Y, \pm Z$), or Custom Vector $(F_x, F_y, F_z)$.
  - Introduce three region selection types:
    1. **Fixed Support**: Rigid displacement constraint ($u_x = u_y = u_z = 0$).
    2. **Applied Load**: Force vector distribution.
    3. **Preserve (Keep Solid / Non-Design)**: Geometric region whose elements are permanently frozen to $\rho \equiv 1.0$ (e.g. bolt holes, contact faces).
- **Consequences**: Meets real-world engineering design needs where functional mounting interfaces must remain solid.

## ADR-014: Volume Fraction & Minimum Feature Size
- **Status**: ACCEPTED
- **Context**: SIMP optimizer parameters and FDM printability constraints.
- **Decision**:
  - Target Volume Fraction: default 30% (slider range 10%–80%).
  - Minimum Feature Size (filter radius $R_{min}$): default 2.0 mm (slider range 0.8 mm–10.0 mm), constrained by $R_{min} \ge 1.5 \times h$ to prevent sub-voxel numerical artifacts.
- **Consequences**: Prevents unprintable ultra-thin strands on standard 0.4 mm FDM nozzles.

## ADR-015: STL Unit Specification Dialog
- **Status**: ACCEPTED
- **Context**: STL files are completely unitless.
- **Decision**: On every STL import, show an explicit unit selection dialog (options: Millimeters [mm - Default], Centimeters [cm], Meters [m], Inches [in]) showing the computed bounding box dimensions in the selected unit.
- **Consequences**: Eliminates silent scale errors (e.g. 1000x scale mismatch between CAD meters and printing mm).
