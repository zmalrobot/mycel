using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Preconditioned Conjugate Gradient (PCG) linear solver.
/// Fully deterministic with zero memory allocations inside the iteration loop.
/// Employs parallel chunked SIMD (AVX2 Vector256) vector reductions with fixed partial summation order.
/// Scratch buffers and parallel delegate instances are allocated once and reused across multiple solves.
/// </summary>
public sealed class PcgSolver
{
    private const int BlockSize = 8192;

    private double[] _r = Array.Empty<double>();
    private double[] _z = Array.Empty<double>();
    private double[] _p = Array.Empty<double>();
    private double[] _ap = Array.Empty<double>();
    private double[] _partialSums = Array.Empty<double>();

    private int _totalDofs;
    private int _numBlocks;

    private readonly ParallelOptions _parallelOptions;
    private readonly Action<int> _dotAction;
    private readonly Action<int> _updateXrNormAction;
    private readonly Action<int> _applyPrecDotAction;
    private readonly Action<int> _updatePAction;

    private nint _ptrX;
    private nint _ptrR;
    private nint _ptrZ;
    private nint _ptrP;
    private nint _ptrAp;
    private nint _ptrInvDiag;

    private nint _dotA;
    private nint _dotB;
    private double _alpha;
    private double _beta;

    public PcgSolver()
    {
        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        _dotAction = chunkIdx => ProcessDotChunk(chunkIdx);
        _updateXrNormAction = chunkIdx => ProcessUpdateXrNormChunk(chunkIdx);
        _applyPrecDotAction = chunkIdx => ProcessApplyPrecDotChunk(chunkIdx);
        _updatePAction = chunkIdx => ProcessUpdatePChunk(chunkIdx);
    }

    private void EnsureBuffers(int size)
    {
        if (_r.Length < size)
        {
            _r = new double[size];
            _z = new double[size];
            _p = new double[size];
            _ap = new double[size];
        }

        int numBlocks = (size + BlockSize - 1) / BlockSize;
        if (_partialSums.Length < numBlocks)
        {
            _partialSums = new double[numBlocks];
        }

        _totalDofs = size;
        _numBlocks = numBlocks;
    }

    public unsafe PcgResult Solve(
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

        // Preflight check: count unique fixed DOFs on active nodes
        if (op.Model.ActiveFixedDofCount < 6)
        {
            throw new InvalidOperationException(
                $"Model has fewer than 6 unique fixed DOFs on active nodes (found {op.Model.ActiveFixedDofCount}). " +
                "At least 6 fixed DOFs are required to prevent rigid body modes in 3D.");
        }

        int n = op.Model.TotalDofs;
        EnsureBuffers(n);

        var rSpan = _r.AsSpan(0, n);
        var zSpan = _z.AsSpan(0, n);
        var pSpan = _p.AsSpan(0, n);
        var apSpan = _ap.AsSpan(0, n);

        var sw = Stopwatch.StartNew();

        // Project initial guess x: zero out all constrained DOFs
        var constrained = op.Model.ConstrainedDofs;
        bool hasNonZeroX = false;
        for (int i = 0; i < n; i++)
        {
            if (constrained[i])
            {
                x[i] = 0.0;
            }
            else if (x[i] != 0.0)
            {
                hasNonZeroX = true;
            }
        }

        // Compute norm of the projected RHS: bNorm = ||P * b||
        double bNormSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (!constrained[i])
            {
                double bi = b[i];
                bNormSq += bi * bi;
            }
        }
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

        fixed (double* pX = x)
        fixed (double* pR = _r)
        fixed (double* pZ = _z)
        fixed (double* pP = _p)
        fixed (double* pApBuf = _ap)
        {
            _ptrX = (nint)pX;
            _ptrR = (nint)pR;
            _ptrZ = (nint)pZ;
            _ptrP = (nint)pP;
            _ptrAp = (nint)pApBuf;

            var workTeam = (maxDegreeOfParallelism > 0 && maxDegreeOfParallelism != FemWorkTeam.Shared.ThreadCount)
                ? new FemWorkTeam(maxDegreeOfParallelism)
                : FemWorkTeam.Shared;

            // Initialize projected residual r:
            // If x != 0: r = P * (b - A * x)
            // If x == 0: r = P * b
            double rNormSq;
            if (hasNonZeroX)
            {
                op.ApplyA(x, apSpan, parallel: true, maxDegreeOfParallelism, xIsAlreadyProjected: true);
                for (int i = 0; i < n; i++)
                {
                    rSpan[i] = constrained[i] ? 0.0 : (b[i] - apSpan[i]);
                }
                rNormSq = DotParallel(_ptrR, _ptrR, workTeam);
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    rSpan[i] = constrained[i] ? 0.0 : b[i];
                }
                rNormSq = bNormSq;
            }

            double rNorm = Math.Sqrt(rNormSq);
            double relRes = rNorm / bNorm;

            if (relRes <= tol)
            {
                sw.Stop();
                ClearPtrs();
                return new PcgResult
                {
                    Iterations = 0,
                    FinalResidual = relRes,
                    Converged = true,
                    Elapsed = sw.Elapsed,
                    Message = "Initial guess already converged."
                };
            }

            // z = M⁻¹ * r and rho0 = rᵀ * z
            double rho0;
            if (preconditioner is JacobiPreconditioner jacobi)
            {
                fixed (double* pInvDiag = jacobi.InvDiag)
                {
                    _ptrInvDiag = (nint)pInvDiag;
                    rho0 = ApplyPrecAndDotJacobi(workTeam);
                    _ptrInvDiag = 0;
                }
            }
            else
            {
                preconditioner.Apply(rSpan, zSpan);
                rho0 = DotParallel(_ptrR, _ptrZ, workTeam);
            }

            // p = z
            zSpan.CopyTo(pSpan);

            int iter = 0;
            double prevRelRes = relRes;
            int stagnationCount = 0;

            while (iter < maxIterations)
            {
                ct.ThrowIfCancellationRequested();
                iter++;

                // ap = A * p (p has p[i] == 0 on constrained DOFs)
                op.ApplyA(pSpan, apSpan, parallel: true, maxDegreeOfParallelism, xIsAlreadyProjected: true);

                // pAp = pᵀ * ap
                double pAp = DotParallel(_ptrP, _ptrAp, workTeam);
                if (!(pAp > 1e-30) || !double.IsFinite(pAp))
                {
                    sw.Stop();
                    ClearPtrs();
                    return new PcgResult
                    {
                        Iterations = iter,
                        FinalResidual = relRes,
                        Converged = false,
                        Elapsed = sw.Elapsed,
                        Message = $"PCG curvature breakdown (pᵀAp = {pAp:E3} <= 0). Is the model not sufficiently constrained?"
                    };
                }

                _alpha = rho0 / pAp;
                if (!double.IsFinite(_alpha) || _alpha <= 0.0)
                {
                    sw.Stop();
                    ClearPtrs();
                    return new PcgResult
                    {
                        Iterations = iter,
                        FinalResidual = relRes,
                        Converged = false,
                        Elapsed = sw.Elapsed,
                        Message = $"PCG step breakdown (alpha = {_alpha:E3}). Is the model not sufficiently constrained?"
                    };
                }

                // Fused update: x += alpha * p, r -= alpha * ap, and rNormSq = rᵀ * r
                rNormSq = UpdateXrNormParallel(workTeam);
                rNorm = Math.Sqrt(rNormSq);
                relRes = rNorm / bNorm;

                if (relRes <= tol)
                {
                    sw.Stop();
                    ClearPtrs();
                    return new PcgResult
                    {
                        Iterations = iter,
                        FinalResidual = relRes,
                        Converged = true,
                        Elapsed = sw.Elapsed
                    };
                }

                // Stagnation detection
                if (Math.Abs(relRes - prevRelRes) < 1e-15 * relRes)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                    {
                        sw.Stop();
                        ClearPtrs();
                        return new PcgResult
                        {
                            Iterations = iter,
                            FinalResidual = relRes,
                            Converged = false,
                            Elapsed = sw.Elapsed,
                            Message = "PCG stagnated: residual stopped decreasing. Is the model not sufficiently constrained?"
                        };
                    }
                }
                else
                {
                    stagnationCount = 0;
                }
                prevRelRes = relRes;

                // z = M⁻¹ * r and rho1 = rᵀ * z
                double rho1;
                if (preconditioner is JacobiPreconditioner jacobiPrec)
                {
                    fixed (double* pInvDiag = jacobiPrec.InvDiag)
                    {
                        _ptrInvDiag = (nint)pInvDiag;
                        rho1 = ApplyPrecAndDotJacobi(workTeam);
                        _ptrInvDiag = 0;
                    }
                }
                else
                {
                    preconditioner.Apply(rSpan, zSpan);
                    rho1 = DotParallel(_ptrR, _ptrZ, workTeam);
                }

                _beta = rho1 / rho0;
                if (!double.IsFinite(_beta) || _beta < 0.0)
                {
                    sw.Stop();
                    ClearPtrs();
                    return new PcgResult
                    {
                        Iterations = iter,
                        FinalResidual = relRes,
                        Converged = false,
                        Elapsed = sw.Elapsed,
                        Message = $"PCG search direction breakdown (beta = {_beta:E3}). Is the model not sufficiently constrained?"
                    };
                }

                // p = z + beta * p
                UpdatePParallel(workTeam);

                rho0 = rho1;

                if (progress != null && (iter % 10 == 0 || iter == maxIterations))
                {
                    progress.Report(new PcgProgress(iter, relRes));
                }
            }

            sw.Stop();
            ClearPtrs();
            return new PcgResult
            {
                Iterations = iter,
                FinalResidual = relRes,
                Converged = false,
                Elapsed = sw.Elapsed,
                Message = "PCG reached maximum iterations without converging. Is the model not sufficiently constrained?"
            };
        }
    }

    private void ClearPtrs()
    {
        _ptrX = 0;
        _ptrR = 0;
        _ptrZ = 0;
        _ptrP = 0;
        _ptrAp = 0;
        _ptrInvDiag = 0;
        _dotA = 0;
        _dotB = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double DotParallel(nint a, nint b, FemWorkTeam workTeam)
    {
        _dotA = a;
        _dotB = b;
        workTeam.For(_numBlocks, _dotAction);
        return ReducePartialSums();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double UpdateXrNormParallel(FemWorkTeam workTeam)
    {
        workTeam.For(_numBlocks, _updateXrNormAction);
        return ReducePartialSums();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ApplyPrecAndDotJacobi(FemWorkTeam workTeam)
    {
        workTeam.For(_numBlocks, _applyPrecDotAction);
        return ReducePartialSums();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdatePParallel(FemWorkTeam workTeam)
    {
        workTeam.For(_numBlocks, _updatePAction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ReducePartialSums()
    {
        double sum = 0.0;
        for (int k = 0; k < _numBlocks; k++)
        {
            sum += _partialSums[k];
        }
        return sum;
    }

    private unsafe void ProcessDotChunk(int chunkIdx)
    {
        int start = chunkIdx * BlockSize;
        int count = Math.Min(BlockSize, _totalDofs - start);

        double* pA = (double*)_dotA + start;
        double* pB = (double*)_dotB + start;

        int vecCount = count / 4;
        Vector256<double> acc = Vector256<double>.Zero;

        for (int i = 0; i < vecCount; i++)
        {
            int offset = i * 4;
            Vector256<double> va = Vector256.Load(pA + offset);
            Vector256<double> vb = Vector256.Load(pB + offset);
            acc += va * vb;
        }

        double sum = Vector256.Sum(acc);
        for (int i = vecCount * 4; i < count; i++)
        {
            sum += pA[i] * pB[i];
        }

        _partialSums[chunkIdx] = sum;
    }

    private unsafe void ProcessUpdateXrNormChunk(int chunkIdx)
    {
        int start = chunkIdx * BlockSize;
        int count = Math.Min(BlockSize, _totalDofs - start);

        double* pX = (double*)_ptrX + start;
        double* pR = (double*)_ptrR + start;
        double* pP = (double*)_ptrP + start;
        double* pAp = (double*)_ptrAp + start;

        Vector256<double> vAlpha = Vector256.Create(_alpha);
        Vector256<double> accRNorm = Vector256<double>.Zero;

        int vecCount = count / 4;
        for (int i = 0; i < vecCount; i++)
        {
            int offset = i * 4;
            Vector256<double> vx = Vector256.Load(pX + offset);
            Vector256<double> vr = Vector256.Load(pR + offset);
            Vector256<double> vp = Vector256.Load(pP + offset);
            Vector256<double> vap = Vector256.Load(pAp + offset);

            vx += vAlpha * vp;
            vr -= vAlpha * vap;

            vx.Store(pX + offset);
            vr.Store(pR + offset);

            accRNorm += vr * vr;
        }

        double sumRNorm = Vector256.Sum(accRNorm);
        for (int i = vecCount * 4; i < count; i++)
        {
            double xi = pX[i] + _alpha * pP[i];
            double ri = pR[i] - _alpha * pAp[i];
            pX[i] = xi;
            pR[i] = ri;
            sumRNorm += ri * ri;
        }

        _partialSums[chunkIdx] = sumRNorm;
    }

    private unsafe void ProcessApplyPrecDotChunk(int chunkIdx)
    {
        int start = chunkIdx * BlockSize;
        int count = Math.Min(BlockSize, _totalDofs - start);

        double* pR = (double*)_ptrR + start;
        double* pZ = (double*)_ptrZ + start;
        double* pInvDiag = (double*)_ptrInvDiag + start;

        Vector256<double> accRho = Vector256<double>.Zero;

        int vecCount = count / 4;
        for (int i = 0; i < vecCount; i++)
        {
            int offset = i * 4;
            Vector256<double> vr = Vector256.Load(pR + offset);
            Vector256<double> vinv = Vector256.Load(pInvDiag + offset);
            Vector256<double> vz = vr * vinv;
            vz.Store(pZ + offset);
            accRho += vr * vz;
        }

        double sumRho = Vector256.Sum(accRho);
        for (int i = vecCount * 4; i < count; i++)
        {
            double zi = pR[i] * pInvDiag[i];
            pZ[i] = zi;
            sumRho += pR[i] * zi;
        }

        _partialSums[chunkIdx] = sumRho;
    }

    private unsafe void ProcessUpdatePChunk(int chunkIdx)
    {
        int start = chunkIdx * BlockSize;
        int count = Math.Min(BlockSize, _totalDofs - start);

        double* pP = (double*)_ptrP + start;
        double* pZ = (double*)_ptrZ + start;

        Vector256<double> vBeta = Vector256.Create(_beta);

        int vecCount = count / 4;
        for (int i = 0; i < vecCount; i++)
        {
            int offset = i * 4;
            Vector256<double> vz = Vector256.Load(pZ + offset);
            Vector256<double> vp = Vector256.Load(pP + offset);
            Vector256<double> newP = vz + vBeta * vp;
            newP.Store(pP + offset);
        }

        for (int i = vecCount * 4; i < count; i++)
        {
            pP[i] = pZ[i] + _beta * pP[i];
        }
    }
}
