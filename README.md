# Mycel

An open-source desktop application and CLI for 3D finite element analysis (FEA) and SIMP topology optimisation tailored for additive manufacturing (FDM/FFF).

![Mycel Main Window](docs/images/main_window.png)

## Workflow

Mycel guides the design through a 6-step sequential pipeline:

1. **Model & Geometry**: Load binary or ASCII STL files. Inspect triangle counts, bounding box dimensions, volume, and watertight manifold status.
2. **Applied Loads**: Interactively select surface patches on the 3D mesh via dihedral angle flood-fill. Assign force magnitudes in Newtons (N), Kilograms (kg), or Grams (g) with normal, axial, or custom vector orientations.
3. **Fixed Supports**: Select constraint patches to fix translational degrees of freedom ($U_x, U_y, U_z$). Optional Preserve regions freeze non-design functional features (e.g. bolt holes).
4. **Material**: Choose from FDM material presets (Generic PLA, PLA+, PETG) or configure custom Young's modulus, Poisson's ratio, and directional yield strengths.
5. **Resolution & Analysis**: Select voxel resolution presets (Draft ~50k, Standard ~100k, Fine ~250k elements). Run linear elastic FEA to preview compliance, displacements, von Mises stresses, and directional FDM safety factors.
6. **Optimise**: Run SIMP topology optimisation with real-time 3D density visualization and compliance charts. Scrub past iterations, verify thresholded geometry, and export watertight binary STLs.

## Features

- **STL Ingestion**: Fast binary and ASCII STL parser with spatial vertex welding and linear BVH tree raycasting.
- **Surface Selection**: Interactive normal-based region growing with adjustable dihedral angle threshold and cylindrical bore selection.
- **Matrix-Free FEA**: Trilinear Hex8 element elasticity solver evaluated on-the-fly with 8-colour parallel lattice domain decomposition, eliminating global stiffness matrix assembly.
- **Topology Optimisation**: Minimum compliance SIMP formulation with linear density cone filtering, chain-rule sensitivities, and Optimality Criteria (OC) volume bisection.
- **Timeline & History**: Scrub and inspect all completed iterations; compare against original ghost mesh envelope.
- **Isosurface Extraction**: Constrained Surface Nets reconstruction with volume-preserving Taubin smoothing for watertight 2-manifold STL export.
- **Persistence**: Brotli-compressed `.topo` project archives storing geometry, boundary patches, and iteration histories.

## How It Works

Mycel discretizes arbitrary 3D geometry into a uniform regular hexahedral (Hex8) voxel grid. Elastic equations are solved using an element-by-element matrix-free Jacobi-preconditioned Conjugate Gradient (PCG) solver with subspace-projected boundary constraints. Density penalization follows the SIMP model ($E(\tilde{\rho}) = E_{\min} + \tilde{\rho}^p (E_0 - E_{\min})$ with $p=3, E_{\min}/E_0 = 10^{-9}$).

The codebase is partitioned into three decoupled assemblies:
- `Mycel.Core`: Pure numerical engine (voxelization, FEM, SIMP, Surface Nets) with zero UI dependencies.
- `Mycel.App`: Cross-platform desktop interface built with Avalonia 12 and Silk.NET OpenGL.
- `Mycel.Cli`: Headless command-line tool for automated batch analysis and optimization.

### Frequently Asked Questions

**Why does the chosen material not change the optimized topology?**  
In linear elasticity with a single compliance objective and a fixed volume fraction, Young's modulus $E$ scales stiffness and displacements proportionally across the entire domain without altering relative element sensitivities. Material properties directly dictate displacements, stress fields, and safety factors, but not the optimal layout.

**Why are the simulation results indicative?**  
Simulations assume ideal homogeneous continuum elasticity. Physical FDM/FFF 3D prints exhibit significant interlayer anisotropy, nozzle porosity, slicing perimeter variances, and thermal crystallization effects.

## Validation

Numerical accuracy is verified by automated analytical benchmarks (V1–V9):
- **Timoshenko Beam Convergence (V3)**: Monotonic convergence from 35.71% error at 1 element through depth to **1.64% error** at 8 elements through depth (5,120 elements), matching Timoshenko beam theory within continuum boundary restraint bounds.
- **Equilibrium & Energy (V4, V5)**: Global force balance $\sum \mathbf{R} = -\sum \mathbf{F}$ holds to $< 10^{-10}$ relative error; moment balance to $< 10^{-8}$; compliance identity $\mathbf{F}^T \mathbf{u} \equiv \mathbf{u}^T \mathbf{K} \mathbf{u}$ holds to $< 10^{-10}$.
- **Performance (V10)**: Matrix-free operator achieves 8.44 ms per matvec on 98,415 active elements (319,872 DOFs) on a 20-thread Linux workstation, with warm-started PCG solves converging in 4.26 s.

See [docs/VALIDATION.md](docs/VALIDATION.md) and [docs/STEP3_BENCH.md](docs/STEP3_BENCH.md) for complete derivations and benchmark logs.

## Installation & Requirements

### System Requirements
- **Linux**: 64-bit distribution with OpenGL 3.3+ Core Profile or OpenGL ES 3.0+ (`libGL.so.1`, `libEGL.so.1`), `libfontconfig.so.1`, `libfreetype.so.6`, and X11 or Wayland. Software rendering fallback is available via `LIBGL_ALWAYS_SOFTWARE=1`.
- **Windows**: Windows 10 (version 1809+) or Windows 11 (x64) with DirectX 11 / OpenGL 3.3 compatible graphics drivers.

See [docs/INSTALL.md](docs/INSTALL.md) for distribution package commands.

### Build from Source
Prerequisites: .NET 10.0 SDK (`dotnet --version` >= 10.0.100).

```bash
# Clone the repository
git clone https://github.com/simone/StlOptimizer.git
cd StlOptimizer

# Build in Release mode
dotnet build -c Release

# Run automated tests
dotnet test -c Release --filter "Category!=Benchmark"

# Launch the desktop application
dotnet run --project src/Mycel.App/Mycel.App.csproj -c Release
```

Prebuilt binary archives are packaged via `./build.sh` (Linux) and `./build.ps1` (Windows) into `dist/`. No prebuilt release binaries are hosted yet.

## CLI Usage

The headless `mycel-cli` executable runs static FEA and topology optimization from JSON specifications:

```bash
# Run static analysis
mycel-cli analyze model.json

# Run topology optimization
mycel-cli optimize model.json --out runs/my_run
```

For full desktop operational instructions, see [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

## Safety & Limitations

> **Indicative Results Disclaimer**: Structural stress distributions and topology-optimized geometries generated by Mycel are numerical engineering approximations based on linear elasticity and SIMP homogenization. FDM/FFF 3D prints exhibit significant interlayer anisotropy, porosity, and thermal variances. Physical load testing and professional FEA verification are mandatory before deploying designs in structural, safety-critical, or load-bearing applications.

Current technical limitations:
- Linear elastic isotropic material model (non-linear plasticity and creep are not modeled).
- Single static load case per run (multiple combined load cases are not yet supported).
- No explicit manufacturing overhang or build-direction self-support constraints.
- Voxel stair-stepping approximation on coarse grids (mitigated by Taubin smoothing during export).

## Status & Roadmap

Implemented capabilities include core FEM, SIMP optimization, GUI workflow, and STL export. The following roadmap items are **not yet implemented**:
- Geometric Multigrid preconditioner (MG-PCG).
- Multiple simultaneous load cases.
- Additive manufacturing overhang angle filters.
- Local von Mises stress-constrained topology optimization.
- Linux AppImage standalone bundle (`packaging/` not yet configured).

See [docs/ROADMAP.md](docs/ROADMAP.md) for milestone specifications.

## Contributing & License

- Contributing guidelines and coding standards: [CONTRIBUTING.md](CONTRIBUTING.md).
- Architecture and design decisions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/DECISIONS.md](docs/DECISIONS.md).
- **License**: GNU General Public License v3.0 ([LICENSE](LICENSE)).

### Acknowledgements

- Topology optimization formulation grounded in the SIMP and OC implementations of Sigmund (2001), Andreassen et al. (2011) "88-line", and Liu & Tovar (2014) "top3d".
- Isosurface polygonization using Constrained Surface Nets (Gibson 1998) and volume-preserving smoothing (Taubin 1995).
- Third-party packages and notices are cataloged in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

