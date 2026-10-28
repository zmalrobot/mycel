# Finite Element Model Validation & Analytical Benchmarks

This document records the mathematical derivations, boundary condition formulations, analytical benchmark references, and numerical convergence results for the finite element solver in **Mycel.Core**.

---

## 1. Mathematical Derivations

### 1.1 Element Stiffness Matrix $\mathbf{K}_e(h)$ Scaling Law
For an 8-node trilinear hexahedral element (Hex8) of edge length $h$ in a regular structured grid:

1. **Shape Functions in Isoparametric Space $[-1, 1]^3$**:
   $$N_a(\xi, \eta, \zeta) = \frac{1}{8} (1 + \xi_a \xi)(1 + \eta_a \eta)(1 + \zeta_a \zeta), \quad a \in \{0, \dots, 7\}$$

2. **Physical Coordinate Mapping**:
   For a cubic element of size $h$ with origin $\mathbf{x}_0$:
   $$x = x_0 + \frac{1 + \xi}{2} h \implies \frac{\partial \xi}{\partial x} = \frac{2}{h}$$
   $$y = y_0 + \frac{1 + \eta}{2} h \implies \frac{\partial \eta}{\partial y} = \frac{2}{h}$$
   $$z = z_0 + \frac{1 + \zeta}{2} h \implies \frac{\partial \zeta}{\partial z} = \frac{2}{h}$$

3. **Spatial Derivatives and the Strain-Displacement Matrix $\mathbf{B}$**:
   $$\frac{\partial N_a}{\partial x} = \frac{\partial N_a}{\partial \xi} \frac{\partial \xi}{\partial x} = \frac{2}{h} \frac{\partial N_a}{\partial \xi}$$
   Since all components of $\mathbf{B}$ are linear in spatial derivatives of shape functions:
   $$\mathbf{B}(h) = \frac{1}{h} \mathbf{B}(1)$$

4. **Volume Differential $dV$**:
   $$dV = dx \, dy \, dz = \det(\mathbf{J}) \, d\xi \, d\eta \, d\zeta = \left(\frac{h}{2}\right)^3 d\xi \, d\eta \, d\zeta = \frac{h^3}{8} \, d\xi \, d\eta \, d\zeta = h^3 \, dV(1)$$

5. **Stiffness Matrix Scaling**:
   $$\mathbf{K}_e(h) = \int_{V} \mathbf{B}(h)^T \mathbf{D} \mathbf{B}(h) \, dV = \int_{[-1, 1]^3} \left(\frac{1}{h} \mathbf{B}(1)\right)^T \mathbf{D} \left(\frac{1}{h} \mathbf{B}(1)\right) \cdot (h^3 \, dV(1))$$
   $$\mathbf{K}_e(h) = \frac{h^3}{h^2} \int_{[-1, 1]^3} \mathbf{B}(1)^T \mathbf{D} \mathbf{B}(1) \, dV(1) = h \cdot \mathbf{K}_e(1) \quad \text{Q.E.D.}$$

This fundamental dimensional property allows precomputing the $24 \times 24$ stiffness matrix $\mathbf{K}_e(1)$ once for a given Poisson's ratio $\nu$ and scaling it directly by $h$, eliminating runtime numerical integration.

---

### 1.2 Boundary Condition Projection & SPD Operator Preservation

In Mycel, vectors are sized over the entire structured bounding box ($N_{\text{total}} = (N_x + 1)(N_y + 1)(N_z + 1)$ nodes, $3 N_{\text{total}}$ DOFs). Only active elements (where $\text{Domain}[e] = 1$) contribute to elasticity. Inactive nodes and prescribed support DOFs are constrained to zero.

To keep the matrix-free linear operator strictly **Symmetric Positive Definite (SPD)** and avoid ill-conditioned zeros on the diagonal:

1. **Projection Operator $\mathbf{P}$**:
   $\mathbf{P}$ is a diagonal projection matrix where:
   $$P_{ii} = \begin{cases} 0 & \text{if DOF } i \text{ is fixed or inactive (constrained)} \\ 1 & \text{if DOF } i \text{ is active and free} \end{cases}$$

2. **System Operator $\mathbf{A}$**:
   $$\mathbf{A} = \mathbf{P} \mathbf{K} \mathbf{P} + (\mathbf{I} - \mathbf{P})$$
   - For free active DOFs $i$: row $i$ equals the standard elasticity stiffness row $\mathbf{K}_{i, :}$ coupled only to other free DOFs.
   - For constrained DOFs $i$: row $i$ has $A_{ii} = 1.0$ and $A_{ij} = 0$ for $j \neq i$ (identity row).
   - Because $\mathbf{P} \mathbf{K} \mathbf{P}$ is positive semi-definite on the free active subspace (strictly positive definite when rigid body modes are fixed) and $\mathbf{I} - \mathbf{P}$ has unit eigenvalues on the orthogonal constrained subspace, $\mathbf{A}$ is **strictly SPD**.

3. **Consistent Right-Hand Side $\mathbf{b}$**:
   $$\mathbf{b} = \mathbf{P} \mathbf{F}$$
   Forces on constrained DOFs are projected out ($b_i = 0$). Thus $\mathbf{A} \mathbf{u} = \mathbf{b}$ guarantees $u_i = 0$ for all constrained DOFs and satisfies $\mathbf{K} \mathbf{u} = \mathbf{F}$ on all free active DOFs.

4. **Reactions at Fixed DOFs**:
   Evaluating reactions using the unconstrained matrix-vector product:
   $$\mathbf{R} = \mathbf{K} \mathbf{u}$$
   At fixed DOFs, $R_i = (\mathbf{K} \mathbf{u})_i$. Because $\mathbf{K}$ satisfies translational and rotational rigid body invariance ($\sum_{node} (\mathbf{K} \mathbf{u}) = 0$), the reactions satisfy exact global equilibrium:
   $$\sum \mathbf{R} = -\sum \mathbf{F}_{\text{applied}}$$
   $$\sum (\mathbf{r} \times \mathbf{R}) + \sum (\mathbf{r} \times \mathbf{F}_{\text{applied}}) = \mathbf{0}$$

---

## 2. Analytical Benchmark Specifications (V1–V9)

### V1 — Axial Bar ($\nu = 0$)
- **Geometry**: $L = 100\text{ mm}$, cross-section $10 \times 10\text{ mm}^2$, $h = 2.5\text{ mm}$ ($40 \times 4 \times 4 = 640$ elements).
- **Material**: $E = 3000\text{ MPa}$, $\nu = 0.0$.
- **Boundary Conditions**: Fixed face at $x = 0$ ($u_x = u_y = u_z = 0$), applied tensile force $P = 1000\text{ N}$ distributed via `SurfacePatch` on $x = 100\text{ mm}$.
- **Analytical Solution**:
  $$\delta_{\text{tip}} = \frac{P L}{A E} = \frac{1000 \cdot 100}{100 \cdot 3000} = \frac{1}{3} \approx 0.33333333\text{ mm}$$
  $$\sigma_{vM} = \frac{P}{A} = \frac{1000}{100} = 10.0\text{ MPa}$$
- **Verification**: FE tip displacement matches analytical solution to within $10^{-6}\text{ mm}$ (residual $< 10^{-10}$). Element von Mises stress in interior elements ($x \in [12.5, 87.5]$) matches $10.0\text{ MPa}$ within $10^{-6}\text{ MPa}$.

### V2 — Axial Bar Minimal Restraints ($\nu = 0.3$)
- **Geometry & Material**: $L = 100\text{ mm}$, section $10 \times 10\text{ mm}^2$, $h = 2.5\text{ mm}$, $E = 3000\text{ MPa}$, $\nu = 0.3$.
- **Minimal Restraints**:
  1. $u_x = 0$ on all 25 nodes of face $x = 0$ (prevents translation in $X$, rotations about $Y$ and $Z$).
  2. $u_y = u_z = 0$ at node $(0, 0, 0)$ (prevents translation in $Y$ and $Z$).
  3. $u_z = 0$ at node $(0, 10, 0)$ (prevents rotation about $X$).
  Total 28 fixed DOFs (eliminates all 6 rigid body modes without constraining Poisson lateral contraction).
- **Analytical Solution**: Pure uniform uniaxial strain state $\varepsilon_{xx} = \frac{P}{AE}$, $\varepsilon_{yy} = \varepsilon_{zz} = -\nu \varepsilon_{xx}$. Because Hex8 bilinear shape functions represent this uniform strain field exactly:
  $$\delta_{\text{tip}} = \frac{P L}{A E} = \frac{1}{3}\text{ mm}$$
- **Verification**: Matches analytical tip displacement within $10^{-6}\text{ mm}$.

### V3 — Cantilever Beam Mesh Convergence (Timoshenko Theory)
- **Geometry**: $L = 100\text{ mm}$, section $b = 10\text{ mm}$, $d = 10\text{ mm}$.
- **Material**: $E = 3000\text{ MPa}$, $\nu = 0.3$.
- **Loading & Support**: End face $x = 0$ fully clamped ($u_x = u_y = u_z = 0$). Tip load $P = 1000\text{ N}$ applied along $-Z$ at $x = L$.
- **Analytical Timoshenko Solution**:
  $$I = \frac{b d^3}{12} = \frac{10 \times 10^3}{12} = \frac{2500}{3} \approx 833.333333\text{ mm}^4$$
  $$G = \frac{E}{2(1 + \nu)} = \frac{3000}{2.6} \approx 1153.846154\text{ MPa}$$
  $$\kappa = \frac{10(1 + \nu)}{12 + 11\nu} = \frac{10(1.3)}{12 + 11(0.3)} = \frac{130}{153} \approx 0.8496732$$
  $$\delta_{\text{bending}} = \frac{P L^3}{3 E I} = \frac{1000 \cdot 100^3}{3 \cdot 3000 \cdot (2500/3)} = \frac{400}{3} \approx 133.333333\text{ mm}$$
  $$\delta_{\text{shear}} = \frac{P L}{\kappa G A} = \frac{1000 \cdot 100}{(130/153) \cdot (3000/2.6) \cdot 100} = 1.020000\text{ mm}$$
  $$\delta_{\text{total}} = \delta_{\text{bending}} + \delta_{\text{shear}} = 134.353333\text{ mm}$$

---

## 3. V3 Cantilever Convergence Table

Convergence measured with $N_z \in \{1, 2, 4, 8\}$ elements through depth ($N_y = N_z$, $h = d / N_z$):

| Elements Through Depth ($N_z$) | Grid Mesh ($N_x \times N_y \times N_z$) | Voxel Size $h$ (mm) | Total Elements | FE Tip Deflection $\delta$ (mm) | Timoshenko Theory $\delta$ (mm) | Relative Error | Monotonic Decrease |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **1** | $10 \times 1 \times 1$ | 10.00 | 10 | 86.3730 | 134.3533 | **35.71%** | — |
| **2** | $20 \times 2 \times 2$ | 5.00 | 80 | 116.7719 | 134.3533 | **13.09%** | **YES** |
| **4** | $40 \times 4 \times 4$ | 2.50 | 640 | 128.6479 | 134.3533 | **4.25%** | **YES** |
| **8** | $80 \times 8 \times 8$ | 1.25 | 5,120 | 132.1529 | 134.3533 | **1.64%** | **YES** |

### Convergence Analysis
- **Monotonic Convergence**: Error strictly decreases monotonically across all mesh refinements ($35.71\% \to 13.09\% \to 4.25\% \to 1.64\%$).
- **Shear Locking Alleviation**: Coarse meshes ($N_z = 1$) exhibit pronounced parasitic shear locking characteristic of standard trilinear Hex8 elements under pure bending, resulting in a significantly stiffer response. As depth refinement reaches $N_z = 8$, shear locking is alleviated.
- **Specification Gate**: Error at 8 elements through depth is **1.64%**, easily satisfying the $\le 8.0\%$ requirement. The residual ~1.6% discrepancy represents the difference between 1D Timoshenko beam kinematics and 3D continuum elasticity with fixed-clamped boundary restraint at $x = 0$.

---

## 4. Equilibrium, Energy, Symmetry & Robustness Summary (V4–V9)

- **V4 Equilibrium**:
  - Global force balance: $\sum \mathbf{R} = -\sum \mathbf{F}_{\text{applied}}$ relative error $< 10^{-10}$.
  - Moment balance about origin: $\sum (\mathbf{r} \times \mathbf{R}) + \sum (\mathbf{r} \times \mathbf{F}) = \mathbf{0}$ relative error $< 10^{-8}$.
- **V5 Energy Invariant**:
  - Compliance identity $C = \mathbf{F}^T \mathbf{u} \equiv \mathbf{u}^T \mathbf{K} \mathbf{u}$ verified to $< 10^{-10}$ relative error.
- **V6 Mirror Symmetry**:
  - Symmetric beam under symmetric load exhibits mirror-symmetric displacements $u_x(y) = u_x(-y)$, $u_z(y) = u_z(-y)$, and anti-symmetric $u_y(y) = -u_y(-y)$ to $< 10^{-9}\text{ mm}$.
- **V7 Elastic Scaling**:
  - Doubling $E$ exactly halves displacements and compliance to $< 10^{-10}$.
  - Scaling all dimensions by $s = 2$ at constant total force halves compliance to $< 10^{-10}$ (consistent with 3D elasticity where $\mathbf{K} \propto E \cdot L$).
- **V8 Robustness**:
  - Models with $< 6$ fixed DOFs immediately abort with a descriptive `InvalidOperationException` before solver execution.
  - Models containing disconnected void islands converge gracefully without breakdown.
- **V9 Consistency**:
  - Parallel 8-colour lattice matvec matches the scalar reference operator to $< 10^{-12}$ relative precision.
  - Repeated static analysis runs produce **bit-identical** displacement and reaction vectors.
