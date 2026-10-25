using System;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Point Jacobi (diagonal) preconditioner: z_i = r_i / D_i.
/// Precomputes inverse diagonal entries 1 / D_i.
/// </summary>
public sealed class JacobiPreconditioner : IPreconditioner
{
    private double[] _invDiag = Array.Empty<double>();

    public ReadOnlySpan<double> InvDiag => _invDiag;

    public void Setup(FeModel model, ReadOnlySpan<double> diagA)
    {
        if (_invDiag.Length != diagA.Length)
        {
            _invDiag = new double[diagA.Length];
        }

        for (int i = 0; i < diagA.Length; i++)
        {
            double d = diagA[i];
            _invDiag[i] = Math.Abs(d) > 1e-30 ? 1.0 / d : 1.0;
        }
    }

    public void Apply(ReadOnlySpan<double> r, Span<double> z)
    {
        for (int i = 0; i < r.Length; i++)
        {
            z[i] = r[i] * _invDiag[i];
        }
    }
}
