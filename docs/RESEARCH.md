# Technical Research (R1 – R12)

## R1: Environment & Tooling
- **Findings**: Linux host (Nobara Linux 44 / Fedora 44, kernel 7.2.11, 20 CPU threads, 62 GiB RAM, AMD Radeon RX 480 8GB, Mesa 26.2.5 with OpenGL Core 4.6, GLES 3.2, Vulkan 1.4). .NET SDK 10.0.112 (host 10.0.12) is installed and active. Avalonia templates (`avalonia.app`, `avalonia.mvvm`) are installed. Latest stable Avalonia on NuGet is 12.1.4.
- **Source URLs**: https://aka.ms/dotnet/info | https://www.nuget.org/packages/Avalonia/ | https://github.com/AvaloniaUI/Avalonia
- **Licence**: MIT (Avalonia) | MIT (.NET Runtime).
- **Recommendation**: Target `net10.0` for all projects (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`). Use Avalonia 12.1.4 with MVVM CommunityToolkit.
- **Risks**: Bleeding-edge .NET 10 / Avalonia 12 minor API updates during development.
- **Open Questions**: None on host environment; all hardware and SDK prerequisites are met.

## R2: 3D Viewport in Avalonia
- **Findings**: Ava3DControl is unmaintained/unverified. `HelixToolkit.Avalonia.SharpDX` is DirectX/Windows-only; `HelixToolkit.Nex` is experimental. Avalonia's built-in `OpenGlControlBase` (`Avalonia.OpenGL.Controls`) provides cross-platform GPU contexts: on Windows it defaults to ANGLE (EGL/GLES 3.0 over DirectX), on Linux it uses EGL/GLX. Silk.NET.OpenGL (2.22.0) binds seamlessly via `GL.GetApi(gl.GetProcAddress)`. Writing shaders in GLSL ES 3.00 (`#version 300 es`) or injecting a small preamble guarantees native cross-platform execution on both Windows (ANGLE) and Linux (Mesa).
- **Source URLs**: https://docs.avaloniaui.net/docs/concepts/headless/opengl-controls | https://www.nuget.org/packages/Silk.NET.OpenGL/ | https://github.com/dotnet/Silk.NET
- **Licence**: MIT (Silk.NET, Avalonia).
- **Recommendation**: Primary: Custom control derived from `OpenGlControlBase` using `Silk.NET.OpenGL` and GLSL ES 3.00 shaders. Fallback: Software/CPU blitted framebuffer or Silk.NET standalone window embed.
- **Risks**: Window resize / swapchain glitches if context synchronization is violated outside `OnOpenGlRender`.
- **One-Day Spike Plan**: (1) Subclass `OpenGlControlBase`, initialize Silk `GL.GetApi(gl.GetProcAddress)`. (2) Compile simple GLSL ES 3.00 diffuse shader. (3) Render spinning cube with dynamic vertex buffer. (4) Verify on Linux (EGL) and Windows (ANGLE).
- **Open Questions**: Color-coded picking buffer vs ray-cast CPU BVH (CPU BVH preferred to keep GL pipeline decoupled).

## R3: Picking & Region Selection
- **Findings**: Triangle-accurate ray picking is best solved with a linear Bounding Volume Hierarchy (BVH) built with Binned Surface Area Heuristic (SAH), paired with branchless Möller–Trumbore ray–triangle intersection. Surface region growing traverses half-edge adjacency starting from the picked triangle, accepting adjacent triangles when normal dihedral angle $\theta \le \theta_{threshold}$ ($15^\circ$–$45^\circ$). Cylindrical holes are selected by identifying co-axial cylindrical patches: seed normal and adjacent normals must be orthogonal to a common cylinder axis $\mathbf{a} = \mathbf{n}_1 \times \mathbf{n}_2$ with constant radius $R$ within tolerance.
- **Source URLs**: Möller & Trumbore (1997) https://doi.org/10.1080/10867651.1997.10487468 | Wald (2007) Binned SAH BVH Building.
- **Licence**: Public domain / algorithmic reference.
- **Recommendation**: Build an immutable AABB-BVH on STL load ($O(N \log N)$ build, $O(\log N)$ pick time). Maintain edge-to-triangle adjacency for $O(M)$ BFS region growing and cylindrical feature detection.
- **Risks**: Non-welded STL vertices break face adjacency; dirty CAD meshes with micro-facets disrupt dihedral thresholds.
- **Open Questions**: User tolerance sliders for angular deviation and cylinder radius.

## R4: STL Pitfalls & Robust Import
- **Findings**: Binary STL headers often contain the ASCII word "solid". Detection must check if file size equals exactly $84 + 50 \times N$ (where $N = \text{uint32 at byte 80}$). Vertices are duplicated across faces. Normals stored in STLs are frequently $(0,0,0)$ or unnormalized, requiring re-computation via $(\mathbf{v}_1 - \mathbf{v}_0) \times (\mathbf{v}_2 - \mathbf{v}_0)$. STL coordinates are unitless; bounding box heuristics ($<0.5 \to$ meters, $1-500 \to$ mm) must detect scale. Non-manifold edges and open boundaries violate watertightness ($V - E + F \ne 2(1-g)$).
- **Source URLs**: https://en.wikipedia.org/wiki/STL_(file_format) | https://www.fabbers.com/tech/STL_Format
- **Licence**: Open standard.
- **Recommendation**: Binary parser with $84 + 50N$ size verification and ASCII fallback. Spatial hash vertex welding ($\epsilon = 10^{-5}$ mm). Recompute face normals from winding order. Validate 2-manifold edge topology.
- **Risks**: Heavily corrupted meshes with self-intersections or degenerate zero-area triangles.
- **Open Questions**: Allow user confirmation dialog when auto-scaling detected meters/inches to mm.

## R5: Voxelisation & Boundary Mapping
- **Findings**: Simple ray-parity counting suffers from edge/vertex singularity hits. Generalised Winding Number (Jacobson et al. 2013) evaluated via solid angles $\Omega_k(\mathbf{p})$ provides robust inside/outside classification even for non-watertight, self-intersecting meshes. Thin walls ($< h$) require conservative surface-voxel intersection before interior flood-fill. Structured grid $n_x \times n_y \times n_z$ with flat active-element list maintains cache locality. Selected surface triangles map to boundary voxel faces/nodes by centroid projection or $h/\sqrt{2}$ spatial bounds.
- **Source URLs**: Jacobson et al. (2013) https://doi.org/10.1145/2461912.2461916 | Akenine-Möller (2001) Triangle-Box Overlap.
- **Licence**: Algorithmic reference.
- **Recommendation**: Fast bounding-box conservative triangle-voxel intersection + 3-axis voting ray parity, falling back to Generalised Winding Number if parity checks disagree. Run BFS connected-component check on active elements.
- **Risks**: Memory explosion if user chooses voxel size $h < 0.2$ mm on large parts.
- **Open Questions**: Minimum voxel resolution guard (e.g. max 2M voxels for v1 interactive responsiveness).

## R6: Voxel FEM Solver
- **Findings**: Isotropic trilinear Hex8 elements with $2\times 2\times 2$ Gauss integration. For regular cubic voxels of size $h$, element stiffness scales linearly: $\mathbf{K}_e = h \cdot \mathbf{K}_0(E, \nu)$. Matrix-free operator evaluates $(\mathbf{K}\mathbf{p}) = \sum_e \mathbf{K}_e \mathbf{p}_e$ without global sparse matrix assembly. 8-colour lattice partitioning ($(i_x\%2, i_y\%2, i_z\%2)$) allows race-free multithreaded scatter accumulation via `Parallel.For`. Fixed DOFs are enforced by subspace projection ($p_i = 0, r_i = 0$), preserving strict SPD properties for Jacobi-PCG. Hex8 exhibits shear locking in thin bending beams (5–15% stiffer than Euler-Bernoulli); validation tolerances must account for discretization error.
- **Source URLs**: Hughes (2000) *The Finite Element Method* | Amir, Aage, Lazarov (2014) https://doi.org/10.1007/s00158-013-1015-5
- **Licence**: Algorithmic textbook reference.
- **Recommendation**: Implement matrix-free 8-colour Jacobi-PCG in pure C# using `Vector256<float>` / `Vector512<float>`. Support warm-starting $\mathbf{u}^{(k+1)} \leftarrow \mathbf{u}^{(k)}$ across optimization steps.
- **Risks**: Ill-conditioned systems at low volume fractions ($E_{min}/E_0 = 10^{-9}$), leading to high PCG iteration counts.
- **Open Questions**: Whether Jacobi-PCG suffices for $\le 500k$ elements under warm-start before needing geometric multigrid.

## R7: Topology Optimisation (SIMP + OC)
- **Findings**: SIMP penalty $E(\tilde{\rho}) = E_{min} + \tilde{\rho}^p (E_0 - E_{min})$ ($p=3$, $E_{min}=10^{-9}$). Linear density filter with cone kernel $w = \max(0, R_{min} - r)$ prevents checkerboarding; chain-rule sensitivities computed via transpose convolution. Optimality Criteria (OC) with bisection on volume multiplier $\lambda$ ($m=0.2$ move limit, $\eta=0.5$ damping). Heaviside threshold projection drives discrete 0/1 designs. Continuation on penalty ($p: 1 \to 2 \to 3$) prevents local minima. 88-line (Andreassen 2011) and top3d (Liu & Tovar 2014) provide standard formulation references. Svanberg's MMA has non-commercial/GPL restrictions; OC is unencumbered and optimal for single-constraint compliance.
- **Source URLs**: Andreassen et al. (2011) https://doi.org/10.1007/s00158-010-0594-7 | Liu & Tovar (2014) https://doi.org/10.1007/s00158-014-1107-5 | Svanberg (1987) MMA.
- **Licence**: Published scientific algorithms; no third-party code copied.
- **Recommendation**: Implement SIMP + Density Filter + OC with bisection in `TopoOpt.Core`. For FDM (0.4 mm nozzle), set filter radius $R_{min} \ge 1.2$ mm ($\ge 2\times$ nozzle width / voxel size).
- **Risks**: Sensitivity boundary artifacts at fixed/loaded non-design regions if not explicitly preserved.
- **Open Questions**: Automatic preservation of solid shells (passive non-design elements) around load and support patches.

## R8: Post-Processing & Iso-Surface Extraction
- **Findings**: Hex8 stresses evaluated at centroid $\xi=\eta=\zeta=0$ avoid integration-point singularity. Nodal averaging over incident active elements produces continuous nodal stress fields; trilinear interpolation samples this field onto original STL vertices. Isosurface extraction: classical Marching Cubes is unpatented but generates degenerate slivers; Surface Nets (Gibson 1998) extracts manifold, watertight quad/triangle surfaces with lower vertex counts. Taubin smoothing ($\lambda > 0, \mu < 0$ with $1/\lambda + 1/\mu < 0$) eliminates voxel staircase artifacts without volume shrinkage.
- **Source URLs**: Gibson (1998) *Constrained Elastic Surface Nets* | Taubin (1995) https://doi.org/10.1145/218380.218473
- **Licence**: Algorithmic reference.
- **Recommendation**: Use Surface Nets for iso-surface extraction at $\rho = 0.5$, followed by 10–20 iterations of volume-preserving Taubin smoothing. Compute nodal von Mises stresses and sample onto output surface vertices.
- **Risks**: Inverted face normals at re-entrant corners after aggressive smoothing.
- **Open Questions**: Adaptive smoothing iterations based on voxel resolution.

## R9: FDM Materials (PLA, PLA+, PETG)
- **Findings**: FDM mechanical properties are strongly anisotropic due to interlayer bonding weaknesses. Representative manufacturer data (Polymaker, eSUN, Prusament) and literature (Tymrak 2014, Dizon 2018):
  - **Generic PLA**: $E_{XY} = 2800\text{--}3500\text{ MPa}$, $\nu = 0.36$, Yield/UTS $XY = 45\text{--}55\text{ MPa}$, $Z = 25\text{--}40\text{ MPa}$.
  - **PLA+**: $E_{XY} = 2200\text{--}3200\text{ MPa}$, $\nu = 0.36$, Yield/UTS $XY = 55\text{--}70\text{ MPa}$, $Z = 35\text{--}45\text{ MPa}$.
  - **PETG**: $E_{XY} = 1900\text{--}2300\text{ MPa}$, $\nu = 0.38$, Yield/UTS $XY = 45\text{--}55\text{ MPa}$, $Z = 30\text{--}42\text{ MPa}$.
- **Source URLs**: Polymaker PolyLite PLA & PETG TDS https://polymaker.com | eSUN PLA+ TDS | Tymrak et al. (2014) https://doi.org/10.1016/j.matdes.2014.02.038
- **Licence**: Public manufacturer technical sheets.
- **Recommendation**: Optimize under isotropic linear elasticity ($E$ does not affect optimal topology at fixed volume fraction). In post-processing, compute directional safety factors: $SF_{XY} = \sigma_{uts,XY} / \sigma_{vM}$ and $SF_Z = \sigma_{uts,Z} / \max(0, \sigma_{zz})$, displaying $SF = \min(SF_{XY}, SF_Z)$. Recommend user safety factor $SF \ge 2.5\text{--}3.5$.
- **Risks**: User assumes printed part matches isotropic bulk strength.
- **Open Questions**: Allow user custom material presets (JSON file).

## R10: Persistence & Iteration Storage
- **Findings**: In .NET 10 BCL, `System.IO.Compression` provides `BrotliStream`, `DeflateStream`, and `ZipArchive`. Zstandard is NOT part of .NET 10 inbox BCL (arrives in .NET 11). Storing 200 iterations of active densities quantized to `byte` ($\rho \times 255$):
  - 100k elements: $100\text{ KB/iter} \times 200 = 20\text{ MB raw} \to \approx 4\text{--}6\text{ MB compressed (Brotli)}$.
  - 500k elements: $500\text{ KB/iter} \times 200 = 100\text{ MB raw} \to \approx 20\text{--}30\text{ MB compressed}$.
  - 1M elements: $1\text{ MB/iter} \times 200 = 200\text{ MB raw} \to \approx 40\text{--}60\text{ MB compressed}$.
- **Source URLs**: https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream | https://github.com/dotnet/runtime
- **Licence**: MIT (.NET BCL).
- **Recommendation**: Project format `.topo`: standard zip container (`ZipArchive`) containing `manifest.json` (metadata, boundary conditions), `input_mesh.stl`, and `density_history.bin` (Brotli-compressed quantized byte stream with keyframes).
- **Risks**: High memory consumption during timeline scrubbing if all uncompressed frames are kept in RAM simultaneously.
- **Open Questions**: Frame chunking / ring buffer cache during interactive playback.

## R11: Packaging & Deployment
- **Findings**: `dotnet publish -c Release -r <rid> --self-contained -p:PublishReadyToRun=true -p:PublishSingleFile=true` generates standalone binaries for `win-x64` and `linux-x64`. Native AOT requires careful reflection configuration for Avalonia XAML bindings (`x:CompileBindings=true`), whereas ReadyToRun (R2R) offers near-instant startup without trimming risks. Linux dependencies include standard desktop libraries: `libX11`, `libXcursor`, `libGL.so.1`/`libEGL.so.1`, and fontconfig. Linux packaging is cleanest via AppImage (`appimagetool`) or standalone directory `.tar.gz`.
- **Source URLs**: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview | https://docs.avaloniaui.net/docs/deployment/
- **Licence**: MIT (.NET & Avalonia).
- **Recommendation**: Release targets: `win-x64` (single-file exe with ReadyToRun) and `linux-x64` (AppImage + portable tarball). Avoid full trimming/Native AOT in v1 to protect reflection/serialization stability.
- **Risks**: Missing OpenGL/EGL runtime packages on stripped-down Linux distributions.
- **Open Questions**: Automated GitHub Actions matrix build for Linux and Windows.

## R12: Antigravity Workflow & Rules Architecture
- **Findings**: Antigravity automatically discovers and applies rules hierarchically: walking up from CWD to the repository root for `GEMINI.md`, `AGENTS.md`, and `.agents/rules/*.md`. Builtin rules take precedence; workspace project rules override declared configurations. Skills are organized under `.agents/skills/<name>/SKILL.md` (or global `~/.gemini/config/skills/`) and loaded via progressive disclosure (only descriptions injected into context until invoked). The `/learn` command persists user-taught patterns and guidelines directly into workspace/global rule files (`AGENTS.md` / `GEMINI.md` / `.agents/rules/`).
- **Source URLs**: Antigravity Builtin Customization System Guide (`agy-customizations/SKILL.md`)
- **Licence**: Google internal / tool native.
- **Recommendation**: Place repository rules in root `AGENTS.md`. Maintain clean separation between tests, core logic, and UI bindings.
- **Risks**: Context budget overflow if rules exceed 24 KB per file or 20,000 tokens aggregate.
- **Open Questions**: Custom skill for running headless topology benchmarks.
