using System;
using System.IO;
using System.Numerics;
using Mycel.App.ViewModels;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Mycel.Core.Workflow;
using Xunit;

namespace Mycel.Core.Tests.Workflow;

public sealed class InfillAndFilamentTests
{
    private static (IndexedMesh Mesh, MeshReport Report) CreateCube(float side)
    {
        var boxTris = StlFixtures.Box(new Vector3(side, side, side));
        using var ms = new MemoryStream();
        StlFixtures.WriteBinary(ms, boxTris);
        ms.Position = 0;
        return StlReader.Read(ms);
    }

    [Fact]
    public void GibsonAshby_EffectiveModulus_CalculatesCorrectlyPerPattern()
    {
        var mat = new MaterialDefinition
        {
            E = 3000.0,
            YieldXy = 50.0,
            YieldZ = 30.0,
            InfillPercent = 100.0
        };

        // At 100% infill, effective properties match solid properties exactly
        Assert.Equal(3000.0, mat.EffectiveE, 3);
        Assert.Equal(50.0, mat.EffectiveYieldXy, 3);
        Assert.Equal(30.0, mat.EffectiveYieldZ, 3);

        // Test at 50% infill with Gyroid (p = 1.7, m = 1.4)
        mat.InfillPercent = 50.0;
        mat.InfillPattern = InfillPatternKind.Gyroid;
        double expectedGyroidE = 3000.0 * Math.Pow(0.5, 1.7);
        double expectedGyroidYield = 50.0 * Math.Pow(0.5, 1.4);
        Assert.Equal(expectedGyroidE, mat.EffectiveE, 2);
        Assert.Equal(expectedGyroidYield, mat.EffectiveYieldXy, 2);

        // Test at 40% infill with Grid (p = 2.0, m = 1.5)
        mat.InfillPercent = 40.0;
        mat.InfillPattern = InfillPatternKind.Grid;
        double expectedGridE = 3000.0 * Math.Pow(0.4, 2.0); // 3000 * 0.16 = 480.0
        Assert.Equal(expectedGridE, mat.EffectiveE, 2);

        // Test with Honeycomb (p = 1.5, m = 1.25)
        mat.InfillPattern = InfillPatternKind.Honeycomb;
        double expectedHoneyE = 3000.0 * Math.Pow(0.4, 1.5);
        Assert.Equal(expectedHoneyE, mat.EffectiveE, 2);
    }

    [Fact]
    public void ComputeMassAndLength_AnalyticalVerification()
    {
        // 10 mm cube: Volume = 1000 mm³ = 1.0 cm³
        var mat = new MaterialDefinition
        {
            Density = 1.24, // g/cm³ (PLA)
            InfillPercent = 100.0
        };

        double vol1000 = 1000.0;

        // At 100% solid:
        // Mass = 1.0 cm³ * 1.24 g/cm³ = 1.24 g
        double massSolid = mat.ComputeMassGrams(vol1000);
        Assert.Equal(1.24, massSolid, 3);

        // Filament length: 1000 mm³ / (pi * (1.75/2)^2) = 1000 / 2.405282 = ~415.75 mm = ~0.4158 m
        double lenSolid = mat.ComputeFilamentLengthMeters(vol1000);
        Assert.Equal(0.4158, lenSolid, 3);

        // At 40% infill:
        mat.InfillPercent = 40.0;
        double mass40 = mat.ComputeMassGrams(vol1000);
        Assert.Equal(1.24 * 0.4, mass40, 3);

        double len40 = mat.ComputeFilamentLengthMeters(vol1000);
        Assert.Equal(0.4158 * 0.4, len40, 3);
    }

    [Fact]
    public void MaterialPresets_IncludeAbsAndExpectedDensities()
    {
        var pla = MaterialDefinition.CreatePreset(MaterialPresetKind.Pla);
        Assert.Equal(1.24, pla.Density, 2);
        Assert.Equal(100.0, pla.InfillPercent);
        Assert.Equal(InfillPatternKind.Gyroid, pla.InfillPattern);

        var plaPlus = MaterialDefinition.CreatePreset(MaterialPresetKind.PlaPlus);
        Assert.Equal(1.24, plaPlus.Density, 2);

        var petg = MaterialDefinition.CreatePreset(MaterialPresetKind.Petg);
        Assert.Equal(1.27, petg.Density, 2);

        var abs = MaterialDefinition.CreatePreset(MaterialPresetKind.Abs);
        Assert.Equal(1.04, abs.Density, 2);
        Assert.Equal(2200.0, abs.E, 1);
        Assert.Equal(40.0, abs.YieldXy, 1);
    }

    [Fact]
    public void ProjectSerializer_InfillAndDensityRoundtrip()
    {
        var (mesh, report) = CreateCube(10f);
        var state = new ProjectState
        {
            Mesh = mesh,
            MeshReport = report,
            Material = new MaterialDefinition
            {
                PresetKind = MaterialPresetKind.Petg,
                E = 2100.0,
                Nu = 0.38,
                YieldXy = 50.0,
                YieldZ = 35.0,
                Density = 1.27,
                InfillPercent = 45.0,
                InfillPattern = InfillPatternKind.Honeycomb
            }
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_infill_{Guid.NewGuid():N}.topo");
        try
        {
            ProjectSerializer.Save(tempFile, state);
            var loaded = ProjectSerializer.Open(tempFile);

            Assert.Equal(1.27, loaded.Material.Density, 2);
            Assert.Equal(45.0, loaded.Material.InfillPercent, 1);
            Assert.Equal(InfillPatternKind.Honeycomb, loaded.Material.InfillPattern);
            Assert.Equal(MaterialPresetKind.Petg, loaded.Material.PresetKind);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MainWindowViewModel_PrintMetricsAndSavings_UpdatesDynamically()
    {
        var vm = new MainWindowViewModel();
        // 20 mm cube: Vol = 8,000 mm³ = 8.0 cm³
        var (mesh, report) = CreateCube(20f);
        var bvh = new Bvh(mesh);
        var selector = new RegionSelector(mesh);
        vm.ApplyLoadedMesh(mesh, bvh, selector, report);

        // Initial PLA 100% infill: Mass = 8.0 cm³ * 1.24 = 9.92 g -> "9.9 g"
        Assert.Equal("9.9 g", vm.PrintWeightText);
        Assert.False(vm.HasOptimizationSavings);

        // Adjust infill to 50%
        vm.MaterialInfillPercent = 50.0;
        // Mass = 8.0 * 1.24 * 0.5 = 4.96 g -> "5.0 g"
        Assert.Equal("5.0 g", vm.PrintWeightText);

        // Simulate optimization reaching volume fraction 0.30 (70% reduction)
        vm.UpdateOptimizationSavings(0.30);
        Assert.True(vm.HasOptimizationSavings);
        Assert.Equal("70.0%", vm.SavedPercentText);
        // Saved mass = 4.96 * 0.70 = 3.47 g -> "-3.5 g"
        Assert.Equal("-3.5 g", vm.SavedWeightText);
        Assert.Equal("-3.5 g (70.0%)", vm.SavedWeightSummaryText);
    }
}

