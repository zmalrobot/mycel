using System;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Trilinear 8-node hexahedral element (Hex8) stiffness matrix and strain-displacement formulation.
/// 
/// Local node order in element-local unit coordinates [0, 1]³:
/// 0: (0,0,0)
/// 1: (1,0,0)
/// 2: (1,1,0)
/// 3: (0,1,0)
/// 4: (0,0,1)
/// 5: (1,0,1)
/// 6: (1,1,1)
/// 7: (0,1,1)
/// 
/// Corresponding isoparametric coordinates (ξ, η, ζ) in [-1, 1]³:
/// 0: (-1, -1, -1)
/// 1: (+1, -1, -1)
/// 2: (+1, +1, -1)
/// 3: (-1, +1, -1)
/// 4: (-1, -1, +1)
/// 5: (+1, -1, +1)
/// 6: (+1, +1, +1)
/// 7: (-1, +1, +1)
/// 
/// Voigt strain vector order: [ε_xx, ε_yy, ε_zz, γ_yz, γ_zx, γ_xy] (with engineering shear strains).
/// 
/// Mathematical Proof of Ke(h) = h · Ke(1) for cubic element of edge h:
/// -------------------------------------------------------------------
/// Shape functions in isoparametric space:
///   N_a(ξ, η, ζ) = 1/8 * (1 + ξ_a * ξ) * (1 + η_a * η) * (1 + ζ_a * ζ)
/// Physical coordinates for cubic element of edge h:
///   x = x_0 + (1 + ξ)/2 * h  =>  ∂ξ/∂x = 2/h
/// Thus spatial derivatives satisfy:
///   ∂N_a/∂x = (∂N_a/∂ξ) * (∂ξ/∂x) = (2/h) * (∂N_a/∂ξ)
/// Therefore the strain-displacement matrix B scales strictly as:
///   B(h) = (1/h) * B(1)
/// The volume differential scales as:
///   dV = dx dy dz = det(J) dξ dη dζ = (h/2)³ dξ dη dζ = (h³/8) dξ dη dζ = h³ * dV(1)
/// Integrating element stiffness:
///   Ke(h) = ∫_V B(h)ᵀ D B(h) dV
///         = ∫_[-1,1]³ (1/h · B(1))ᵀ D (1/h · B(1)) · (h³ · dV(1))
///         = (1/h²) · h³ · ∫_[-1,1]³ B(1)ᵀ D B(1) dV(1)
///         = h · Ke(1).  Q.E.D.
/// </summary>
public static class Hex8Element
{
    private static readonly double[] GaussCoords = { -1.0 / Math.Sqrt(3.0), 1.0 / Math.Sqrt(3.0) };

    private static readonly double[] Xi =  { -1,  1,  1, -1, -1,  1,  1, -1 };
    private static readonly double[] Eta = { -1, -1,  1,  1, -1, -1,  1,  1 };
    private static readonly double[] Zeta = { -1, -1, -1, -1,  1,  1,  1,  1 };

    /// <summary>
    /// Base Ke for unit edge h = 1.0 and default Poisson's ratio ν = 0.3, with E = 1.
    /// Size: 24 x 24 = 576 elements (row-major).
    /// </summary>
    public static readonly double[] BaseKe = CreateKe(1.0, 0.3);

    /// <summary>
    /// Diagonal entries of BaseKe (length 24).
    /// </summary>
    public static readonly double[] BaseDiagonal = ExtractDiagonal(BaseKe);

    /// <summary>
    /// Strain-displacement matrix B at the element centre (ξ=0, η=0, ζ=0) for h = 1.0.
    /// Size: 6 x 24 = 144 elements (row-major).
    /// </summary>
    public static readonly double[] BaseBc = CreateBc(1.0);

    /// <summary>
    /// Computes the 6x6 isotropic elasticity constitutive matrix D for E=1 and given Poisson's ratio ν.
    /// Voigt order: [xx, yy, zz, yz, zx, xy].
    /// </summary>
    public static double[] CreateConstitutiveMatrix(double e, double nu)
    {
        double factor = e / ((1.0 + nu) * (1.0 - 2.0 * nu));
        double c1 = factor * (1.0 - nu);
        double c2 = factor * nu;
        double c3 = e / (2.0 * (1.0 + nu)); // shear modulus G

        double[] d = new double[36];
        d[0 * 6 + 0] = c1; d[0 * 6 + 1] = c2; d[0 * 6 + 2] = c2;
        d[1 * 6 + 0] = c2; d[1 * 6 + 1] = c1; d[1 * 6 + 2] = c2;
        d[2 * 6 + 0] = c2; d[2 * 6 + 1] = c2; d[2 * 6 + 2] = c1;
        d[3 * 6 + 3] = c3;
        d[4 * 6 + 4] = c3;
        d[5 * 6 + 5] = c3;
        return d;
    }

    /// <summary>
    /// Creates the 24x24 element stiffness matrix Ke for cubic element of edge h, E = 1, and Poisson's ratio nu.
    /// Using 2x2x2 Gauss quadrature at ±1/√3.
    /// </summary>
    public static double[] CreateKe(double h, double nu)
    {
        if (h <= 0)
            throw new ArgumentOutOfRangeException(nameof(h), "Element edge length h must be positive.");

        // For h != 1, we can compute Ke(1) and multiply by h (proven above)
        double[] ke = new double[24 * 24];
        double[] d = CreateConstitutiveMatrix(1.0, nu);

        double[] b = new double[6 * 24];
        double[] db = new double[6 * 24];

        // 2x2x2 Gauss integration over [-1, 1]³
        // det(J) for h=1 is 1/8. Gauss weights are 1.0 each.
        double dVWeight = 1.0 / 8.0;

        for (int ix = 0; ix < 2; ix++)
        {
            double xi = GaussCoords[ix];
            for (int iy = 0; iy < 2; iy++)
            {
                double eta = GaussCoords[iy];
                for (int iz = 0; iz < 2; iz++)
                {
                    double zeta = GaussCoords[iz];

                    EvaluateB(xi, eta, zeta, 1.0, b);

                    // Compute D * B (6x6 by 6x24 -> 6x24)
                    for (int r = 0; r < 6; r++)
                    {
                        for (int c = 0; c < 24; c++)
                        {
                            double sum = 0.0;
                            for (int k = 0; k < 6; k++)
                            {
                                sum += d[r * 6 + k] * b[k * 24 + c];
                            }
                            db[r * 24 + c] = sum;
                        }
                    }

                    // Accumulate Bᵀ * (D * B) * dVWeight
                    for (int i = 0; i < 24; i++)
                    {
                        for (int j = 0; j < 24; j++)
                        {
                            double sum = 0.0;
                            for (int k = 0; k < 6; k++)
                            {
                                sum += b[k * 24 + i] * db[k * 24 + j];
                            }
                            ke[i * 24 + j] += sum * dVWeight;
                        }
                    }
                }
            }
        }

        // Apply scale factor h
        if (Math.Abs(h - 1.0) > 1e-15)
        {
            for (int i = 0; i < 24 * 24; i++)
            {
                ke[i] *= h;
            }
        }

        return ke;
    }

    /// <summary>
    /// Evaluates the 6x24 strain-displacement matrix B at isoparametric coordinate (xi, eta, zeta) for edge h.
    /// </summary>
    public static void EvaluateB(double xi, double eta, double zeta, double h, Span<double> b)
    {
        b.Clear();
        double invH = 1.0 / h;

        for (int a = 0; a < 8; a++)
        {
            // ∂N_a/∂x = (2/h) * (1/8 * ξ_a * (1 + η_a * η) * (1 + ζ_a * ζ))
            double dNdx = (0.25 * invH) * Xi[a] * (1.0 + Eta[a] * eta) * (1.0 + Zeta[a] * zeta);
            double dNdy = (0.25 * invH) * Eta[a] * (1.0 + Xi[a] * xi) * (1.0 + Zeta[a] * zeta);
            double dNdz = (0.25 * invH) * Zeta[a] * (1.0 + Xi[a] * xi) * (1.0 + Eta[a] * eta);

            int colU = 3 * a + 0;
            int colV = 3 * a + 1;
            int colW = 3 * a + 2;

            // ε_xx = ∂u/∂x
            b[0 * 24 + colU] = dNdx;
            // ε_yy = ∂v/∂y
            b[1 * 24 + colV] = dNdy;
            // ε_zz = ∂w/∂z
            b[2 * 24 + colW] = dNdz;
            // γ_yz = ∂v/∂z + ∂w/∂y
            b[3 * 24 + colV] = dNdz;
            b[3 * 24 + colW] = dNdy;
            // γ_zx = ∂u/∂z + ∂w/∂x
            b[4 * 24 + colU] = dNdz;
            b[4 * 24 + colW] = dNdx;
            // γ_xy = ∂u/∂y + ∂v/∂x
            b[5 * 24 + colU] = dNdy;
            b[5 * 24 + colV] = dNdx;
        }
    }

    /// <summary>
    /// Creates the 6x24 strain-displacement matrix B at the element centre (ξ=0, η=0, ζ=0) for edge h.
    /// </summary>
    public static double[] CreateBc(double h)
    {
        double[] bc = new double[6 * 24];
        EvaluateB(0.0, 0.0, 0.0, h, bc);
        return bc;
    }

    public static double[] ExtractDiagonal(ReadOnlySpan<double> ke24x24)
    {
        double[] diag = new double[24];
        for (int i = 0; i < 24; i++)
        {
            diag[i] = ke24x24[i * 24 + i];
        }
        return diag;
    }
}
