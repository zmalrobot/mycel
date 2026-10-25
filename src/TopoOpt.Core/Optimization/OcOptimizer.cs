using System;

namespace TopoOpt.Core.Optimization;

public static class OcOptimizer
{
    /// <summary>
    /// Performs an Optimality Criteria (OC) design variable update with bisection on the volume Lagrange multiplier λ.
    /// Evaluates volume linearly in O(N) operations without re-filtering:
    ///   V(λ) = Σ_i s_i * ρ_i(λ)
    /// </summary>
    public static double Update(
        ReadOnlySpan<double> rho,
        ReadOnlySpan<double> dcDrho,
        ReadOnlySpan<double> s,
        ReadOnlySpan<bool> isPassive,
        Span<double> rhoNew,
        double targetVolumeFraction,
        double moveLimit = 0.2,
        double rhoMin = 1e-3,
        double lambdaGuess = 1.0,
        int maxBisectionSteps = 60,
        double volumeTolerance = 1e-8)
    {
        int n = rho.Length;
        if (dcDrho.Length < n || s.Length < n || rhoNew.Length < n)
            throw new ArgumentException("All spans must have length >= rho.Length.");

        // Compute total volume target V* = targetVolumeFraction * Σ s_i
        double totalS = 0.0;
        for (int i = 0; i < n; i++)
        {
            totalS += s[i];
        }
        double targetVolume = targetVolumeFraction * totalS;

        // Determine brackets [l1, l2]
        double l1 = (lambdaGuess > 0.0 && !double.IsInfinity(lambdaGuess) && !double.IsNaN(lambdaGuess))
            ? lambdaGuess * 0.5
            : 1e-12;
        double l2 = (lambdaGuess > 0.0 && !double.IsInfinity(lambdaGuess) && !double.IsNaN(lambdaGuess))
            ? lambdaGuess * 2.0
            : 1e12;

        if (l1 <= 1e-50) l1 = 1e-12;
        if (l2 <= l1) l2 = l1 * 10.0;

        double v1 = EvaluateVolume(rho, dcDrho, s, isPassive, l1, moveLimit, rhoMin);
        double v2 = EvaluateVolume(rho, dcDrho, s, isPassive, l2, moveLimit, rhoMin);

        // Expand lower bound if needed
        int expandSteps = 0;
        while (v1 < targetVolume && l1 > 1e-50 && expandSteps++ < 50)
        {
            l2 = l1;
            v2 = v1;
            l1 *= 0.1;
            v1 = EvaluateVolume(rho, dcDrho, s, isPassive, l1, moveLimit, rhoMin);
        }

        // Expand upper bound if needed
        expandSteps = 0;
        while (v2 > targetVolume && l2 < 1e50 && expandSteps++ < 50)
        {
            l1 = l2;
            v1 = v2;
            l2 *= 10.0;
            v2 = EvaluateVolume(rho, dcDrho, s, isPassive, l2, moveLimit, rhoMin);
        }

        // Log-scale bisection
        double lMid = Math.Sqrt(l1 * l2);

        for (int step = 0; step < maxBisectionSteps; step++)
        {
            lMid = Math.Sqrt(l1 * l2);
            double vMid = EvaluateVolume(rho, dcDrho, s, isPassive, lMid, moveLimit, rhoMin);

            double relErr = Math.Abs(vMid - targetVolume) / Math.Max(targetVolume, 1e-12);
            if (relErr <= volumeTolerance)
            {
                break;
            }

            if (vMid > targetVolume)
            {
                // Volume is too high -> increase lambda to penalize/reduce densities
                l1 = lMid;
            }
            else
            {
                // Volume is too low -> decrease lambda to increase densities
                l2 = lMid;
            }

            if (Math.Abs(l2 - l1) <= 1e-15 * lMid)
            {
                break;
            }
        }

        // Write final densities to rhoNew
        for (int i = 0; i < n; i++)
        {
            if (isPassive.Length > i && isPassive[i])
            {
                rhoNew[i] = 1.0;
                continue;
            }

            double r = rho[i];
            double dc = dcDrho[i];
            double si = s[i];

            double rLower = Math.Max(rhoMin, r - moveLimit);
            double rUpper = Math.Min(1.0, r + moveLimit);

            double negDc = -dc;
            if (negDc <= 0.0 || si <= 0.0)
            {
                rhoNew[i] = rLower;
            }
            else
            {
                double b = negDc / (lMid * si);
                double trial = r * Math.Sqrt(b);
                rhoNew[i] = Math.Clamp(trial, rLower, rUpper);
            }
        }

        return lMid;
    }

    private static double EvaluateVolume(
        ReadOnlySpan<double> rho,
        ReadOnlySpan<double> dcDrho,
        ReadOnlySpan<double> s,
        ReadOnlySpan<bool> isPassive,
        double lambda,
        double moveLimit,
        double rhoMin)
    {
        int n = rho.Length;
        double vol = 0.0;

        for (int i = 0; i < n; i++)
        {
            if (isPassive.Length > i && isPassive[i])
            {
                vol += s[i] * 1.0;
                continue;
            }

            double r = rho[i];
            double dc = dcDrho[i];
            double si = s[i];

            double rLower = Math.Max(rhoMin, r - moveLimit);
            double rUpper = Math.Min(1.0, r + moveLimit);

            double negDc = -dc;
            if (negDc <= 0.0 || si <= 0.0)
            {
                vol += si * rLower;
            }
            else
            {
                double b = negDc / (lambda * si);
                double trial = r * Math.Sqrt(b);
                vol += si * Math.Clamp(trial, rLower, rUpper);
            }
        }

        return vol;
    }
}
