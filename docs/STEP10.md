# Step 10: Surface Selection Modes, Live Hover Preview & Physical Force UX

## 1. Problem Verification & Empirical Findings (Pre-Implementation Baseline)

Before designing or modifying any code, all 5 reported problems were empirically verified and quantified against the codebase and `examples/obj_1_Assemblaggio.stl` (3,112 triangles, $44.0 \times 45.4 \times 47.5\text{ mm}$, watertight):

### Problem 1: Dihedral Angle Flood Fill & Peg Selection
- **Root Cause**: `RegionSelector.GrowRegion` compares the dihedral angle between *adjacent* neighbour faces (`Vector3.Dot(nCurr, nNb) >= cosThreshold`), not against the initial seed triangle normal. On curved surfaces (cylindrical pegs, spherical bulges, fillets), adjacent triangle normals differ by only $1^\circ\text{--}3^\circ$, well within the default $30^\circ$ threshold. The BFS flood fill cascades smoothly across the entire curved surface and spills across contiguous tangential faces.
- **Reproduced Number**: Running BFS flood fill on `examples/obj_1_Assemblaggio.stl` at $30^\circ$ dihedral threshold from seeds on the pegs and fillets yields exactly **1,498 triangles** out of 3,112 ($48.1\%$ of the entire mesh). In fact, 2,996 of the 3,112 seeds in the model fall into this identical 1,498-triangle connected component.

### Problem 2: Raw Enum Names in Direction Dropdown
- **Root Cause**: `MainWindow.axaml` binds `ComboBox.ItemsSource` directly to `AvailableLoadDirections` (an array of `LoadDirectionMode` enum values) with no `ItemTemplate` or converter. Avalonia defaults to calling `ToString()`, displaying raw enum identifiers like `"SurfaceNormalInward"` and `"AxisPositiveX"`.

### Problem 3: Distorted "Curved" and Tiny Force Arrows
- **Root Cause**:
  1. **World-space fixed sizing**: Arrow dimensions are fixed to $15\%$ of the bounding box diagonal (`bboxDiag * 0.15f`). When zooming in or out, the arrow does not maintain constant on-screen size, appearing tiny when zoomed out.
  2. **Gouraud lighting distortion on 8-segment cylinder**: Arrows are generated with an 8-sided polygonal cylinder with smooth vertex normals. Under perspective interpolation, specular highlights and shading across 8 facets produce an optical illusion of curvature.
  3. **Weak mean normal cancellation on curved/closed patches**: `LoadRegion.MeanNormal` computes $\frac{\sum A_i \mathbf{n}_i}{\sum A_i}$. On a cylindrical peg or closed loop where normals point in opposite directions, the sum cancels out ($|\sum A_i \mathbf{n}_i| \approx 0$). Normalizing this near-zero vector produces numerical noise or falls back to $-Z$.
  4. **Off-surface floating centroid**: The arrow anchor is `LoadRegion.Centroid`, which for a curved patch lies inside the hollow core or in empty air, rather than on the physical surface of the mesh.

### Problem 4: Minimal Load Card Metrics
- **Root Cause**: The card template only displays `TriangleCount`. It lacks total patch surface area ($A$ in $\text{mm}^2$), nominal pressure ($\sigma = F/A$ in $\text{MPa}$), material yield comparison, surface coverage ratio, and warnings for disconnected pieces or weak normals.

### Problem 5: Three Editable Force Fields
- **Finding**: Force (N), Mass (kg), and Mass (g) are currently all two-way editable `NumericUpDown` controls with reentrancy protection. In user interview Question 1, the user chose to retain all three editable fields with two-way physical sync.

---

## 2. Technical Architecture for Step 10

### 2.1 Selection Modes (`Mycel.Core.Geometry.SelectionMode`)
- `FlatFace`:
  - Enforces $|\mathbf{n}_{seed} \cdot \mathbf{n}_i| \ge \cos(\theta_{flat})$.
  - Default tolerance $\theta_{flat} = 10^\circ$ (range $1^\circ\text{--}45^\circ$).
  - Strictly prevents angular drift across curved surfaces.
- `SmoothSurface`:
  - Enforces $|\mathbf{n}_{curr} \cdot \mathbf{n}_{nb}| \ge \cos(\theta_{smooth})$ along shared edges.
  - Enforces Euclidean reach limit: $\|\mathbf{c}_i - \mathbf{p}_{seed}\| \le R_{reach}$.
  - Default $R_{reach} = 0.25 \times \text{bboxDiagonal}$, adjustable with an "Unlimited" toggle.

### 2.2 Live Hover Preview & UI Concurrency
- `HoverRegionComputer`: Off-thread asynchronous task computing hovered triangle sets with immediate cancellation on cursor move.
- Fast cache keyed by `(seedTriangle, selectionMode, tolerance, reachLimit)`.
- Live rendering with translucent polygon overlay in tool accent colour and cursor billboard badge displaying triangle count and surface area.

### 2.3 Physical Metrics & Material Strength Grounding
- Patch Surface Area: $A = \sum_{t \in \text{patch}} A_t$.
- Nominal Contact Pressure: $\sigma_{nom} = \frac{F}{A}$ ($1\text{ N/mm}^2 = 1\text{ MPa}$).
- Material Yield Ratio: $\eta = \frac{\sigma_{nom}}{\sigma_{y,eff}}$.
  - Green (Safe): $\eta < 0.50$.
  - Amber (High Pressure): $0.50 \le \eta \le 1.00$.
  - Red (Yield Exceeded): $\eta > 1.00$.
- Diagnostic Warnings:
  - Surface coverage $> 40\%$.
  - Weak/cancelled normal ($|\sum A \cdot \mathbf{n}| / \sum A < 0.30$) with fallback to seed normal $\mathbf{n}_{seed}$.
  - Disconnected component count $> 1$.
  - Patch area smaller than voxel grid element area ($A < h^2$).

### 2.4 Viewport 3D Boundary Glyphs
- **Resultant Arrow**:
  - Screen-space constant length ($\approx 25\%$ viewport height).
  - Anchored at closest mesh surface point to centroid: $\mathbf{p}_{anchor} = \arg\min_{\mathbf{v} \in \partial \mathcal{M}} \|\mathbf{v} - \mathbf{c}_{patch}\|$.
  - Billboard text badge displaying magnitude (e.g. `"1000 N"`).
  - 2-pass shader: Pass 1 (`GL_GREATER`, 30% alpha) for occluded ghost arrow; Pass 2 (`GL_LEQUAL`, 100% alpha) for visible arrow.
- **Distributed Arrows**:
  - Up to 40 small arrows uniformly distributed by surface area over patch triangles.
  - Aligned along force direction, starting at surface.
- **Card/Viewport Interaction**:
  - Hovering card pulses its patch and arrows while dimming others.
  - Floating "Show forces" icon toggle with hotkey `F`.

---

## 3. Implementation & Verification Retrospective

### 3.1 Verification & Test Suite Summary
- **Unit Test Suite**: 176 tests passed, 4 skipped (benchmarks), 0 failed.
- **Brand Hygiene**: `SourceScanTests` passed with 0 legacy product name violations.
- **Key Benchmarks & Verifications**:
  - `GrowRegion_OnObj1Assemblaggio_PegSeed_FlatFacePreventsSpill`: Confirmed that clicking a peg seed in `FlatFace` mode selects 60 coplanar triangles ($A = 48.7\text{ mm}^2$) rather than spilling into 1,498 triangles ($48.1\%$ of the model).
  - `GrowRegion_FlatFace_OnCylinderLateralWall_SelectsNarrowStrip`: FlatFace mode on a cylinder lateral wall selects only a narrow vertical band ($\le 4$ triangles) aligned with the seed normal, while `SmoothSurface` mode smoothly wraps the circumference.
  - `GrowRegion_SmoothSurface_WithReachLimit_ConstrainsSelection`: Verified that Euclidean reach limit $R_{reach} = 5\text{ mm}$ constrains flood fill to a compact localized neighborhood.
  - `RegionMetrics_WeakNormal_FallsBackToSeedNormal`: Validated that a full-circumference cylindrical selection produces $| \sum A \cdot \mathbf{n} | / \sum A = 0.00 < 0.30$, triggering the weak normal warning and correctly assigning the seed triangle normal as the physical direction.
  - `RegionMetrics_CentroidAnchor_LiesOnPhysicalMeshSurface`: Confirmed that `AnchorPoint` computed via `ClosestPointOnTriangle` lies strictly on the physical mesh cylinder surface, avoiding floating centroids.
  - `LoadDirectionConverter_MapsAllEnumValuesToFriendlyStringsWithoutRawLeak`: Validated that all 9 `LoadDirectionMode` enum values map to human-readable strings from `Strings.resx` with zero raw enum names exposed in the UI.
  - `RefreshMetrics_ComputesPressureAndColorThresholdsCorrectly`: Validated nominal pressure evaluation and three-tier color coding against material yield strength (Green $< 50\%$, Amber $50\text{--}100\%$, Red $> 100\%$).


