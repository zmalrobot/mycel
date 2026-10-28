using System;

namespace Mycel.Core.Fem;

/// <summary>
/// Isotropic elastic material properties.
/// </summary>
/// <param name="E0">Base Young's modulus in MPa (1 MPa = 1 N/mm²).</param>
/// <param name="Nu">Poisson's ratio.</param>
/// <param name="EminRatio">Ratio of artificial void stiffness Emin / E0 (default 1e-6).</param>
public sealed record FeMaterial(double E0, double Nu, double EminRatio = 1e-6)
{
    /// <summary>
    /// Artificial void Young's modulus Emin in MPa.
    /// </summary>
    public double Emin => E0 * EminRatio;
}
