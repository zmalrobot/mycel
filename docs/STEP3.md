# Step 3 — Matrix-Free Hex8 FEM Solver, 8-Colour PCG & Numerical Verification

## 1. Overview & Architecture

Step 3 delivers the complete Finite Element Method (FEM) simulation core of **TopoOpt**, located entirely in `TopoOpt.Core.Fem` with **zero UI dependencies**.

Key deliverables implemented:
1. **`FeMaterial`**: Elastic isotropic material definition ($E_0$, $\nu$) with configurable artificial void stiffness ratio $E_{\min} / E_0$ (default $10^{-6}$).
2. **`LoadCase` & Consistent Force Assembly**: Apportions total force vectors $\mathbf{F}_{\text{total}}$ to surface patch boundary nodes weighted strictly by exposed tributary quad face areas ($\sum \mathbf{F}_{\text{node}} = \mathbf{F}_{\text{total}}$ within $10^{-12}$ relative).
3. **`FeModel`**: Structured grid model containing active domain masking, element densities ($\rho_e \in [0, 1]$), penalisation exponent $p$, boundary support DOFs (`FixedDofs`), and multiple load cases.
4. **`Hex8Element`**: Rigorous derivation of the trilinear 8-node hexahedral element stiffness matrix $\mathbf{K}_e$ via 2×2×2 Gauss quadrature. Includes precomputed diagonal, centroid strain-displacement matrix $\mathbf{B}_c$, and mathematical proof of the scaling law $\mathbf{K}_e(h) = h \cdot \mathbf{K}_e(1)$.
5. **`FemOperator`**: Matrix-free element-by-element operator evaluating $\mathbf{y} = \mathbf{K}(\rho)\mathbf{x}$ using an 8-colour regular lattice decomposition (`(ix & 1) | ((iy & 1) << 1) | ((iz & 1) << 2)`). Ensures 100% race-free, atomic-free parallel evaluations across threads with bit-identical determinism.
6. **Constrained Operator $\mathbf{A} = \mathbf{P} \mathbf{K} \mathbf{P} + (\mathbf{I} - \mathbf{P})$**: Projects out fixed DOFs and unmeshed inactive DOFs with identity rows, guaranteeing strict SPD preservation.
7. **`PcgSolver`**: Double-precision Preconditioned Conjugate Gradient linear solver with Jacobi (diagonal) preconditioning. Completely allocation-free inside the iteration loop; enforces deterministic reductions with fixed summation order.
8. **`StaticAnalysis`**: End-to-end static solver returning nodal displacements $\mathbf{u}$, compliance $C = \mathbf{F}^T \mathbf{u}$, support reactions $\mathbf{R} = \mathbf{K} \mathbf{u}$ at fixed DOFs, centroid von Mises stress $\sigma_{vM}$ per element, max displacement, and trilinear spatial interpolation/averaging helpers.

---

## 2. Mathematical Formulation & Derivations

### 2.1 Hex8 Element Stiffness Scaling
- For a cubic element of size $h$, spatial derivatives scale as $\frac{\partial N_a}{\partial x} = \frac{2}{h} \frac{\partial N_a}{\partial \xi}$, implying $\mathbf{B}(h) = \frac{1}{h} \mathbf{B}(1)$.
- Volume differential scales as $dV = \det(\mathbf{J}) d\xi d\eta d\zeta = \frac{h^3}{8} d\xi d\eta d\zeta = h^3 dV(1)$.
- Therefore:
  $$\mathbf{K}_e(h) = \int_V \mathbf{B}(h)^T \mathbf{D} \mathbf{B}(h) dV = \frac{h^3}{h^2} \int_{[-1, 1]^3} \mathbf{B}(1)^T \mathbf{D} \mathbf{B}(1) dV(1) = h \cdot \mathbf{K}_e(1)$$
- Element stiffness matrix $\mathbf{K}_e(1)$ is generated once for a given Poisson ratio $\nu$ and scaled directly by $h$.

### 2.2 8-Colour Lattice Parallelisation
- Active elements are partitioned into 8 disjoint sets by parity coordinates:
  $$c = (i_x \bmod 2) + 2(i_y \bmod 2) + 4(i_z \bmod 2) \in \{0, \dots, 7\}$$
- Any two elements with the same colour index $c$ have $|i_x - j_x| \ge 2$, $|i_y - j_y| \ge 2$, or $|i_z - j_z| \ge 2$, which guarantees they share **zero nodes**.
- Iterating colours sequentially ($c = 0 \to 7$) and parallelising over elements within colour $c$ allows direct scattering into the global vector $\mathbf{y}$ without memory locks, atomics, or thread races.
- Reduction order is strictly deterministic, yielding bit-identical outputs across multiple executions.

### 2.3 Boundary Condition Projection
- The unconstrained matrix-free operator $\mathbf{K}$ operates on all nodes.
- Prescribed support DOFs and all DOFs of unmeshed/inactive nodes are zeroed via projection matrix $\mathbf{P}$.
- The linear operator solved is:
  $$\mathbf{A} = \mathbf{P} \mathbf{K} \mathbf{P} + (\mathbf{I} - \mathbf{P}), \quad \mathbf{b} = \mathbf{P} \mathbf{F}$$
- $\mathbf{A}$ is strictly SPD, avoiding solver breakdown on exterior nodes. Support reactions are recovered via $\mathbf{R} = \mathbf{K} \mathbf{u}$ on the fixed DOFs.

---

## 3. Numerical Verification (V1–V9)

All specification tests were authored before the solver implementation and pass cleanly:

| Test ID | Benchmark Description | Acceptance Criterion | Result | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Ke1** | $\mathbf{K}_e$ Symmetry | $|K_{ij} - K_{ji}| < 10^{-12}$ | $\max \text{diff} < 10^{-16}$ | **PASS** |
| **Ke2** | Rigid Body Translations (X, Y, Z) | $\mathbf{K}_e \mathbf{u}_{\text{trans}} = \mathbf{0}$ to $10^{-12}$ | $\max |F_i| < 10^{-16}$ | **PASS** |
| **Ke3** | Rigid Body Rotations (X, Y, Z) | $\mathbf{K}_e \mathbf{u}_{\text{rot}} = \mathbf{0}$ to $10^{-12}$ | $\max |F_i| < 10^{-15}$ | **PASS** |
| **Ke4** | Uniaxial Patch Test ($\nu = 0.3$) | Consistent forces vs analytical stress state to $10^{-12}$ | $\max \text{diff} < 10^{-15}$ | **PASS** |
| **Ke5** | Pure Shear Patch Test | Consistent forces vs $G \gamma$ to $10^{-12}$ | $\max \text{diff} < 10^{-15}$ | **PASS** |
| **Ke6** | $\mathbf{K}_e(h) = h \cdot \mathbf{K}_e(1)$ Scaling | Valid across $h \in \{0.5, 2.0, 3.5, 10.0\}$ to $10^{-12}$ | $\max \text{diff} < 10^{-15}$ | **PASS** |
| **V1** | Axial Bar ($\nu = 0$) | $u_{\text{tip}} = PL/(AE)$ within $10^{-6}$, $\sigma_{vM} = P/A$ within $10^{-6}$ | Error $< 10^{-8}$ | **PASS** |
| **V2** | Axial Bar Minimal Restraints ($\nu = 0.3$) | $u_{\text{tip}} = PL/(AE)$ within $10^{-6}$ | Error $< 10^{-8}$ | **PASS** |
| **V3** | Cantilever Convergence (Timoshenko) | Monotonic error decrease across $N_z \in \{1, 2, 4, 8\}$; $\le 8\%$ at $N_z = 8$ | Monotonic: **YES**, $N_z=8$: **1.64%** | **PASS** |
| **V4** | Equilibrium | $\sum \mathbf{R} = -\sum \mathbf{F}$ ($10^{-8}$); Moment equilibrium ($10^{-8}$) | Relative force $< 10^{-10}$, moment $< 10^{-8}$ | **PASS** |
| **V5** | Energy Invariant | $C = \mathbf{F}^T \mathbf{u} \equiv \mathbf{u}^T \mathbf{K} \mathbf{u}$ to $10^{-8}$ | Relative error $< 10^{-10}$ | **PASS** |
| **V6** | Mirror Symmetry | Symmetric beam/load $\implies$ symmetric $\mathbf{u}$ to $10^{-9}$ | Discrepancy $< 10^{-14}$ | **PASS** |
| **V7** | Elastic Scaling | $E \to 2E \implies u \to u/2, C \to C/2$; $L \to 2L \implies C \to C/2$ | Exact to $10^{-10}$ | **PASS** |
| **V8** | Robustness | Supports $< 6$ DOFs throws error; disconnected island converges | Exception verified; convergence verified | **PASS** |
| **V9** | Consistency & Bit-Determinism | Parallel vs scalar reference to $10^{-12}$; 2 runs bit-identical | Rel diff $< 10^{-15}$; Bit-identical: **YES** | **PASS** |

---

## 4. Performance Gate Findings & Limits (Section 6)

Detailed numbers are recorded in `docs/STEP3_BENCH.md`:
- **Matrix-Free Operator Throughput**:
  - $N_{\text{active}} \approx 100\text{k}$ elements (320k DOFs): **6.74 ms** per matvec.
  - $N_{\text{active}} \approx 500\text{k}$ elements (1.53M DOFs): **29.63 ms** per matvec.
  - Multi-threading scaling: **8.39× speed-up** on 20 logical cores.
  - Memory footprint: $64.4\text{ MB}$ at 100k elements, $263.9\text{ MB}$ at 500k elements.
- **Performance Gate**:
  - Warm-started PCG at 100k active elements with 2% density change required 915 iterations (**10.06 seconds**).
  - Condition: $\le 3.0\text{ s}$ warm solve at 100k elements was **NOT MET** using simple Jacobi diagonal preconditioning on slender beam geometries.
- **Architectural Directive**:
  - As instructed in Section 6, multigrid development is deferred. Measured metrics and recommendations (Geometric MG-PCG, Fast Draft resolution presets at 30k–50k) are documented in `docs/STEP3_BENCH.md`.
