using System;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Preconditioner interface for iterative linear solvers (PCG).
/// Allows plugging in Jacobi, Multigrid, or custom preconditioners.
/// </summary>
public interface IPreconditioner
{
    /// <summary>
    /// Sets up or updates the preconditioner for the given model and diagonal of operator A.
    /// </summary>
    void Setup(FeModel model, ReadOnlySpan<double> diagA);

    /// <summary>
    /// Applies the preconditioner: z = M⁻¹ * r.
    /// </summary>
    void Apply(ReadOnlySpan<double> r, Span<double> z);
}
