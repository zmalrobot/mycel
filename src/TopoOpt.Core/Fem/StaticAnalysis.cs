using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace TopoOpt.Core.Fem;

public sealed record StaticAnalysisOptions(
    double Tolerance = 1e-6,
    int MaxIterations = 5000,
    double[]? WarmStartDisplacements = null,
    IPreconditioner? Preconditioner = null,
    IProgress<PcgProgress>? Progress = null,
    CancellationToken CancellationToken = default,
    int MaxDegreeOfParallelism = -1
);

public sealed class StaticAnalysisResult
{
    public double[] U { get; init; } = Array.Empty<double>();
    public double Compliance { get; init; }
    public double[] Reactions { get; init; } = Array.Empty<double>();
    public double[] VonMisesPerElement { get; init; } = Array.Empty<double>();
    public double MaxDisplacement { get; init; }
    public PcgResult SolverInfo { get; init; } = new();
}

public static class StaticAnalysis
{
    /// <summary>
    /// Executes static finite element analysis on the model.
    /// Returns displacements, compliance, support reactions, element von Mises stress, and max displacement.
    /// </summary>
    public static StaticAnalysisResult Run(FeModel model, StaticAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new StaticAnalysisOptions();

        // Preflight checks
        if (model.FixedDofs == null || model.FixedDofs.Length < 6)
        {
            throw new InvalidOperationException(
                $"Model has fewer than 6 fixed DOFs (found {model.FixedDofs?.Length ?? 0}). " +
                "At least 6 fixed DOFs are required to prevent rigid body modes in 3D.");
        }

        double[] f = model.AssembleForces();
        int n = model.TotalDofs;

        // Form RHS: b = P * F (zero out constrained DOFs)
        double[] b = new double[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = model.ConstrainedDofs[i] ? 0.0 : f[i];
        }

        // Initialize displacement vector (warm start if provided)
        double[] u = new double[n];
        if (options.WarmStartDisplacements != null)
        {
            if (options.WarmStartDisplacements.Length == n)
            {
                Array.Copy(options.WarmStartDisplacements, u, n);
            }
        }

        // Build operator and preconditioner
        var op = new FemOperator(model);
        var prec = options.Preconditioner ?? new JacobiPreconditioner();

        var solver = new PcgSolver();
        var solverResult = solver.Solve(
            op,
            b,
            u,
            prec,
            options.Tolerance,
            options.MaxIterations,
            options.Progress,
            options.CancellationToken,
            options.MaxDegreeOfParallelism);

        // Compliance: C = Fᵀ * U
        double compliance = 0.0;
        for (int i = 0; i < n; i++)
        {
            compliance += f[i] * u[i];
        }

        // Reactions at fixed DOFs computed with the unconstrained operator K:
        // R = K * U at fixed DOFs (and 0 elsewhere)
        double[] ku = new double[n];
        op.ApplyK(u, ku, parallel: true, options.MaxDegreeOfParallelism);

        double[] reactions = new double[n];
        foreach (int fixedDof in model.FixedDofs)
        {
            reactions[fixedDof] = ku[fixedDof];
        }

        // Max nodal displacement
        double maxDispSq = 0.0;
        for (int node = 0; node < model.TotalNodes; node++)
        {
            if (model.ActiveNodes[node])
            {
                double ux = u[3 * node + 0];
                double uy = u[3 * node + 1];
                double uz = u[3 * node + 2];
                double dispSq = ux * ux + uy * uy + uz * uz;
                if (dispSq > maxDispSq)
                {
                    maxDispSq = dispSq;
                }
            }
        }
        double maxDisp = Math.Sqrt(maxDispSq);

        // Von Mises stress per element at element centre: σ = D · B_c · u_e
        double[] vonMises = ComputeElementVonMises(model, op, u);

        return new StaticAnalysisResult
        {
            U = u,
            Compliance = compliance,
            Reactions = reactions,
            VonMisesPerElement = vonMises,
            MaxDisplacement = maxDisp,
            SolverInfo = solverResult
        };
    }

    private static double[] ComputeElementVonMises(FeModel model, FemOperator op, ReadOnlySpan<double> u)
    {
        double[] vm = new double[model.TotalElements];
        double[] bc = Hex8Element.CreateBc(model.Grid.H);
        double[] d = Hex8Element.CreateConstitutiveMatrix(1.0, model.Material.Nu);

        Span<double> ue = stackalloc double[24];
        Span<double> eps = stackalloc double[6];
        Span<double> sigma = stackalloc double[6];

        for (int c = 0; c < 8; c++)
        {
            int[] elems = model.ElementsByColor[c];
            int[] dofs = op.ColorElemDofs[c];
            double[] eVals = op.ColorElemE[c];

            for (int i = 0; i < elems.Length; i++)
            {
                int e = elems[i];
                double ee = eVals[i];
                int dofBase = i * 24;

                // Gather u_e
                for (int k = 0; k < 24; k++)
                {
                    ue[k] = u[dofs[dofBase + k]];
                }

                // ε = Bc * ue (6x24 by 24)
                for (int r = 0; r < 6; r++)
                {
                    double sum = 0.0;
                    int rowOffset = r * 24;
                    for (int k = 0; k < 24; k++)
                    {
                        sum += bc[rowOffset + k] * ue[k];
                    }
                    eps[r] = sum;
                }

                // σ = E_e * D * ε (6x6 by 6)
                for (int r = 0; r < 6; r++)
                {
                    double sum = 0.0;
                    int rowOffset = r * 6;
                    for (int k = 0; k < 6; k++)
                    {
                        sum += d[rowOffset + k] * eps[k];
                    }
                    sigma[r] = sum * ee;
                }

                // Von Mises formula:
                // σ_vm = sqrt( 0.5 * [(σ_xx - σ_yy)² + (σ_yy - σ_zz)² + (σ_zz - σ_xx)²] + 3 * (τ_yz² + τ_zx² + τ_xy²) )
                double sxx = sigma[0];
                double syy = sigma[1];
                double szz = sigma[2];
                double tyz = sigma[3];
                double tzx = sigma[4];
                double txy = sigma[5];

                double diff1 = sxx - syy;
                double diff2 = syy - szz;
                double diff3 = szz - sxx;

                double vmVal = Math.Sqrt(0.5 * (diff1 * diff1 + diff2 * diff2 + diff3 * diff3) + 3.0 * (tyz * tyz + tzx * tzx + txy * txy));
                vm[e] = vmVal;
            }
        }

        return vm;
    }

    /// <summary>
    /// Converts an element scalar field into an averaged nodal scalar field for visualisation.
    /// For each active node, averages the scalar values of all active elements meeting at that node.
    /// </summary>
    public static double[] AverageElementToNode(FeModel model, ReadOnlySpan<double> elementScalar)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (elementScalar.Length != model.TotalElements)
            throw new ArgumentException("elementScalar length must equal TotalElements.", nameof(elementScalar));

        double[] nodalSum = new double[model.TotalNodes];
        int[] nodalCount = new int[model.TotalNodes];

        int nx = model.Grid.Nx;
        int ny = model.Grid.Ny;
        int nx1 = nx + 1;
        int ny1 = ny + 1;

        for (int idx = 0; idx < model.ActiveElements.Length; idx++)
        {
            int e = model.ActiveElements[idx];
            double val = elementScalar[e];

            model.Grid.InverseElementIndex(e, out int ix, out int iy, out int iz);

            int n0 = ix + nx1 * (iy + ny1 * iz);
            int n1 = n0 + 1;
            int n3 = n0 + nx1;
            int n2 = n3 + 1;
            int n4 = n0 + nx1 * ny1;
            int n5 = n4 + 1;
            int n7 = n4 + nx1;
            int n6 = n7 + 1;

            nodalSum[n0] += val; nodalCount[n0]++;
            nodalSum[n1] += val; nodalCount[n1]++;
            nodalSum[n2] += val; nodalCount[n2]++;
            nodalSum[n3] += val; nodalCount[n3]++;
            nodalSum[n4] += val; nodalCount[n4]++;
            nodalSum[n5] += val; nodalCount[n5]++;
            nodalSum[n6] += val; nodalCount[n6]++;
            nodalSum[n7] += val; nodalCount[n7]++;
        }

        double[] nodalResult = new double[model.TotalNodes];
        for (int n = 0; n < model.TotalNodes; n++)
        {
            if (nodalCount[n] > 0)
            {
                nodalResult[n] = nodalSum[n] / nodalCount[n];
            }
        }

        return nodalResult;
    }

    /// <summary>
    /// Samples a nodal scalar field at an arbitrary 3D point using trilinear interpolation within the containing cell
    /// if all cell nodes are active. Falls back to nearest active node otherwise.
    /// </summary>
    public static double SampleNodalScalar(FeModel model, ReadOnlySpan<double> nodalScalar, Vector3 point)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (nodalScalar.Length != model.TotalNodes)
            throw new ArgumentException("nodalScalar length must equal TotalNodes.", nameof(nodalScalar));

        var grid = model.Grid;
        float h = grid.H;
        Vector3 origin = grid.Origin;

        float rx = (point.X - origin.X) / h;
        float ry = (point.Y - origin.Y) / h;
        float rz = (point.Z - origin.Z) / h;

        int ix = (int)Math.Floor(rx);
        int iy = (int)Math.Floor(ry);
        int iz = (int)Math.Floor(rz);

        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int nx1 = nx + 1;
        int ny1 = ny + 1;

        if (ix >= 0 && ix < nx && iy >= 0 && iy < ny && iz >= 0 && iz < nz)
        {
            int n0 = ix + nx1 * (iy + ny1 * iz);
            int n1 = n0 + 1;
            int n3 = n0 + nx1;
            int n2 = n3 + 1;
            int n4 = n0 + nx1 * ny1;
            int n5 = n4 + 1;
            int n7 = n4 + nx1;
            int n6 = n7 + 1;

            if (model.ActiveNodes[n0] && model.ActiveNodes[n1] && model.ActiveNodes[n2] && model.ActiveNodes[n3] &&
                model.ActiveNodes[n4] && model.ActiveNodes[n5] && model.ActiveNodes[n6] && model.ActiveNodes[n7])
            {
                // Local unit coordinates [0, 1]³
                double xi = rx - ix;
                double eta = ry - iy;
                double zeta = rz - iz;

                // Trilinear shape functions
                double n0Val = (1.0 - xi) * (1.0 - eta) * (1.0 - zeta);
                double n1Val = xi * (1.0 - eta) * (1.0 - zeta);
                double n2Val = xi * eta * (1.0 - zeta);
                double n3Val = (1.0 - xi) * eta * (1.0 - zeta);
                double n4Val = (1.0 - xi) * (1.0 - eta) * zeta;
                double n5Val = xi * (1.0 - eta) * zeta;
                double n6Val = xi * eta * zeta;
                double n7Val = (1.0 - xi) * eta * zeta;

                return n0Val * nodalScalar[n0] +
                       n1Val * nodalScalar[n1] +
                       n2Val * nodalScalar[n2] +
                       n3Val * nodalScalar[n3] +
                       n4Val * nodalScalar[n4] +
                       n5Val * nodalScalar[n5] +
                       n6Val * nodalScalar[n6] +
                       n7Val * nodalScalar[n7];
            }
        }

        // Fallback: nearest active node
        double bestDistSq = double.MaxValue;
        double bestVal = 0.0;

        for (int node = 0; node < model.TotalNodes; node++)
        {
            if (model.ActiveNodes[node])
            {
                grid.NodeCoords(node, out int ni, out int nj, out int nk);
                Vector3 nodePos = grid.NodePosition(ni, nj, nk);
                double distSq = Vector3.DistanceSquared(point, nodePos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestVal = nodalScalar[node];
                }
            }
        }

        return bestVal;
    }
}
