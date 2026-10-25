# Step 2 — Voxelisation, Surface Patches, Model Validation & Voxel Debug View

## 1. Overview & Architecture

Step 2 delivers the structural discretisation and validation layer of **TopoOpt**, bridging geometric boundary representations (STL surface meshes) and voxel-based finite element methods (Hex8 structured grids).

Key deliverables implemented:
1. **`GridSpec`**: Regular 3D structured grid indexing conforming strictly to `AGENTS.md` equations with 64-bit safe node and DOF handling and bounds-checking.
2. **`Voxeliser`**: High-performance multi-threaded ray-parity filling with 2D YZ triangle binning (CSR layout) and irrational perturbation tie-breaking ($\delta_y = \phi \cdot 10^{-5} h$, $\delta_z = \frac{\sqrt{3}-1}{2} \cdot 10^{-5} h$). Includes robust 3-axis (X, Y, Z) majority voting fallback (2-of-3) for non-watertight meshes.
3. **`Connectivity`**: BFS-based 6-connected component analysis, automatically retaining the largest structural domain by default and dropping disconnected floating voxels.
4. **`SurfacePatchBuilder`**: Boundary face mapping with sub-BVH distance queries over selected triangles ($\tau = 1.0 \implies \text{dist} \le 1.0 h$) and normal alignment rejection ($\mathbf{n}_{\text{face}} \cdot \mathbf{n}_{\text{tri}} \ge 0.5$). Implements exact tributary node area distribution ($\sum \text{NodeArea} = \text{Faces.Length} \times h^2$).
5. **`ModelValidator`**: Comprehensive pre-flight checks detecting unconstrained bodies, missing loads, point-load singularities ($< 4$ nodes), load/support overlaps, disconnected regions, coarse resolution warnings, and PCG resource estimation.
6. **`PassiveRegion`**: Chebyshev distance masking ($r = 2h$) around boundary patch nodes to form numerical protective solid shells ($\rho \equiv 1$).
7. **App Voxel Debug View**: Real-time Avalonia 3D viewport toggle between Original STL and Voxel mode (drawing exposed quads via Blinn-Phong shading) along with highlighted patch node point overlays, target element presets (50k to 500k), background voxelisation progress, and validation diagnostics panel.

---

## 2. Mathematical & Algorithmic Formulation

### 2.1 Structured Grid Indexing (`AGENTS.md`)
For a bounding box discretised into $n_x \times n_y \times n_z$ elements with pitch $h$:
- **Element index**: $e = i_x + n_x(i_y + n_y i_z)$ with $i_x \in [0, n_x - 1], i_y \in [0, n_y - 1], i_z \in [0, n_z - 1]$.
- **Node index**: $n = i + (n_x + 1)(j + (n_y + 1)k)$ with $i \in [0, n_x], j \in [0, n_y], k \in [0, n_z]$.
- **DOF index**: $\text{dof} = 3n + c$ with $c \in \{0, 1, 2\}$ corresponding to $(u_x, u_y, u_z)$.
- Inverses are implemented using single-instruction integer division/remainder (`Math.DivRem`) and validated against round-trips up to $10^9$ elements / $3 \cdot 10^9$ DOFs.

### 2.2 Parallel Ray-Parity Voxelisation with 2D Binning
- **2D Binning**: Each triangle is projected onto the cross-section plane (YZ for X-rays). Bounding row spans are computed in $O(1)$ and accumulated into a flat CSR index structure, completely eliminating global ray-triangle queries.
- **Irrational Tie-Breaking**: Ray origins are shifted by:
  $$\delta_y = \frac{\sqrt{5} - 1}{2} \cdot 10^{-5} h \approx 0.61803399 \cdot 10^{-5} h$$
  $$\delta_z = \frac{\sqrt{3} - 1}{2} \cdot 10^{-5} h \approx 0.36602540 \cdot 10^{-5} h$$
  This mathematically guarantees that rays never pass through mesh vertices or coplanar edges.
- **Ray Intersection**: Analytical 2D barycentric inversion along $(1, 0, 0)$ with sorted intersection pairing $\left[X_{2k}, X_{2k+1}\right]$.
- **Multi-Axis Voting Fallback**: When `!report.IsWatertight`, independent ray fills along X, Y, and Z are computed and combined via majority vote:
  $$\text{Domain}[e] = \begin{cases} 1 & \text{if } D_x[e] + D_y[e] + D_z[e] \ge 2 \\ 0 & \text{otherwise} \end{cases}$$

### 2.3 Surface Patch Tributary Area Invariant
For an exposed quad face of area $h^2$, the area is equally apportioned to its 4 Hex8 corner nodes:
$$A_{\text{tributary}} = \frac{h^2}{4}$$
For any boundary patch:
$$\sum_{n \in \text{Nodes}} A_n \equiv N_{\text{faces}} \cdot h^2$$
This ensures exact force conservation during nodal load conversion: $\mathbf{F}_n = \frac{A_n}{A_{\text{total}}} \mathbf{F}_{\text{total}}$.

---

## 3. Benchmark Results & Verification

All automated tests act as specification and passed without skipping or modification.

### 3.1 Test Suite Summary
- **Total Test Cases**: 46 passed (0 failed, 1 explicit benchmark skipped during regular runs).
- **Execution Duration**: 546 ms.
- **Coverage**:
  - `GridSpecTests`: Indexing round-trips, boundary checks, 64-bit overflow, volume sizing, element limit caps.
  - `VoxeliserTests`: Box volume exactness, integer coordinate tie-breaks, sphere volume error ($< 1.5\%$), cylinder with coaxial hole ($< 2.0\%$), L-bracket ($< 2.0\%$), rotated box invariance with cavity checking, non-watertight 3-axis majority voting, bit-exact determinism across runs (SHA-256).
  - `SurfacePatchBuilderTests`: Box top face tributary areas (corner $h^2/4$, edge $h^2/2$, interior $h^2$), cylindrical hole inner-wall normal filtering, sub-voxel empty patch handling.
  - `ModelValidatorTests`: Point load rejection ($< 4$ nodes), load/support overlap detection, coarse resolution warning ($< 3$ voxels across).
  - `PassiveRegionTests`: Chebyshev distance ball masking ($r = 2h$).

### 3.2 1M-Triangle to 2M-Voxel Benchmark
Executed on a synthetic high-resolution sphere mesh ($R = 50\text{ mm}$):
- **Input Geometry**: 1,001,112 triangles, 500,558 vertices.
- **Structured Grid**: $157 \times 157 \times 157 = 3,869,893$ total elements ($h = 0.640\text{ mm}$).
- **Solid Elements Produced**: 1,999,897 elements.
- **Execution Time**: **234 ms** (0.23 s).
- **Memory Delta**: 86 MB.

---

## 4. UI Voxel Debug View Features

1. **Target Elements Input**: Presets for 50k, 100k, 250k, 500k elements, defaulting to 100k.
2. **Asynchronous Voxelisation**: Runs on the .NET thread pool with `CancellationToken` and `IProgress<float>` reporting to a live progress bar without freezing the Avalonia UI.
3. **View Mode Toggle**: Instant switching between Original STL rendering and Voxel exposed-quad mode.
4. **Active Patch Node Points**: When a surface selection is highlighted, its associated Hex8 boundary nodes are rendered as large colored points directly on the voxel mesh surface.
5. **Validation Diagnostics Panel**: Live issue feed with color-coded severity badges (Red Error, Yellow Warning, Blue Info). The "Start Optimisation" button is automatically disabled when validation errors are present.
