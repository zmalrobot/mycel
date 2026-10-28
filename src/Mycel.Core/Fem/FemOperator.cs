using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;

namespace Mycel.Core.Fem;

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
    private const int ChunkSize = 256;
    private readonly ParallelOptions _parallelOptions;
    private readonly Action<int>[] _colorWorkers;

    private nint _ptrX;
    private nint _ptrY;
    private nint _ptrKe;

    public FemOperator(FeModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Ke = Hex8Element.CreateKe(model.Grid.H, model.Material.Nu);
        DiagKe = Hex8Element.ExtractDiagonal(Ke);
        DiagA = new double[model.TotalDofs];
        _vScratch = new double[model.TotalDofs];

        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        _colorWorkers = new Action<int>[8];
        for (int c = 0; c < 8; c++)
        {
            int color = c;
            _colorWorkers[c] = chunkIdx => ProcessColorChunk(color, chunkIdx);
        }

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
    /// Synchronizes changes with Model.ElementDensity.
    /// </summary>
    public void UpdateDensities(ReadOnlySpan<double> densities, double penal)
    {
        double e0 = Model.Material.E0;
        double emin = Model.Material.Emin;

        if (!densities.SequenceEqual(Model.ElementDensity))
        {
            densities.CopyTo(Model.ElementDensity);
        }
        Model.Penal = penal;

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

    private unsafe void ProcessColorChunk(int color, int chunkIdx)
    {
        int[] elems = Model.ElementsByColor[color];
        int start = chunkIdx * ChunkSize;
        int end = Math.Min(start + ChunkSize, elems.Length);

        int[] dofs = ColorElemDofs[color];
        double[] eVals = ColorElemE[color];
        double* pKe = (double*)_ptrKe;
        double* pX = (double*)_ptrX;
        double* pY = (double*)_ptrY;

        for (int i = start; i < end; i++)
        {
            MultiplyElementUnsafe(i, dofs, eVals, pKe, pX, pY);
        }
    }

    /// <summary>
    /// Evaluates y = K(ρ) * x element-by-element using the 8-colour lattice.
    /// Completely zero-allocation in hot loops using pre-allocated delegates and chunked workers.
    /// </summary>
    public unsafe void ApplyK(ReadOnlySpan<double> x, Span<double> y, bool parallel = true, int maxDegreeOfParallelism = -1)
    {
        y.Clear();

        fixed (double* pX = x)
        fixed (double* pY = y)
        fixed (double* pKe = Ke)
        {
            _ptrX = (nint)pX;
            _ptrY = (nint)pY;
            _ptrKe = (nint)pKe;

            if (parallel)
            {
                var workTeam = (maxDegreeOfParallelism > 0 && maxDegreeOfParallelism != FemWorkTeam.Shared.ThreadCount)
                    ? new FemWorkTeam(maxDegreeOfParallelism)
                    : FemWorkTeam.Shared;

                for (int c = 0; c < 8; c++)
                {
                    int count = Model.ElementsByColor[c].Length;
                    if (count == 0) continue;

                    int numChunks = (count + ChunkSize - 1) / ChunkSize;
                    // Elements of the same colour share no nodes: race-free parallel evaluation
                    workTeam.For(numChunks, _colorWorkers[c]);
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

            _ptrX = 0;
            _ptrY = 0;
            _ptrKe = 0;
        }
    }

    /// <summary>
    /// Evaluates y = A * x = [P K P + (I - P)] * x.
    /// If xIsAlreadyProjected is true (e.g. in PCG iterations where P*x == x),
    /// redundant memory copying into scratch vectors is bypassed.
    /// </summary>
    public void ApplyA(
        ReadOnlySpan<double> x,
        Span<double> y,
        bool parallel = true,
        int maxDegreeOfParallelism = -1,
        bool xIsAlreadyProjected = false)
    {
        var constrained = Model.ConstrainedDofs;

        if (xIsAlreadyProjected)
        {
            ApplyK(x, y, parallel, maxDegreeOfParallelism);

            // Inactive DOFs are never touched by any active element (y remains 0.0),
            // and x is already projected (x is 0.0 on inactive DOFs).
            // Only Fixed DOFs may have received contributions from active elements,
            // so we set y[dof] = x[dof] for all Fixed DOFs.
            int[] fixedDofs = Model.FixedDofs;
            for (int i = 0; i < fixedDofs.Length; i++)
            {
                int dof = fixedDofs[i];
                y[dof] = x[dof];
            }
        }
        else
        {
            // 1. Zero out constrained DOFs: v = P * x
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

    /// <summary>
    /// Performs element matrix-vector product Ke * ue scaled by E_e.
    /// <para>
    /// Note on floating-point determinism: Floating-point addition is non-associative.
    /// When AVX2 (Vector256) is available, <see cref="Vector256.Sum"/> performs pairwise reduction across 4 lanes.
    /// When running under the scalar fallback, accumulation is sequential (<c>c = 0..23</c>).
    /// Both execution paths are 100% deterministic on their respective hardware targets,
    /// but may differ by at most 1-2 units in the last place (ULP, &lt; 1e-15 relative) compared to each other.
    /// </para>
    /// </summary>
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

        if (Vector256.IsHardwareAccelerated)
        {
            fixed (double* pUe = ue)
            {
                Vector256<double> u0 = Vector256.Load(pUe + 0);
                Vector256<double> u1 = Vector256.Load(pUe + 4);
                Vector256<double> u2 = Vector256.Load(pUe + 8);
                Vector256<double> u3 = Vector256.Load(pUe + 12);
                Vector256<double> u4 = Vector256.Load(pUe + 16);
                Vector256<double> u5 = Vector256.Load(pUe + 20);

                for (int r = 0; r < 24; r++)
                {
                    double* row = ke + r * 24;
                    Vector256<double> k0 = Vector256.Load(row + 0);
                    Vector256<double> k1 = Vector256.Load(row + 4);
                    Vector256<double> k2 = Vector256.Load(row + 8);
                    Vector256<double> k3 = Vector256.Load(row + 12);
                    Vector256<double> k4 = Vector256.Load(row + 16);
                    Vector256<double> k5 = Vector256.Load(row + 20);

                    Vector256<double> acc = k0 * u0 + k1 * u1 + k2 * u2 + k3 * u3 + k4 * u4 + k5 * u5;
                    y[dofs[dofBase + r]] += Vector256.Sum(acc);
                }
            }
        }
        else
        {
            // Scalar fallback
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
}
