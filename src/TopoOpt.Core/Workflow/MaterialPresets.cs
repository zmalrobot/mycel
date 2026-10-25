using System;

namespace TopoOpt.Core.Workflow;

public enum MaterialPresetKind
{
    Pla,
    PlaPlus,
    Petg,
    Custom
}

public sealed class MaterialDefinition
{
    public MaterialPresetKind PresetKind { get; set; } = MaterialPresetKind.Pla;
    public double E { get; set; } = 3000.0; // MPa
    public double Nu { get; set; } = 0.36;
    public double YieldXy { get; set; } = 50.0; // MPa
    public double YieldZ { get; set; } = 30.0;  // MPa
    public bool ConservativeZ { get; set; } = false;

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
                ConservativeZ = false
            },
            MaterialPresetKind.PlaPlus => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.PlaPlus,
                E = 2700.0,
                Nu = 0.36,
                YieldXy = 60.0,
                YieldZ = 40.0,
                ConservativeZ = false
            },
            MaterialPresetKind.Petg => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Petg,
                E = 2100.0,
                Nu = 0.38,
                YieldXy = 50.0,
                YieldZ = 35.0,
                ConservativeZ = false
            },
            _ => new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Custom,
                E = 3000.0,
                Nu = 0.36,
                YieldXy = 50.0,
                YieldZ = 30.0,
                ConservativeZ = false
            }
        };
    }

    public double ComputeSafetyFactor(double vonMisesStress)
    {
        if (vonMisesStress <= 1e-12)
            return 99.0; // Negligible stress -> very safe

        double effectiveYield = ConservativeZ ? YieldZ : YieldXy;
        return effectiveYield / vonMisesStress;
    }
}

