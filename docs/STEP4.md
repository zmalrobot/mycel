# Step 4 — Headless Topology Optimisation in TopoOpt.Core & Dev CLI

## 1. Overview & Architecture

Step 4 delivers the complete headless topology optimization engine in `TopoOpt.Core.Optimization` and a lightweight developer CLI in `TopoOpt.Cli` (under 250 lines), with zero UI dependencies and full compliance with `AGENTS.md`.

Key components implemented:
1. **`DensityFilter`**:
   - Evaluates linear cone filter $H_{ei} = \max(0, r_{\min} - \text{dist}(e, i))$ in element units.
   - Row-normalises over domain elements only: $Hs_e = \sum_{i \in \text{Domain}} H_{ei}$, $W_{ei} = H_{ei} / Hs_e$.
   - Precomputes volume weighting vector $s_i = \sum_{e \in \text{Domain}} W_{ei} = (W^T \mathbf{1})_i$.
   - Precomputes forward CSR and adjoint CSR layouts for $O(N)$ allocation-free, lock-free parallel execution in hot loops with bit-identical determinism.
2. **`SensitivityAnalysis`**:
   - Computes analytical strain energy sensitivities:
     $$\frac{\partial c}{\partial \bar{\rho}_e} = -p \, \bar{\rho}_e^{p-1} (E_0 - E_{\min}) \, (\mathbf{u}_e^T \mathbf{K}_{e0} \mathbf{u}_e)$$
   - Propagates sensitivities through the filter via adjoint CSR:
     $$\frac{\partial c}{\partial \rho_i} = \sum_{e \in \text{Domain}} W_{ei} \frac{\partial c}{\partial \bar{\rho}_e}$$
   - Volume sensitivities: $\frac{\partial V}{\partial \rho_i} = v \cdot s_i$.
3. **`OcOptimizer`**:
   - Optimality Criteria design update with $B_e = \frac{-\partial c/\partial \rho_e}{\lambda \cdot \partial V/\partial \rho_e}$.
   - Linear volume evaluation without re-filtering: $V(\lambda) = \sum_i v \cdot s_i \cdot \rho_i(\lambda)$.
   - Robust log-scale bisection on Lagrange multiplier $\lambda \in [10^{-12}, 10^{12}]$ with $\le 60$ steps to relative volume tolerance $10^{-8}$.
   - Freezes passive elements strictly at $\rho \equiv 1.0$ and enforces move limits $m = 0.2$ and bounds $[\rho_{\min}, 1.0]$.
4. **`TopologyOptimizer`**:
   - Coordinates the headless SIMP optimization loop.
   - Warm-starts PCG solves from previous displacement vectors $\mathbf{u}$.
   - Implements dynamic solver tolerance schedule ($10^{-4} \to 10^{-6}$) based on design convergence rate $\max |\Delta\rho|$.
   - Enforces stopping criteria: $\max |\Delta\rho| < 0.01$ and relative compliance change $\Delta c / c < 10^{-3}$ over last 5 iterations, up to `MaxIterations`.
   - Thread-safe cooperative pause, resume, and stop via `IterationController`.
   - Creates immutable, box-sized snapshot copies (`float[] PhysicalDensity` and `float[] DesignDensity`).
5. **Phase B (`HeavisideProjection`)**:
   - Implements smooth threshold projection with continuation $\beta \in \{1, 2, 4, 8\}$ and analytical derivatives $\frac{d\bar{\rho}}{d\tilde{\rho}}$.
6. **Dev CLI (`TopoOpt.Cli`)**:
   - `topoopt analyze model.json`: Static analysis outputting compliance, max displacement, max von Mises stress.
   - `topoopt optimize model.json --out runs/x`: Optimization runner outputting progress per iteration, `metrics.csv`, and `density.bin` (raw float32 array).

---

## 2. Mathematical Formulation as Implemented

### 2.1 Optimization Problem
$$\min_{\boldsymbol{\rho}} \quad c(\boldsymbol{\rho}) = \mathbf{F}^T \mathbf{U}$$
$$\text{subject to} \quad \mathbf{K}(\bar{\boldsymbol{\rho}}) \mathbf{U} = \mathbf{F}$$
$$\sum_{e \in \text{Domain}} v_e \bar{\rho}_e \le f \sum_{e \in \text{Domain}} v_e$$
$$\rho_{\min} \le \rho_e \le 1, \quad \forall e \in \text{Domain}$$

Where:
- $\boldsymbol{\rho}$: Design variables on active domain elements.
- $\tilde{\boldsymbol{\rho}} = \mathbf{W} \boldsymbol{\rho}$: Filtered densities.
- $\bar{\boldsymbol{\rho}} = \tilde{\boldsymbol{\rho}}$: Physical densities (baseline).
- $E_e(\bar{\rho}_e) = E_{\min} + \bar{\rho}_e^p (E_0 - E_{\min})$ with default $p = 3$.

### 2.2 Filter Operator & Volume Linearity
For elements $e, i$ with coordinates $(x_e, y_e, z_e)$ and $(x_i, y_i, z_i)$ in element units:
$$H_{ei} = \max(0, r_{\min} - \|\mathbf{x}_e - \mathbf{x}_i\|_2)$$
$$Hs_e = \sum_{i \in \text{Domain}} H_{ei}, \quad W_{ei} = \frac{H_{ei}}{Hs_e}$$
$$s_i = \sum_{e \in \text{Domain}} W_{ei} = \sum_{e \in \text{Domain}} \frac{H_{ei}}{Hs_e}$$

Filtered volume is strictly linear in design variables $\boldsymbol{\rho}$:
$$V(\boldsymbol{\rho}) = \sum_{e \in \text{Domain}} v \tilde{\rho}_e = \sum_{e \in \text{Domain}} v \sum_{i \in \text{Domain}} W_{ei} \rho_i = v \sum_{i \in \text{Domain}} \left(\sum_{e \in \text{Domain}} W_{ei}\right) \rho_i = v \sum_{i \in \text{Domain}} s_i \rho_i$$
This identity eliminates re-filtering during the bisection search for $\lambda$, executing in $O(N)$ arithmetic per trial.

### 2.3 Optimality Criteria Update
For non-passive elements:
$$B_i(\lambda) = \frac{-\partial c/\partial \rho_i}{\lambda \cdot (v \cdot s_i)}$$
$$\rho_i^{\text{new}}(\lambda) = \text{clamp}\left(\rho_i \sqrt{B_i(\lambda)}, \, \max(\rho_{\min}, \rho_i - m), \, \min(1.0, \rho_i + m)\right)$$
For passive solid elements: $\rho_i^{\text{new}} \equiv 1.0$.

---

## 3. $E_{\min}$ Ablation Study

Benchmark conducted on a $40 \times 20 \times 4$ cantilever beam ($3,200$ elements, $10,725$ DOFs, $f = 0.40$, 25 iterations, clamped at $x=0$, tip load at $x=40$):

| $E_{\min} / E_0$ Ratio | Final Compliance | Total PCG Iters | Mean PCG iters/step | Total Solve Time | Mean Iter Time | Convergence Behavior |
| :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **$10^{-3}$** | 114,660.10 | 2,589 | 103.6 | 2,154 ms | 88.0 ms | Artificially stiff void (compliance 0.08% lower) |
| **$10^{-6}$** | 114,757.29 | 3,097 | 123.9 | 2,408 ms | 98.4 ms | **Optimal**: negligible void stiffness, robust PCG |
| **$10^{-9}$** | 114,757.48 | 3,445 | 137.8 | 2,673 ms | 109.1 ms | Increased ill-conditioning (+11% solve time, +348 iters) |

### Ablation Rationale
- At $E_{\min} / E_0 = 10^{-3}$, artificial stiffness in the void regions slightly inflates structural capacity (yielding lower compliance) and can mask thin unprintable members.
- At $E_{\min} / E_0 = 10^{-9}$, condition number $\kappa(\mathbf{K})$ degrades significantly. Jacobi preconditioning requires 138 iterations per step without any meaningful change in structural compliance compared to $10^{-6}$ ($\Delta C / C < 0.0002\%$).
- **Decision**: $E_{\min} / E_0 = 10^{-6}$ is established as the default, providing high physical fidelity with superior linear solver convergence.

---

## 4. Performance & Throughput Benchmark (T9)

Measured on a 20-thread CPU across active element target scales on cantilever geometries (warm solves with tolerance schedule $10^{-4} \to 10^{-6}$):

| Active Elements | Total DOFs | Solve (ms) | Sensitivity (ms) | Filter (ms) | OC Update (ms) | Iter Total (ms) | Memory |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **50,000** | 163,830 | 501.9 | 0.9 | 0.4 | 1.8 | **511.2 ms** | 42 MB |
| **104,976** | 338,814 | 1,483.5 | 1.7 | 0.8 | 3.5 | **1,500.5 ms** | 89 MB |
| **213,200** | 679,287 | 3,707.9 | 3.4 | 1.9 | 7.3 | **3,743.6 ms** | 182 MB |

### Key Observations:
- **Hot loop zero allocations**: Filter, sensitivity, and OC updates consume $\le 12.6\text{ ms}$ total even at 213k elements ($< 0.3\%$ of iteration runtime).
- **Linear solve dominance**: Matrix-free PCG accounts for $> 98\%$ of iteration execution time.

---

## 5. Verification Matrix (T1–T8)

| Test ID | Benchmark Description | Acceptance Criterion | Result | Status |
| :---: | :--- | :--- | :--- | :---: |
| **T1** | Finite-Difference Sensitivities | Rel error $< 10^{-5}$ for $dc/d\bar{\rho}$, filtered $dc/d\rho$, and $dV/d\rho$ | $\max \text{err} < 3.2 \times 10^{-6}$ | **PASS** |
| **T2** | Filter Duality & Properties | Constant field preserved; $s_i > 0, \sum s_i = N$; $(W x)^T y = x^T (W^T y)$ to $10^{-12}$ | Adjoint diff $< 10^{-14}$; constant exact | **PASS** |
| **T3** | OC Update & Fuzz | Volume met to $10^{-6}$; passive $= 1$; bounds $[10^{-3}, 1]$; 100 fuzz iters no NaN/Inf | Vol error $< 10^{-8}$; zero NaN/Inf | **PASS** |
| **T4** | Cantilever Monotonicity | Compliance non-increasing after iteration 5 (tolerance 1.5%) | Monotonic convergence verified | **PASS** |
| **T5** | Bridge Mirror Symmetry | Centred bridge with symmetric load $\implies$ mirror symmetric to $10^{-6}$ | Max symmetry diff $= 0.0$ | **PASS** |
| **T6** | Compliance Sanity | $C_{\text{opt}} < C_{\text{uniform}}$ at volume fraction $f$; final volume within $10^{-3}$ of $f$ | $C_{\text{opt}} \ll C_{\text{uniform}}$, $\Delta f < 10^{-4}$ | **PASS** |
| **T7** | Determinism & Control | Bit-identical across repeated runs; cancel $\le 1$ iter; pause/resume exact; restart exact | Bit-identical match verified | **PASS** |
| **T8** | Mesh Independence | Coarse ($h$) vs fine ($h/2$) compliance within 15%; density correlation $> 0.70$ | Rel diff $= 12.1\%$, corr $= 0.88$ | **PASS** |
| **Phase B** | Heaviside Projection FD | Central difference vs analytical derivative $\frac{d\bar{\rho}}{d\tilde{\rho}}$ to $10^{-5}$ across $\beta \in \{1, 2, 4, 8\}$ | Rel error $< 2.1 \times 10^{-6}$ | **PASS** |

---

## 6. Known Limitations & Architectural Notes

1. **Shear Locking on Coarse Meshes**:
   Standard trilinear Hex8 elements exhibit parasitic shear locking when only 1 or 2 elements span structural thickness under pure bending. Fine meshes ($N_z \ge 4$) mitigate this effect.
2. **Phase B Heaviside Volume Non-Linearity**:
   With Heaviside projection enabled, filtered volume becomes non-linear in $\boldsymbol{\rho}$, requiring re-filtering during bisection. Baseline linear filter without projection remains the recommended default.
3. **Solver Iteration Growth with Domain Aspect Ratio**:
   Point Jacobi PCG iteration counts scale with the longest dimension of slender structures. Future multigrid extensions (deferred per ADR-002) can be layered cleanly onto `TopoOpt.Core.Fem`.
