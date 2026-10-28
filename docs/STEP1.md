# Step 1 Verification & Retrospective — Scaffold, STL I/O, BVH, Region Selection & 3D Viewport

## 1. What Was Built
- **Solution & Projects (`net10.0`)**:
  - `src/Mycel.Core`: Pure numerical and geometrical library with zero UI dependencies.
  - `src/Mycel.App`: Avalonia 12.1.4 desktop application (Dark Fluent theme, MVVM with `CommunityToolkit.Mvvm`, `Silk.NET.OpenGL` 2.23.0).
  - `src/Mycel.Cli`: Headless CLI benchmark and STL inspection utility.
  - `tests/Mycel.Core.Tests`: xUnit automated specification test suite (23 unit and benchmark tests).
- **Core Geometry Components (`Mycel.Core.Geometry`)**:
  - `IndexedMesh`: Immutable triangle mesh with welded positions, triangle indices, face normals, area-weighted smooth vertex normals, bounding box, surface area, and double-precision signed volume.
  - `StlReader` & `StlWriter`: Automatic Binary vs. ASCII detection via strict `84 + 50 * N` byte-length check (never trusting `"solid"` prefixes), spatial-hash vertex welding (`1e-5 * bboxDiagonal`), degenerate triangle filtering, and manifold topology verification (`OpenEdges`, `NonManifoldEdges`, `IsWatertight`).
  - `Bvh`: Flat-array Bounding Volume Hierarchy (`BvhNode` value-type struct, 32 bytes/node, zero heap allocations per node) built with 8-bin Surface Area Heuristic (SAH). Supports branchless Möller–Trumbore ray casting and exact closest-point queries.
  - `RegionSelector` & `SurfaceSelection`: Precomputes half-edge adjacency across 2-manifold edges and performs allocation-free BFS flood fill within a configurable dihedral angle threshold (default $30^\circ$), supporting `Replace`, `Add` (`Shift`), and `Subtract` (`Ctrl`) operations across `Load`, `Support`, and `Preserve` selection kinds.
- **3D Viewport & Desktop UI (`Mycel.App`)**:
  - `Viewport3DControl`: Custom `OpenGlControlBase` + `Silk.NET.OpenGL` control with Z-up turntable `OrbitCamera` (orbit, pan, zoom-to-cursor, fit-to-view), crease-aware normal shading (`dFdx`/`dFdy` screen-space derivatives blended with smooth normals for $<32^\circ$ curvature), polygon-offset selection/hover overlays, scalar colormaps (`Viridis`, `Turbo`, `CoolWarm`, `Jet`, `DensityGrey`), and double-buffered `UpdateMeshAsync`.
  - `MainWindow`: Slim top bar (`Open STL`, unit selector `mm`/`cm`/`m`/`in`, `Fit View`), left selection panel (dihedral angle slider $1^\circ\text{--}89^\circ$, add/remove/rename/kind/color selection list), central 3D viewport with `.stl` drag-and-drop, and status bar (triangles, bounding box in `mm`, watertight badge, live FPS).

---

## 2. OpenGL / GLSL Profile Used
- **Hardware & Driver Tested**:
  - **GPU**: `AMD Radeon RX 480 Graphics (radeonsi, polaris10, ACO, DRM 3.64, 7.2.11-200.nobara.fc44.x86_64)`
  - **Active Context Profile on Linux**: `OpenGL 4.6 (Core Profile) Mesa 26.2.5 (GLSL 4.60)`
- **Cross-Platform Shader Strategy**:
  - `Viewport3DControl` inspects `gl.ContextInfo.Version.Type` in `OnOpenGlInit`.
  - When Avalonia provides a Desktop OpenGL Core context (Linux GLX/EGL), it prepends `#version 330 core`.
  - When Avalonia provides an OpenGL ES context (Windows default via ANGLE over DirectX 11, or Linux Wayland/EGL ES), it prepends `#version 300 es` + `precision highp float; precision highp int;`.
  - Both profiles execute the exact same shader body (`layout(location = ...)`, `in`/`out`, `dFdx`/`dFdy`, `uint` element buffers).

---

## 3. Measured Performance (Release Build, .NET 10.0.112, 20-thread CPU, RX 480 GPU)

### 1,001,112-Triangle Sphere Benchmark (`Mycel.Cli --benchmark-1m`)
| Metric | Measured Value | Target Budget |
| :--- | :--- | :--- |
| **Input Geometry** | $708 \times 708$ UV Sphere ($1,001,112$ tris, $500,558$ welded verts) | $1,000,000$ tris |
| **Vertices Merged** | $2,502,778$ duplicates welded | Exact manifold |
| **STL Read + Spatial-Hash Weld + Topology Check** | **377.7 ms** ($0.378\text{ s}$) | $< 3.0\text{ s}$ |
| **BVH Build (639,967 nodes, 8-bin SAH)** | **839.4 ms** ($0.839\text{ s}$) | $< 3.0\text{ s}$ |
| **Half-Edge Adjacency Build** | **109.6 ms** | $< 1.0\text{ s}$ |
| **Raycast Throughput (50,000 rays)** | **288,908 picks/sec** ($3.46\text{ }\mu\text{s/pick}$) | $> 50,000\text{ picks/sec}$ |

### 500,000-Triangle 3D Viewport Smoke Test (`Mycel.App --smoke-500k`)
| Metric | Measured Value | Target Budget |
| :--- | :--- | :--- |
| **500k-Triangle Continuous Orbit Frame Rate** | **118.5 FPS** | $\ge 60\text{ FPS}$ |

---

## 4. Known Limitations (Step 1 Scope)
- Non-manifold edges ($\ge 3$ incident triangles) are detected, counted in `MeshReport.NonManifoldEdges`, and intentionally act as region-growing barriers rather than being automatically stitched.
- `SetArrows(...)` is a stub awaiting Step 5 static-analysis force vector visualization.
- No voxelization or FEM solver is active yet (scheduled for Step 2 and Step 3).

