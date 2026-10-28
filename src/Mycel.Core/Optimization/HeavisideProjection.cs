using System;

namespace Mycel.Core.Optimization;

/// <summary>
/// Smooth threshold Heaviside projection (Phase B):
/// ρ̄ = [tanh(βη) + tanh(β(ρ̃ − η))] / [tanh(βη) + tanh(β(1 − η))]
/// dρ̄/dρ̃ = β(1 − tanh²(β(ρ̃ − η))) / [tanh(βη) + tanh(β(1 − η))]
/// </summary>
public static class HeavisideProjection
{
    public static void Project(ReadOnlySpan<double> rhoTilde, Span<double> rhoBar, double beta, double eta = 0.5)
    {
        double denom = Math.Tanh(beta * eta) + Math.Tanh(beta * (1.0 - eta));
        double invDenom = 1.0 / denom;
        double tanhBetaEta = Math.Tanh(beta * eta);

        for (int i = 0; i < rhoTilde.Length; i++)
        {
            double rt = rhoTilde[i];
            double num = tanhBetaEta + Math.Tanh(beta * (rt - eta));
            rhoBar[i] = Math.Clamp(num * invDenom, 0.0, 1.0);
        }
    }

    public static void Derivative(ReadOnlySpan<double> rhoTilde, Span<double> deriv, double beta, double eta = 0.5)
    {
        double denom = Math.Tanh(beta * eta) + Math.Tanh(beta * (1.0 - eta));
        double invDenom = 1.0 / denom;

        for (int i = 0; i < rhoTilde.Length; i++)
        {
            double t = Math.Tanh(beta * (rhoTilde[i] - eta));
            double d = beta * (1.0 - t * t) * invDenom;
            deriv[i] = Math.Max(d, 1e-12);
        }
    }
}
