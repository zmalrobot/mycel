# Mycel User Guide

Mycel is a desktop application for 3D finite element analysis (FEA) and SIMP topology optimisation of parts intended for FDM/FFF 3D printing. All calculations use **millimetres (mm)**, **Newtons (N)**, and **Megapascals (MPa)** in a right-handed coordinate system with **Z up** (the print build direction).

---

## 1. Open a Model (Step 1: Model)
1. Click **Open STL** in the top toolbar (or drag and drop an `.stl` file onto the 3D viewport).
2. Review the mesh metrics in **Step 1: Model** (triangle count, bounding box dimensions, volume, and watertight status).
3. If the bounding box dimensions appear unusually small or large, the **Units Validation Guard** prompts you to keep millimetres or rescale from metres or inches.
4. Use **Save Project** / **Open Project** to persist or reload a project directory (`project.json` and `model.stl`). Project loading verifies the `model.stl` SHA-256 hash so saved triangle selections always match the geometry, with debounced non-blocking autosave.

## 2. Define Loads, Supports, and Preserve Regions (Steps 2 & 3)
Select surfaces on the 3D model by adjusting the dihedral angle limit slider and clicking faces in the viewport:
- **Step 2: Loads**:
  - Click **+ Load**, pick the target surface triangles on the mesh, and set the force **Magnitude [N]**.
  - Choose the **Direction Mode**: *Normal Inward*, *Normal Outward*, *Global X / Y / Z*, or a *Custom Vector*.
  - Orange 3D cylindrical arrows with conical heads (`#FF9800`) appear at each load region's centroid pointing along the resultant force.
- **Step 3: Supports**:
  - Click **+ Support** and pick the constrained surface triangles.
  - Choose the fixed degrees of freedom (**DOF**): *All Fixed*, *Fixed X*, *Fixed Y*, *Fixed Z*, or a custom combination.
  - Blue 4-sided pyramid anchor glyphs (`#2196F3`) mark supported regions in the viewport.
- **Preserve Regions**:
  - Add optional **Preserve** regions for functional surfaces (bolt bosses, mating faces) that must remain solid during optimisation. A protective passive solid shell around load and support patches is also enforced automatically.

## 3. Pick a Material (Step 4: Material)
1. Open **Step 4: Material** and choose a preset grounded for FDM 3D printing (**PLA**, **PLA+**, **PETG**) or **Custom**:
   - **PLA**: $\sigma_{y,XY} = 60\text{ MPa}$, $\sigma_{y,Z} = 30\text{ MPa}$
   - **PLA+**: $\sigma_{y,XY} = 55\text{ MPa}$, $\sigma_{y,Z} = 35\text{ MPa}$
   - **PETG**: $\sigma_{y,XY} = 50\text{ MPa}$, $\sigma_{y,Z} = 32\text{ MPa}$
2. Edit the isotropic Young's modulus $E$, Poisson's ratio $\nu$, in-plane yield strength $\sigma_{y,XY}$, and interlayer Z yield strength $\sigma_{y,Z}$ as needed.
3. Changing material properties preserves the voxelised grid and boundary patches, invalidating only downstream analysis results.

## 4. Choose Resolution & Prepare (Step 5: Resolution & Preview)
1. In **Step 5: Resolution & Preview**, choose a target resolution preset (**Draft ~50k**, **Standard ~100k**, **Fine ~250k**, or **Custom** voxel size $h$).
2. Check the live solve-time and memory estimators.
3. Click **Prepare** to voxelise the mesh, map surface selections to Hex8 boundary patches, build passive regions, and run model validation. Any upstream edit to the mesh, regions, or resolution marks downstream results with an **Outdated** badge.

## 5. Run Static Analysis (Step 5: Resolution & Preview)
1. Once preparation succeeds without validation errors, click **Run FEA** to solve the linear elastic problem on the solid part.
2. Inspect the structural metrics table: **Compliance**, **Max Displacement [mm]**, **Max von Mises Stress [MPa]**, **99th-Percentile Stress ($p_{99}$)**, and the directional **Safety Factors** ($SF_{XY}$ and conservative $SF_Z$).
3. Use the floating **Viewport HUD** to switch the surface scalar field between **Von Mises [MPa]** and **Displacement [mm]**, toggle the colormap between **Turbo** (default) and **Viridis**, and read the colour legend bar showing `min`, $p_{99}$, and `max`.

## 6. Run Topology Optimisation (Step 6: Optimise)
1. Open **Step 6: Optimise** and set the basic parameters:
   - **Volume Fraction**: target fraction of the design domain volume to retain.
   - **Filter Radius [mm]**: minimum feature size control.
   - **Max Iterations**: stopping iteration limit.
2. Optionally expand **Advanced** to adjust the SIMP penalisation exponent, OC move limit, Heaviside projection toggle, and $E_{\min}/E_0$ ratio. Review the pre-run disk usage estimate.
3. Click **Start** (enabled when preparation has no validation errors).
4. Monitor live convergence metrics (iteration, compliance, volume fraction, max density change, grey fraction, PCG solver iterations, and iteration time) alongside the live compliance-vs-iteration chart and ~8 Hz iso-surface viewport updates.
5. Use **Pause**, **Resume**, and **Stop** to control execution cooperatively. Every iteration is saved atomically into `runs/<yyyy-MM-dd_HH-mm-ss>_<name>/` (`run.json`, `domain.bin.br`, `metrics.csv`, `iterations/it_NNNNNN.bin.br`, and `checkpoint_latest.bin.br`), allowing bit-exact continuation of interrupted runs.

## 7. Scrub Iterations & Compare
- Drag the **Timeline Slider** at the bottom of the viewport or click/drag directly on the **Compliance Chart** to scrub through stored iterations.
- Toggle **Ghost Original** to overlay a translucent view of the original STL envelope around the optimised iso-surface.

## 8. Verify & Export to STL
1. Select any iteration on the timeline and click **Verify** to threshold the physical density ($\bar{\rho}_e \ge \tau \to 1.0$), run a static FEA solve on the thresholded voxel geometry, and inspect the resulting von Mises stress distribution on the extracted iso-surface.
2. Adjust the iso-surface density threshold $\tau$ and optional volume-preserving **Taubin Smoothing** iterations ($0..10$, $\lambda = 0.50, \mu = -0.53$).
3. Click **Export STL**. Mycel extracts a closed, watertight 2-manifold triangle mesh via Constrained Surface Nets (with 1-voxel zero-padding and 3D digital-topology well-composedness repair), writes a binary STL with header `"Mycel it=<n> th=<t>"`, and automatically re-reads the file with `StlReader.Read` to verify watertightness and volume.

---

## 9. Limits and Safety
- **Indicative Numerical Results**: All stress distributions, displacements, safety factors, and optimised geometries are numerical approximations based on linear isotropic elasticity and SIMP homogenisation on a uniform Hex8 voxel grid.
- **FDM/FFF Anisotropy & Process Effects**: Extruded 3D prints are inherently anisotropic—interlaminar Z-axis adhesion is substantially weaker than XY filament paths—and part strength depends heavily on slicer perimeters, infill, nozzle temperature, moisture, and print defects.
- **Mandatory Physical Testing**: Never deploy a 3D-printed part in a load-bearing or safety-critical application based solely on simulation output. Always validate the physical printed part under real operating loads before use.

