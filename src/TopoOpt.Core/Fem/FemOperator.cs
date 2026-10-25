using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Matrix-free finite element operator for Hex8 structured grids.
/// Evaluates y = K(ρ)x and constrained operator y = Ax = [P K(ρ) P + (I - P)]x.
/// Uses 8-colour lattice decomposition for race-free, atomic-free parallel matvecs.
/// </summary>
public sealed class FemOperator
{
    public FeModel Model { get; }
    public double[] Ke { get; }
    public double[] DiagKe { get; }
    public double[] DiagA { get; }

    public int[][] ColorElemDofs { get; }
    public double[][] ColorElemE { get; }

    private readonly double[] _vScratch;

    public FemOperator(FeModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Ke = Hex8Element.CreateKe(model.Grid.H, model.Material.Nu);
        DiagKe = Hex8Element.ExtractDiagonal(Ke);
        DiagA = new double[model.TotalDofs];
        _vScratch = new double[model.TotalDofs];

        // Precompute flat 24-DOF index arrays and element Young's moduli for each color
        ColorElemDofs = new int[8][];
        ColorElemE = new double[8][];

        int nx = model.Grid.Nx;
        int ny = model.Grid.Ny;
        int nx1 = nx + 1;
        int ny1 = ny + 1;

        double e0 = model.Material.E0;
        double emin = model.Material.Emin;
        double penal = model.Penal;

        for (int c = 0; c < 8; c++)
        {
            int[] elems = model.ElementsByColor[c];
            int count = elems.Length;
            int[] dofs = new int[count * 24];
            double[] eVals = new double[count];

            for (int i = 0; i < count; i++)
            {
                int e = elems[i];
                model.Grid.InverseElementIndex(e, out int ix, out int iy, out int iz);

                // Compute 8 corner node indices
                int n0 = ix + nx1 * (iy + ny1 * iz);
                int n1 = n0 + 1;
                int n3 = n0 + nx1;
                int n2 = n3 + 1;
                int n4 = n0 + nx1 * ny1;
                int n5 = n4 + 1;
                int n7 = n4 + nx1;
                int n6 = n7 + 1;

                int[] nodes = { n0, n1, n2, n3, n4, n5, n6, n7 };
                int baseIdx = i * 24;
                for (int a = 0; a < 8; a++)
                {
                    int dof0 = 3 * nodes[a];
                    dofs[baseIdx + 3 * a + 0] = dof0 + 0;
                    dofs[baseIdx + 3 * a + 1] = dof0 + 1;
                    dofs[baseIdx + 3 * a + 2] = dof0 + 2;
                }

                // SIMP interpolation: E_e = Emin + rho^p * (E0 - Emin)
                double rho = model.ElementDensity[e];
                eVals[i] = emin + Math.Pow(rho, penal) * (e0 - emin);
            }

            ColorElemDofs[c] = dofs;
            ColorElemE[c] = eVals;
        }

        AssembleDiagonal();
    }

    /// <summary>
    /// Updates element stiffness scaling E_e when densities or penalization change.
    /// </summary>
    public void UpdateDensities(ReadOnlySpan<double> densities, double penal)
    {
        double e0 = Model.Material.E0;
        double emin = Model.Material.Emin;

        for (int c = 0; c < 8; c++)
        {
            int[] elems = Model.ElementsByColor[c];
            double[] eVals = ColorElemE[c];
            for (int i = 0; i < elems.Length; i++)
            {
                int e = elems[i];
                double rho = densities[e];
                eVals[i] = emin + Math.Pow(rho, penal) * (e0 - emin);
            }
        }

        AssembleDiagonal();
    }

    /// <summary>
    /// Assembles the diagonal of operator A = P K P + (I - P).
    /// </summary>
    public void AssembleDiagonal()
    {
        Array.Clear(DiagA);

        for (int c = 0; c < 8; c++)
        {
            int[] elems = Model.ElementsByColor[c];
            int[] dofs = ColorElemDofs[c];
            double[] eVals = ColorElemE[c];

            for (int i = 0; i < elems.Length; i++)
            {
                double ee = eVals[i];
                int dofBase = i * 24;
                for (int k = 0; k < 24; k++)
                {
                    DiagA[dofs[dofBase + k]] += ee * DiagKe[k];
                }
            }
        }

        // Apply constraints: identity for constrained DOFs
        for (int i = 0; i < Model.TotalDofs; i++)
        {
            if (Model.ConstrainedDofs[i])
            {
                DiagA[i] = 1.0;
            }
        }
    }

    private static readonly ParallelOptions DefaultParallelOptions = new();

    /// <summary>
    /// Evaluates y = K(ρ) * x element-by-element using the 8-colour lattice.
    /// </summary>
    public unsafe void ApplyK(ReadOnlySpan<double> x, Span<double> y, bool parallel = true, int maxDegreeOfParallelism = -1)
    {
        y.Clear();

        fixed (double* pX = x)
        fixed (double* pY = y)
        fixed (double* pKe = Ke)
        {
            nint ptrX = (nint)pX;
            nint ptrY = (nint)pY;
            nint ptrKe = (nint)pKe;

            if (parallel)
            {
                ParallelOptions pOpts = maxDegreeOfParallelism > 0
                    ? new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }
                    : DefaultParallelOptions;

                for (int c = 0; c < 8; c++)
                {
                    int[] dofs = ColorElemDofs[c];
                    double[] eVals = ColorElemE[c];
                    int count = Model.ElementsByColor[c].Length;
                    if (count == 0) continue;

                    // Elements of the same colour share no nodes: race-free parallel evaluation
                    Parallel.For(0, count, pOpts, i =>
                    {
                        MultiplyElementUnsafe(i, dofs, eVals, (double*)ptrKe, (double*)ptrX, (double*)ptrY);
                    });
                }
            }
            else
            {
                for (int c = 0; c < 8; c++)
                {
                    int[] dofs = ColorElemDofs[c];
                    double[] eVals = ColorElemE[c];
                    int count = Model.ElementsByColor[c].Length;

                    for (int i = 0; i < count; i++)
                    {
                        MultiplyElementUnsafe(i, dofs, eVals, pKe, pX, pY);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Evaluates y = A * x = [P K P + (I - P)] * x.
    /// </summary>
    public void ApplyA(ReadOnlySpan<double> x, Span<double> y, bool parallel = true, int maxDegreeOfParallelism = -1)
    {
        // 1. Zero out constrained DOFs: v = P * x
        var constrained = Model.ConstrainedDofs;
        for (int i = 0; i < Model.TotalDofs; i++)
        {
            _vScratch[i] = constrained[i] ? 0.0 : x[i];
        }

        // 2. y = K * v
        ApplyK(_vScratch, y, parallel, maxDegreeOfParallelism);

        // 3. Project back: y = P * y + (I - P) * x
        for (int i = 0; i < Model.TotalDofs; i++)
        {
            if (constrained[i])
            {
                y[i] = x[i];
            }
        }
    }

    /// <summary>
    /// Sequential scalar reference implementation for cross-checking against the optimised parallel operator.
    /// </summary>
    public void ApplyKScalarReference(ReadOnlySpan<double> x, Span<double> y)
    {
        y.Clear();

        int nx = Model.Grid.Nx;
        int ny = Model.Grid.Ny;
        int nx1 = nx + 1;
        int ny1 = ny + 1;

        double e0 = Model.Material.E0;
        double emin = Model.Material.Emin;
        double penal = Model.Penal;

        Span<double> ue = stackalloc double[24];
        Span<double> ye = stackalloc double[24];
        Span<int> dofs = stackalloc int[24];

        for (int idx = 0; idx < Model.ActiveElements.Length; idx++)
        {
            int e = Model.ActiveElements[idx];
            Model.Grid.InverseElementIndex(e, out int ix, out int iy, out int iz);

            int n0 = ix + nx1 * (iy + ny1 * iz);
            int n1 = n0 + 1;
            int n3 = n0 + nx1;
            int n2 = n3 + 1;
            int n4 = n0 + nx1 * ny1;
            int n5 = n4 + 1;
            int n7 = n4 + nx1;
            int n6 = n7 + 1;

            int[] nodes = { n0, n1, n2, n3, n4, n5, n6, n7 };
            for (int a = 0; a < 8; a++)
            {
                int dof0 = 3 * nodes[a];
                dofs[3 * a + 0] = dof0 + 0;
                dofs[3 * a + 1] = dof0 + 1;
                dofs[3 * a + 2] = dof0 + 2;
            }

            double rho = Model.ElementDensity[e];
            double ee = emin + Math.Pow(rho, penal) * (e0 - emin);

            // Gather
            for (int k = 0; k < 24; k++)
            {
                ue[k] = x[dofs[k]];
            }

            // Local product: ye = Ke * ue
            for (int r = 0; r < 24; r++)
            {
                double sum = 0.0;
                int rowOffset = r * 24;
                for (int c = 0; c < 24; c++)
                {
                    sum += Ke[rowOffset + c] * ue[c];
                }
                ye[r] = sum * ee;
            }

            // Scatter
            for (int k = 0; k < 24; k++)
            {
                y[dofs[k]] += ye[k];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void MultiplyElementUnsafe(
        int elemIndex,
        int[] dofs,
        double[] eVals,
        double* ke,
        double* x,
        double* y)
    {
        double ee = eVals[elemIndex];
        int dofBase = elemIndex * 24;

        Span<double> ue = stackalloc double[24];

        // Gather scaled by E_e
        for (int k = 0; k < 24; k++)
        {
            ue[k] = x[dofs[dofBase + k]] * ee;
        }

        // Multiply & scatter
        for (int r = 0; r < 24; r++)
        {
            double sum = 0.0;
            int rowOffset = r * 24;
            for (int c = 0; c < 24; c++)
            {
                sum += ke[rowOffset + c] * ue[c];
            }

            y[dofs[dofBase + r]] += sum;
        }
    }
}
