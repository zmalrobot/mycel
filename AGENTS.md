# Mycel Agent Development Rules

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
- **Multi-Unit Load Inputs & Physical Grounding**: `Mycel.Core` and `.topo` project files MUST store load forces exclusively in Newtons ($N$). UI layers projecting alternative units (such as equivalent mass in $\text{kg}$ or $\text{g}$ via $F = m \cdot g_0$) must use standard gravitational acceleration $g_0 = 9.80665\text{ m/s}^2$ (ISO 80000-3 / NIST) and guard bidirectional property updates with an `_isSyncing` reentrancy flag to avoid circular notification cascades and precision drift.
- **Filament Metrics & Material Savings Grounding**:
  - Filament length calculations are grounded to standard FDM diameter $d = 1.75\text{ mm}$ ($A_{1.75} = \frac{\pi \cdot d^2}{4} \approx 2.405282\text{ mm}^2$).
  - Effective printed volume scales with infill percentage: $V_{eff} = V_{solid} \cdot \left(\frac{\rho_{infill}}{100}\right) \cdot V^*$.
  - Mass in grams: $M\text{ [g]} = V_{eff}\text{ [mm}^3\text{]} \cdot \frac{\rho_m\text{ [g/cm}^3\text{]}}{1000}$.
  - Filament length in meters: $L\text{ [m]} = \frac{V_{eff}\text{ [mm}^3\text{]}}{A_{1.75} \cdot 1000}$.
  - Material savings during optimization: $\Delta M = M_0 \cdot (1 - V^*)$, $\Delta L = L_0 \cdot (1 - V^*)$, and $\text{Savings \%} = (1 - V^*) \times 100\%$ relative to the initial un-optimized model at the active infill level.

## 2. Core Architecture & Numerical Rigor
- `Mycel.Core` MUST NOT reference any UI framework (no Avalonia, no WPF, no Silk.NET, no OpenGL).
- Deterministic Numerics: Fixed reduction order in parallel loops (e.g. dot products, norm calculations).
- Zero Allocations in Hot Loops: Pre-allocate all scratch buffers, vectors, and matrices outside PCG and OC iteration loops.
- License & Dependencies:
  - Project License: **GNU General Public License v3.0 (GPLv3)**.
  - Authorized NuGet Packages only: `Avalonia`, `CommunityToolkit.Mvvm`, `Silk.NET.OpenGL`, `ScottPlot.Avalonia`, `Material.Icons.Avalonia`, `xunit`.
  - No new NuGet package may be added without prior explicit user approval in `docs/DECISIONS.md`.
- **Additive Manufacturing Infill & Cellular Mechanics Invariant**:
  - Infill percentage ($\rho_{infill} \in [10\%, 100\%]$) and morphology pattern (`Gyroid`, `Grid`, `Honeycomb`, `Rectilinear`, `Triangles`) must physically scale effective elastic stiffness and yield strength via Gibson-Ashby cellular solid laws:
    $$E_{eff} = E_0 \cdot \left(\frac{\rho_{infill}}{100}\right)^p, \quad \sigma_{y,eff} = \sigma_{y,0} \cdot \left(\frac{\rho_{infill}}{100}\right)^m$$
  - All FEA static analysis solves, baseline compliance calculations, and topology optimization solves must consistently evaluate using $E_{eff}$ and $\sigma_{y,eff}$ rather than unscaled bulk solid properties when infill is below 100%.

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
- **Voxelization of Multi-Body Assemblies (Union vs Parity)**: Voxel ray-casting must use the Non-Zero Winding Rule ($\vec{D} \cdot \vec{N} < 0 \implies +1, \vec{D} \cdot \vec{N} > 0 \implies -1$, solid when $\text{winding} > 0$) rather than even-odd parity to ensure intersecting closed shells in CAD assemblies are fused via boolean Union ($A \cup B$) without excavating internal void cavities.
- **Structural Voxel Connectivity**: Connected component analysis and filtering on structured Hex8 grids must employ 26-connectivity (sharing vertices, edges, or faces). Hex8 stiffness matrices couple DOFs across shared nodes, so 6-connectivity erroneously disconnects and erodes diagonal or tangential features (e.g., cylindrical pins, thin fillets).
- **Interactive Picking State Disarm**: Any interactive 3D viewport modal state (picking, region selection banner, hover highlight) must be unconditionally disarmed upon step transition, stepper header click, or workflow action execution (`Prepare`, `Analyze`, `Save`, `Load`), clearing hover geometry and requesting viewport re-rendering.
- **Modal Dialog Keyboard & State Lifecycle**: Any modal overlay dialog (such as User Guide, About, or Confirmation prompts) must disarm active 3D picking upon opening and handle the `Esc` key with `e.Handled = true` to dismiss top-level overlays before bubbling to other viewport or application shortcuts.
- **Dynamic Picking Region Growing on Angle Adjustments**:
  - While interactive surface picking is active, adjusting the dihedral angle threshold slider must dynamically re-grow from the last clicked seed triangle (`LastClickedSeedTriangle`), merging with previously selected patches (`_baseTrianglesBeforeLastClick`) using the active modification mode (`Replace`, `Add`, `Subtract`) via `RegionSelector.ModifySelection`.
  - Disarming picking (via `Esc`, Stepper header click, or `Done`) must reset `LastClickedSeedTriangle = -1` and restore replacement mode.
- **NumericUpDown Input Sizing & Text Clipping Invariant**:
  - In Avalonia desktop UI, `NumericUpDown` controls contain up/down spinner buttons consuming 36–40px of internal horizontal space. For high-magnitude physical values (e.g. Grams up to 1,000,000,000, Newtons up to 10,000,000) or multi-digit floating point numbers, fixed `Width` below 200px causes right-hand numerical clipping. Controls binding large numeric ranges must specify sufficient width (e.g., `Width="205"` in sidebar panels) and compact typography (`FontSize="11"`) or flexible grid columns to guarantee complete readability across all valid ranges.
- **Repository Brand Hygiene & Forbidden Word Scan Invariant**:
  - In rebranded repositories governed by automated keyword scan regression tests (such as `SourceScanTests`), milestone documentation (`docs/STEP*.md`), release notes, and markdown summaries must never mention obsolete product names in plain text unless explicitly added to the test allowlist. Authors must use generic terminology ("legacy application", "former product name") to preserve 100% green source-scan gates.
- **Pre-Publication Public Git Audit Protocol**:
  - Before pushing a repository to a public remote, an automated pre-flight hygiene scan must verify:
    1. Commit authors/emails (`git log --all --format='%an <%ae>' | sort -u`): any personal email requires explicit user confirmation.
    2. Tracked file sizes (< 5 MB) and large blobs in history.
    3. Absence of secret/credential patterns (`.env`, `*.pfx`, `*.key`, tokens, passwords) across all revisions.
    4. Absence of absolute personal paths (`/home/<user>`, `C:\Users\<user>`) in tracked text files.
    5. Cleanliness of ignored artifacts: no `bin/`, `obj/`, `dist/`, `runs/` or generated STLs tracked.
