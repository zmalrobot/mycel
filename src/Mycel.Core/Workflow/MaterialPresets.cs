using System;

namespace Mycel.Core.Workflow;

public enum MaterialPresetKind
{
    Pla,
    PlaPlus,
    Petg,
    Abs,
    Custom
}

public enum InfillPatternKind
{
    Gyroid,
    Grid,
    Honeycomb,
    Rectilinear,
    Triangles
}

public sealed class MaterialDefinition
{
    public const double StandardFilamentDiameterMm = 1.75;
    public static readonly double FilamentCrossSectionMm2 = Math.PI * Math.Pow(StandardFilamentDiameterMm * 0.5, 2.0);

    public MaterialPresetKind PresetKind { get; set; } = MaterialPresetKind.Pla;
    public double E { get; set; } = 3000.0; // MPa
    public double Nu { get; set; } = 0.36;
    public double YieldXy { get; set; } = 50.0; // MPa
    public double YieldZ { get; set; } = 30.0;  // MPa
    public bool ConservativeZ { get; set; } = false;

    // 3D Printing Specific Parameters
    public double Density { get; set; } = 1.24; // g/cm³
    public double InfillPercent { get; set; } = 100.0; // 10.0 to 100.0 %
    public InfillPatternKind InfillPattern { get; set; } = InfillPatternKind.Gyroid;

    public static double GetGibsonAshbyModulusExponent(InfillPatternKind pattern) => pattern switch
    {
        InfillPatternKind.Gyroid => 1.7,
        InfillPatternKind.Grid => 2.0,
        InfillPatternKind.Honeycomb => 1.5,
        InfillPatternKind.Rectilinear => 2.0,
        InfillPatternKind.Triangles => 1.8,
        _ => 2.0
    };

    public static double GetGibsonAshbyYieldExponent(InfillPatternKind pattern) => pattern switch
    {
        InfillPatternKind.Gyroid => 1.4,
        InfillPatternKind.Grid => 1.5,
        InfillPatternKind.Honeycomb => 1.25,
        InfillPatternKind.Rectilinear => 1.5,
        InfillPatternKind.Triangles => 1.35,
        _ => 1.5
    };

    public double EffectiveE
    {
        get
        {
            if (InfillPercent >= 99.99) return E;
            double relativeDensity = Math.Clamp(InfillPercent / 100.0, 0.05, 1.0);
            return E * Math.Pow(relativeDensity, GetGibsonAshbyModulusExponent(InfillPattern));
        }
    }

    public double EffectiveYieldXy
    {
        get
        {
            if (InfillPercent >= 99.99) return YieldXy;
            double relativeDensity = Math.Clamp(InfillPercent / 100.0, 0.05, 1.0);
            return YieldXy * Math.Pow(relativeDensity, GetGibsonAshbyYieldExponent(InfillPattern));
        }
    }

    public double EffectiveYieldZ
    {
        get
        {
            if (InfillPercent >= 99.99) return YieldZ;
            double relativeDensity = Math.Clamp(InfillPercent / 100.0, 0.05, 1.0);
            return YieldZ * Math.Pow(relativeDensity, GetGibsonAshbyYieldExponent(InfillPattern));
        }
    }

    public static MaterialDefinition CreatePreset(MaterialPresetKind kind)
    {
        return kind switch
        {
            MaterialPresetKind.Pla => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Pla,
                E = 3000.0,
                Nu = 0.36,
                YieldXy = 50.0,
                YieldZ = 30.0,
                ConservativeZ = false,
                Density = 1.24,
                InfillPercent = 100.0,
                InfillPattern = InfillPatternKind.Gyroid
            },
            MaterialPresetKind.PlaPlus => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.PlaPlus,
                E = 2700.0,
                Nu = 0.36,
                YieldXy = 60.0,
                YieldZ = 40.0,
                ConservativeZ = false,
                Density = 1.24,
                InfillPercent = 100.0,
                InfillPattern = InfillPatternKind.Gyroid
            },
            MaterialPresetKind.Petg => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Petg,
                E = 2100.0,
                Nu = 0.38,
                YieldXy = 50.0,
                YieldZ = 35.0,
                ConservativeZ = false,
                Density = 1.27,
                InfillPercent = 100.0,
                InfillPattern = InfillPatternKind.Gyroid
            },
            MaterialPresetKind.Abs => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Abs,
                E = 2200.0,
                Nu = 0.35,
                YieldXy = 40.0,
                YieldZ = 28.0,
                ConservativeZ = false,
                Density = 1.04,
                InfillPercent = 100.0,
                InfillPattern = InfillPatternKind.Gyroid
            },
            _ => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Custom,
                E = 3000.0,
                Nu = 0.36,
                YieldXy = 50.0,
                YieldZ = 30.0,
                ConservativeZ = false,
                Density = 1.24,
                InfillPercent = 100.0,
                InfillPattern = InfillPatternKind.Gyroid
            }
        };
    }

    public double ComputeSafetyFactor(double vonMisesStress)
    {
        if (vonMisesStress <= 1e-12)
            return 99.0; // Negligible stress -> very safe

        double effectiveYield = ConservativeZ ? EffectiveYieldZ : EffectiveYieldXy;
        return effectiveYield / vonMisesStress;
    }

    public double ComputeMassGrams(double volumeMm3, double volumeFraction = 1.0)
    {
        double relativeDensity = Math.Clamp(InfillPercent / 100.0, 0.0, 1.0);
        double effectiveVolMm3 = Math.Abs(volumeMm3) * Math.Clamp(volumeFraction, 0.0, 1.0) * relativeDensity;
        return effectiveVolMm3 * (Density / 1000.0);
    }

    public double ComputeFilamentLengthMeters(double volumeMm3, double volumeFraction = 1.0)
    {
        double relativeDensity = Math.Clamp(InfillPercent / 100.0, 0.0, 1.0);
        double effectiveVolMm3 = Math.Abs(volumeMm3) * Math.Clamp(volumeFraction, 0.0, 1.0) * relativeDensity;
        return (effectiveVolMm3 / FilamentCrossSectionMm2) / 1000.0;
    }
}
