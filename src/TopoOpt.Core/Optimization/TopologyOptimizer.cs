using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Optimization;

public static class TopologyOptimizer
{
    /// <summary>
    /// Executes SIMP topology optimization on the structured FE model.
    /// Bit-identical determinism with fixed parallel reduction orders and zero allocation in hot loops.
    /// </summary>
    public static TopologyOptimizationResult Run(
        FeModel model,
        OptimizerOptions? options = null,
        IProgress<IterationSnapshot>? progress = null,
        CancellationToken ct = default,
        IterationController? controller = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new OptimizerOptions();
        controller ??= options.Controller;

        var warnings = new List<string>();
        double h = model.Grid.H;
        double f = options.VolumeFraction;
        double rMin = options.FilterRadiusMm / h;

        if (rMin < 1.5)
        {
            warnings.Add($"Filter radius {options.FilterRadiusMm:F2} mm ({rMin:F2} voxels) is below 1.5h ({1.5 * h:F2} mm).");
            if (options.EnforceMinimumFilterRadius)
            {
                rMin = 1.5;
            }
        }

        int nActive = model.ActiveElementCount;
        int totalElements = model.TotalElements;
        int[] activeElements = model.ActiveElements;

        // Build passive elements mask
        bool[] isPassive = new bool[nActive];
        if (options.PassiveMask != null)
        {
            for (int i = 0; i < nActive; i++)
            {
                int e = activeElements[i];
                if (e < options.PassiveMask.Length && options.PassiveMask[e] != 0)
                {
                    isPassive[i] = true;
                }
            }
        }

        if (options.ProtectBoundaryHinges)
        {
            var patchNodes = new HashSet<int>();
            foreach (var load in model.Loads)
            {
                var pNodes = load.Patch.Nodes;
                for (int i = 0; i < pNodes.Length; i++)
                    patchNodes.Add(pNodes[i]);
            }

            int[] fixedDofs = model.FixedDofs;
            for (int i = 0; i < fixedDofs.Length; i++)
            {
                patchNodes.Add(fixedDofs[i] / 3);
            }

            byte[] shell = PassiveRegion.Build(model.Grid, null, patchNodes, radiusInVoxels: 1);
            for (int i = 0; i < nActive; i++)
            {
                int e = activeElements[i];
                if (shell[e] != 0)
                {
                    isPassive[i] = true;
                }
            }
        }

        // Initialize Filter
        var filter = new DensityFilter(model.Grid, model.Domain, activeElements, rMin);
        double[] s = filter.S;

        // Initialize design densities rho
        double[] rho = new double[nActive];
        if (options.InitialDensity != null)
        {
            if (options.InitialDensity.Length == totalElements)
            {
                for (int i = 0; i < nActive; i++)
                    rho[i] = options.InitialDensity[activeElements[i]];
            }
            else if (options.InitialDensity.Length == nActive)
            {
                for (int i = 0; i < nActive; i++)
                    rho[i] = options.InitialDensity[i];
            }
            else
            {
                throw new ArgumentException(
                    $"InitialDensity length ({options.InitialDensity.Length}) must match either TotalElements ({totalElements}) or ActiveElementCount ({nActive}).");
            }
        }
        else
        {
            for (int i = 0; i < nActive; i++)
            {
                rho[i] = isPassive[i] ? 1.0 : f;
            }
        }

        // Ensure passive elements start and stay at 1.0
        for (int i = 0; i < nActive; i++)
        {
            if (isPassive[i]) rho[i] = 1.0;
        }

        // Pre-allocate scratch vectors outside hot loops
        double[] rhoTilde = new double[nActive];
        double[] rhoBar = new double[nActive];
        double[] rhoOld = new double[nActive];
        double[] dcDrhoBar = new double[nActive];
        double[] dcDrhoTilde = options.EnableHeaviside ? new double[nActive] : dcDrhoBar;
        double[] dRhoBar_dRhoTilde = options.EnableHeaviside ? new double[nActive] : Array.Empty<double>();
        double[] dcDrho = new double[nActive];
        double[] u = new double[model.TotalDofs];

        if (options.WarmStartDisplacements != null)
        {
            if (options.WarmStartDisplacements.Length == model.TotalDofs)
            {
                Array.Copy(options.WarmStartDisplacements, u, model.TotalDofs);
            }
        }

        var op = new FemOperator(model);
        var prec = new JacobiPreconditioner();
        var solver = new PcgSolver();

        double[] fForces = model.AssembleForces();
        int nDofs = model.TotalDofs;
        double[] b = new double[nDofs];
        for (int i = 0; i < nDofs; i++)
        {
            b[i] = model.ConstrainedDofs[i] ? 0.0 : fForces[i];
        }

        double lambda = 1.0;
        double maxChange = 1.0;
        double beta = 1.0;
        bool converged = false;

        var snapshots = new List<IterationSnapshot>();
        var complianceHistory = new Queue<double>(6);

        int startIter = options.StartIteration;
        int maxIter = options.MaxIterations;
        double penalBase = options.Penal;

        for (int iter = startIter; iter <= maxIter; iter++)
        {
            // Cooperative pause / stop / cancel check
            if (controller != null && controller.CheckAndBlock(ct))
                break;
            if (ct.IsCancellationRequested)
                break;

            var swTotal = Stopwatch.StartNew();

            // 1. Penalization continuation
            double penal = penalBase;
            if (options.Continuation)
            {
                if (iter < 20) penal = 1.0;
                else if (iter < 40) penal = 2.0;
                else penal = penalBase;
            }

            // Ensure passive solid elements remain 1.0 before filtering
            for (int i = 0; i < nActive; i++)
            {
                if (isPassive[i]) rho[i] = 1.0;
            }

            // 2. Filter step: rhoTilde = W * rho
            var swFilter = Stopwatch.StartNew();
            if (options.EnableHeaviside)
            {
                filter.ApplyFilter(rho, rhoTilde);
                HeavisideProjection.Project(rhoTilde, rhoBar, beta);
            }
            else
            {
                filter.ApplyFilter(rho, rhoBar);
            }
            swFilter.Stop();

            // Sync rhoBar into model.ElementDensity and op
            for (int i = 0; i < nActive; i++)
            {
                model.ElementDensity[activeElements[i]] = rhoBar[i];
            }
            op.UpdateDensities(model.ElementDensity, penal);

            // 3. Solver tolerance schedule: 1e-4 -> 1e-6 as maxChange decreases
            double solverTol;
            if (iter == startIter)
            {
                solverTol = options.SolverToleranceInitial;
            }
            else
            {
                double tolAlpha = Math.Clamp((maxChange - 0.01) / 0.09, 0.0, 1.0);
                double logInitial = Math.Log10(Math.Max(options.SolverToleranceInitial, 1e-15));
                double logFinal = Math.Log10(Math.Max(options.SolverToleranceFinal, 1e-15));
                double logTol = logFinal + tolAlpha * (logInitial - logFinal);
                solverTol = Math.Pow(10.0, logTol);
            }

            // 4. FEM Linear solve (warm start in-place in u, zero allocations in hot loop)
            var swSolve = Stopwatch.StartNew();
            PcgResult solverResult;
            try
            {
                solverResult = solver.Solve(
                    op,
                    b,
                    u,
                    prec,
                    tol: solverTol,
                    maxIterations: options.SolverMaxIterations,
                    ct: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            swSolve.Stop();

            if (ct.IsCancellationRequested)
                break;

            if (!solverResult.Converged)
            {
                throw new InvalidOperationException(
                    $"FEM solver did not converge in {solverResult.Iterations} iterations ({solverResult.Message}). Is the model not sufficiently constrained?");
            }

            // Compliance: c = Fᵀ * U
            double compliance = 0.0;
            for (int i = 0; i < nDofs; i++)
            {
                compliance += fForces[i] * u[i];
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
                        maxDispSq = dispSq;
                }
            }
            double maxDisp = Math.Sqrt(maxDispSq);

            // 5. Sensitivity analysis
            var swSens = Stopwatch.StartNew();
            SensitivityAnalysis.ComputeComplianceSensitivities(model, op, u, rhoBar, dcDrhoBar, penal);
            if (options.EnableHeaviside)
            {
                HeavisideProjection.Derivative(rhoTilde, dRhoBar_dRhoTilde, beta);
                for (int i = 0; i < nActive; i++)
                {
                    dcDrhoTilde[i] = dcDrhoBar[i] * dRhoBar_dRhoTilde[i];
                }
                filter.ApplySensitivityFilter(dcDrhoTilde, dcDrho);
            }
            else
            {
                filter.ApplySensitivityFilter(dcDrhoBar, dcDrho);
            }
            swSens.Stop();

            // 6. OC update
            var swUpdate = Stopwatch.StartNew();
            Array.Copy(rho, rhoOld, nActive);

            lambda = OcOptimizer.Update(
                rho,
                dcDrho,
                s,
                isPassive,
                rho, // in-place update into rho
                targetVolumeFraction: f,
                moveLimit: options.MoveLimit,
                rhoMin: options.RhoMin,
                lambdaGuess: lambda,
                maxBisectionSteps: 60,
                volumeTolerance: 1e-8);
            swUpdate.Stop();

            // Step up Heaviside beta every 25 iterations
            if (options.EnableHeaviside && iter % 25 == 0 && beta < 8.0)
            {
                beta = Math.Min(8.0, beta * 2.0);
            }

            swTotal.Stop();

            // Compute convergence metrics
            maxChange = 0.0;
            double currentVol = 0.0;
            double greySum = 0.0;

            for (int i = 0; i < nActive; i++)
            {
                double diff = Math.Abs(rho[i] - rhoOld[i]);
                if (diff > maxChange) maxChange = diff;

                currentVol += rhoBar[i];
                double rb = options.EnableHeaviside ? rhoTilde[i] : rhoBar[i];
                greySum += rb * (1.0 - rb);
            }

            double volFraction = currentVol / nActive;
            double greyLevel = (4.0 * greySum / nActive) * 100.0;

            // Box-sized immutable copy of physical and design densities
            float[] physicalDensityBox = new float[totalElements];
            float[] designDensityBox = new float[totalElements];
            for (int i = 0; i < nActive; i++)
            {
                physicalDensityBox[activeElements[i]] = (float)rhoBar[i];
                designDensityBox[activeElements[i]] = (float)rho[i];
            }

            var timings = new IterationTimings(
                SolveMs: swSolve.Elapsed.TotalMilliseconds,
                SensitivityMs: swSens.Elapsed.TotalMilliseconds,
                FilterMs: swFilter.Elapsed.TotalMilliseconds,
                UpdateMs: swUpdate.Elapsed.TotalMilliseconds,
                TotalMs: swTotal.Elapsed.TotalMilliseconds);

            var snapshot = new IterationSnapshot
            {
                Iteration = iter,
                Compliance = compliance,
                VolumeFraction = volFraction,
                MaxChange = maxChange,
                GreyLevel = greyLevel,
                SolverIterations = solverResult.Iterations,
                SolverResidual = solverResult.FinalResidual,
                Timings = timings,
                PhysicalDensity = physicalDensityBox,
                DesignDensity = designDensityBox,
                MaxDisplacement = maxDisp
            };

            snapshots.Add(snapshot);
            progress?.Report(snapshot);

            // Convergence criteria: max|Δρ| < 0.01 AND relative compliance change < 1e-3 over last 5 iterations
            complianceHistory.Enqueue(compliance);
            if (complianceHistory.Count > 6)
            {
                complianceHistory.Dequeue();
            }

            if (complianceHistory.Count == 6)
            {
                double oldestC = complianceHistory.Peek();
                double relCChange = Math.Abs(compliance - oldestC) / compliance;
                if (maxChange < 0.01 && relCChange < 1e-3)
                {
                    converged = true;
                    break;
                }
            }
        }

        var finalSnap = snapshots.Count > 0 ? snapshots[^1] : new IterationSnapshot();

        return new TopologyOptimizationResult
        {
            Converged = converged,
            TotalIterations = snapshots.Count,
            FinalSnapshot = finalSnap,
            Snapshots = snapshots,
            FinalCompliance = finalSnap.Compliance,
            FinalVolumeFraction = finalSnap.VolumeFraction,
            FinalPhysicalDensity = finalSnap.PhysicalDensity,
            FinalDisplacements = (double[])u.Clone(),
            Warnings = warnings
        };
    }
}
