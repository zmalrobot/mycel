using System;
using System.Diagnostics;
using System.Threading;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Preconditioned Conjugate Gradient (PCG) linear solver.
/// Fully deterministic with zero memory allocations inside the iteration loop.
/// Scratch buffers are allocated once and reused across multiple solves.
/// </summary>
public sealed class PcgSolver
{
    private double[] _r = Array.Empty<double>();
    private double[] _z = Array.Empty<double>();
    private double[] _p = Array.Empty<double>();
    private double[] _ap = Array.Empty<double>();

    private void EnsureBuffers(int size)
    {
        if (_r.Length < size)
        {
            _r = new double[size];
            _z = new double[size];
            _p = new double[size];
            _ap = new double[size];
        }
    }

    public PcgResult Solve(
        FemOperator op,
        ReadOnlySpan<double> b,
        Span<double> x,
        IPreconditioner preconditioner,
        double tol = 1e-6,
        int maxIterations = 5000,
        IProgress<PcgProgress>? progress = null,
        CancellationToken ct = default,
        int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(preconditioner);

        // Preflight check
        if (op.Model.FixedDofs == null || op.Model.FixedDofs.Length < 6)
        {
            throw new InvalidOperationException(
                $"Model has fewer than 6 fixed DOFs (found {op.Model.FixedDofs?.Length ?? 0}). " +
                "At least 6 fixed DOFs are required to prevent rigid body modes in 3D.");
        }

        int n = op.Model.TotalDofs;
        EnsureBuffers(n);

        var r = _r.AsSpan(0, n);
        var z = _z.AsSpan(0, n);
        var p = _p.AsSpan(0, n);
        var ap = _ap.AsSpan(0, n);

        var sw = Stopwatch.StartNew();

        // Compute norm of RHS
        double bNormSq = Dot(b, b);
        double bNorm = Math.Sqrt(bNormSq);

        if (bNorm <= 1e-30)
        {
            x.Clear();
            sw.Stop();
            return new PcgResult
            {
                Iterations = 0,
                FinalResidual = 0.0,
                Converged = true,
                Elapsed = sw.Elapsed,
                Message = "RHS is zero. Solution is zero."
            };
        }

        // Set up preconditioner
        preconditioner.Setup(op.Model, op.DiagA);

        // Check whether initial x is non-zero (warm start)
        bool hasNonZeroX = false;
        for (int i = 0; i < n; i++)
        {
            if (x[i] != 0.0)
            {
                hasNonZeroX = true;
                break;
            }
        }

        if (hasNonZeroX)
        {
            // r = b - A * x
            op.ApplyA(x, ap, parallel: true, maxDegreeOfParallelism);
            for (int i = 0; i < n; i++)
            {
                r[i] = b[i] - ap[i];
            }
        }
        else
        {
            // r = b
            b.CopyTo(r);
        }

        double rNorm = Math.Sqrt(Dot(r, r));
        double relRes = rNorm / bNorm;

        if (relRes <= tol)
        {
            sw.Stop();
            return new PcgResult
            {
                Iterations = 0,
                FinalResidual = relRes,
                Converged = true,
                Elapsed = sw.Elapsed,
                Message = "Initial guess already converged."
            };
        }

        // z = M⁻¹ * r
        preconditioner.Apply(r, z);
        double rho0 = Dot(r, z);

        z.CopyTo(p);

        int iter = 0;
        while (iter < maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iter++;

            // ap = A * p
            op.ApplyA(p, ap, parallel: true, maxDegreeOfParallelism);

            double pAp = Dot(p, ap);
            if (pAp <= 1e-30)
            {
                sw.Stop();
                return new PcgResult
                {
                    Iterations = iter,
                    FinalResidual = relRes,
                    Converged = false,
                    Elapsed = sw.Elapsed,
                    Message = "PCG curvature breakdown (pᵀAp <= 0). Is the model not sufficiently constrained?"
                };
            }

            double alpha = rho0 / pAp;

            // x += alpha * p
            // r -= alpha * ap
            for (int i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * ap[i];
            }

            rNorm = Math.Sqrt(Dot(r, r));
            relRes = rNorm / bNorm;

            if (relRes <= tol)
            {
                sw.Stop();
                return new PcgResult
                {
                    Iterations = iter,
                    FinalResidual = relRes,
                    Converged = true,
                    Elapsed = sw.Elapsed
                };
            }

            // z = M⁻¹ * r
            preconditioner.Apply(r, z);
            double rho1 = Dot(r, z);

            double beta = rho1 / rho0;

            // p = z + beta * p
            for (int i = 0; i < n; i++)
            {
                p[i] = z[i] + beta * p[i];
            }

            rho0 = rho1;

            if (progress != null && (iter % 10 == 0 || iter == maxIterations))
            {
                progress.Report(new PcgProgress(iter, relRes));
            }
        }

        sw.Stop();
        return new PcgResult
        {
            Iterations = iter,
            FinalResidual = relRes,
            Converged = false,
            Elapsed = sw.Elapsed,
            Message = "PCG reached maximum iterations without converging. Is the model not sufficiently constrained?"
        };
    }

    /// <summary>
    /// Deterministic inner product with fixed sequential reduction order.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double Dot(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        double sum = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }
}
