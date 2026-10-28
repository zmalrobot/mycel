# Mycel Implementation Roadmap

## Step 1: Scaffold + Viewport + STL Import + Picking
- **Scope**:
  - Solution scaffolding (`Mycel.Core`, `Mycel.App`, `Mycel.Cli`, `Mycel.Core.Tests`).
  - Binary & ASCII STL parser with $84+50N$ binary header verification and spatial hash vertex welding.
  - Linear BVH tree with Möller–Trumbore ray casting.
  - Surface region growing (dihedral angle threshold) and cylindrical hole detection.
  - Avalonia 3D viewport control on `OpenGlControlBase` + Silk.NET with orbit/pan/zoom camera and selection highlighting.
- **Acceptance Criteria**:
  - Unit tests parse complex sample binary and ASCII STLs with 100% vertex welding accuracy.
  - BVH raycast test locates target triangles in $< 1\text{ ms}$ on a 500k-triangle mesh.
  - Region growing correctly selects a planar face and a bolt hole on a benchmark bracket.
  - App window opens on Linux (Mesa) and Windows (ANGLE) showing the STL rendered with phong shading at 60 FPS.
  - Verification recorded in `docs/STEP1.md`.

---

## Step 2: Voxelisation + Surface-to-Node Patches
- **Scope**:
  - Bounding box regular voxel grid allocation ($n_x \times n_y \times n_z$).
  - Fast triangle-voxel AABB overlap + 3-axis ray parity test, with fallback to Generalised Winding Number.
  - Connected component analysis (BFS) to discard floating artifacts.
  - Boundary mapping: project selected surface triangles to boundary voxel faces and Hex8 node IDs.
  - Visual overlay of voxelized solid in the 3D viewport.
- **Acceptance Criteria**:
  - Voxelizer accurately identifies solid interior for thin-walled and concave benchmark geometry.
  - Element and node indexing matches formula: $e = i_x + n_x(i_y + n_y i_z)$, $n = i + (n_x+1)(j + (n_y+1)k)$.
  - Selected surface triangles consistently activate corresponding boundary nodes without gaps.
  - Voxelization of a 200k active voxel model finishes in $< 3\text{ seconds}$.
  - Verification recorded in `docs/STEP2.md`.

---

## Step 3: Matrix-Free FEM Solver + Analytical Validation
- **Scope**:
  - Trilinear Hex8 element isotropic stiffness tensor ($\mathbf{K}_0$) with $2\times 2\times 2$ Gauss integration.
  - Linear element scaling: $\mathbf{K}_e = h \cdot \mathbf{K}_0$.
  - Matrix-free operator: compute $(\mathbf{K}\mathbf{p})$ on the fly with SIMD vectorization.
  - 8-colour regular domain decomposition for lock-free parallel scatter accumulation.
  - Subspace projection Dirichlet constraint handling.
  - Jacobi-preconditioned Conjugate Gradient (PCG) solver with convergence tracking.
  - Element centroid von Mises stress evaluation and nodal averaging.
- **Acceptance Criteria**:
  - Tip deflection of a cantilever beam benchmark matches analytical beam theory within discretization tolerance (accounting for hex8 shear locking).
  - PCG solver achieves residual norm $\|r\|_2 / \|b\|_2 \le 10^{-6}$ for single static load case.
  - Matrix-free solver consumes $< 250\text{ MB}$ RAM for a 500k-element model.
  - Unit tests enforce SPD behavior and strict deterministic numeric reduction order.
  - Verification recorded in `docs/STEP3.md`.

---

## Step 4: Headless Optimisation Loop + CLI Dev Tool
- **Scope**:
  - SIMP formulation: $E(\tilde{\rho}) = E_{min} + \tilde{\rho}^p (E_0 - E_{min})$ ($p=3, E_{min}=10^{-9}$).
  - Density cone filter with transpose-convolution sensitivity filtering.
  - Optimality Criteria (OC) density update with volume multiplier bisection.
  - Continuation strategy on penalization exponent ($p: 1 \to 2 \to 3$).
  - Measure of Non-Discreteness ($M_{nd}$) tracking.
  - `Mycel.Cli` console tool to run end-to-end optimizations headless with progress logging.
- **Acceptance Criteria**:
  - Headless optimization of standard MBB beam and 3D cantilever benchmarks converges to classic topology structures.
  - Volume constraint is satisfied to within $\pm 0.05\%$.
  - Sensitivity filtering strictly eliminates checkerboard patterns.
  - Automated tests verify compliance monotonically decreases under continuation steps.
  - Verification recorded in `docs/STEP4.md`.

---

## Step 5: UI Workflow + Static Analysis Preview
- **Scope**:
  - Guided step-by-step workflow navigation: (1) Load STL $\to$ (2) Load Patches $\to$ (3) Support Patches $\to$ (4) Material Selection $\to$ (5) Static Analysis Preview $\to$ (6) Optimize.
  - Material selection for Generic PLA, PLA+, PETG with default mechanical parameters and safety guidelines.
  - Pre-optimization static analysis preview: compute solid part deflection, von Mises stress colormap, and FDM directional safety factor $SF = \min(SF_{XY}, SF_Z)$.
  - Visual force vector arrows and support anchor gizmos in the 3D viewport.
- **Acceptance Criteria**:
  - User can complete the selection workflow without UI freezes or unhandled exceptions.
  - Stress colormap updates interactively with legend scale (min/max MPa).
  - Material switching dynamically updates displacement and stress metrics without re-running geometry steps.
  - Verification recorded in `docs/STEP5.md`.

---

## Step 6: Live Run + Iteration Storage + STL Export
- **Scope**:
  - Background asynchronous optimization execution with `CancellationToken` and `IProgress<OptimizationProgress>`.
  - Real-time 3D viewport update (live dynamic thresholded voxel / density cloud rendering) + convergence metrics chart (compliance & volume vs iteration).
  - In-memory ring buffer & `.topo` project file saving with Brotli-compressed density streams.
  - Timeline scrubber control allowing scrub/playback of all completed iterations.
  - Surface Nets isosurface extraction at $\rho = 0.5$ + volume-preserving Taubin smoothing.
  - Export selected iteration to watertight binary STL.
- **Acceptance Criteria**:
  - Live 3D view renders smoothly at $\ge 30\text{ FPS}$ during background optimization.
  - Cancelling optimization cleanly stops worker threads within $200\text{ ms}$ without corrupting state.
  - Scrubbing the timeline smoothly displays past iterations in real time.
  - Exported STL is verified as watertight, 2-manifold, and printable in slicers (e.g. PrusaSlicer/OrcaSlicer).
  - Verification recorded in `docs/STEP6.md`.

---

## Step 7: Packaging + End-to-End QA
- **Scope**:
  - `dotnet publish` automation for `win-x64` and `linux-x64` with ReadyToRun (R2R) single-file deployment.
  - Linux AppImage generation.
  - Comprehensive end-to-end test suite covering loading, boundary assignment, solver, optimization, persistence, and export.
  - Memory leak and hot-path profiling validation.
- **Acceptance Criteria**:
  - Standalone binaries run out-of-the-box on clean Linux and Windows environments without missing native dependencies.
  - 100% of automated unit and integration tests pass cleanly.
  - Cold startup time $\le 1.5\text{ seconds}$.
  - Verification recorded in `docs/STEP7.md`.
