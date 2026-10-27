using System;
using TopoOpt.App.ViewModels;
using TopoOpt.Core.Workflow;
using Xunit;

namespace TopoOpt.Core.Tests.App;

public sealed class LoadItemViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllUnitsCorrectly()
    {
        var model = new LoadRegion { Name = "TestLoad", MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        Assert.Equal(1000f, vm.MagnitudeNewtons);
        Assert.Equal(1000f / LoadItemViewModel.StandardGravity, vm.MagnitudeKg, 1e-3);
        Assert.Equal((1000f / LoadItemViewModel.StandardGravity) * 1000f, vm.MagnitudeGrams, 1e-1);
        Assert.Equal(1000f, vm.Model.MagnitudeNewtons);
    }

    [Fact]
    public void ChangingMagnitudeNewtons_UpdatesKgAndGramsAndModel()
    {
        var model = new LoadRegion { MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        bool changedFired = false;
        vm.Changed += _ => changedFired = true;

        vm.MagnitudeNewtons = 980.665f;

        Assert.True(changedFired);
        Assert.Equal(980.665f, vm.MagnitudeNewtons, 1e-4);
        Assert.Equal(100.0f, vm.MagnitudeKg, 1e-4);
        Assert.Equal(100000.0f, vm.MagnitudeGrams, 1e-2);
        Assert.Equal(980.665f, vm.Model.MagnitudeNewtons, 1e-4);
    }

    [Fact]
    public void ChangingMagnitudeKg_UpdatesNewtonsAndGramsAndModel()
    {
        var model = new LoadRegion { MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        bool changedFired = false;
        vm.Changed += _ => changedFired = true;

        vm.MagnitudeKg = 50.0f;

        Assert.True(changedFired);
        Assert.Equal(50.0f, vm.MagnitudeKg, 1e-4);
        Assert.Equal(50.0f * LoadItemViewModel.StandardGravity, vm.MagnitudeNewtons, 1e-3);
        Assert.Equal(50000.0f, vm.MagnitudeGrams, 1e-2);
        Assert.Equal(50.0f * LoadItemViewModel.StandardGravity, vm.Model.MagnitudeNewtons, 1e-3);
    }

    [Fact]
    public void ChangingMagnitudeGrams_UpdatesKgAndNewtonsAndModel()
    {
        var model = new LoadRegion { MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        bool changedFired = false;
        vm.Changed += _ => changedFired = true;

        vm.MagnitudeGrams = 250.0f;

        Assert.True(changedFired);
        Assert.Equal(250.0f, vm.MagnitudeGrams, 1e-3);
        Assert.Equal(0.25f, vm.MagnitudeKg, 1e-5);
        Assert.Equal(0.25f * LoadItemViewModel.StandardGravity, vm.MagnitudeNewtons, 1e-4);
        Assert.Equal(0.25f * LoadItemViewModel.StandardGravity, vm.Model.MagnitudeNewtons, 1e-4);
    }

    [Fact]
    public void RepeatedBidirectionalChanges_DoNotCauseInfiniteLoopOrDrift()
    {
        var model = new LoadRegion { MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        // Switch between units multiple times
        for (int i = 1; i <= 20; i++)
        {
            vm.MagnitudeKg = i * 10f;
            Assert.Equal(i * 10f, vm.MagnitudeKg, 1e-4);
            Assert.Equal(i * 10000f, vm.MagnitudeGrams, 1e-2);
            Assert.Equal(i * 10f * LoadItemViewModel.StandardGravity, vm.MagnitudeNewtons, 1e-3);
        }
    }

    [Fact]
    public void SmallLoadsInGrams_AreAccuratelyConverted()
    {
        var model = new LoadRegion { MagnitudeNewtons = 1000f };
        var vm = new LoadItemViewModel(model);

        // 1 gram load
        vm.MagnitudeGrams = 1.0f;

        Assert.Equal(1.0f, vm.MagnitudeGrams, 1e-4);
        Assert.Equal(0.001f, vm.MagnitudeKg, 1e-6);
        Assert.Equal(0.001f * LoadItemViewModel.StandardGravity, vm.MagnitudeNewtons, 1e-6);
        Assert.Equal(0.001f * LoadItemViewModel.StandardGravity, vm.Model.MagnitudeNewtons, 1e-6);
    }

    [Theory]
    [InlineData(100f, "100", "10.197", "10197.2")]
    [InlineData(1000f, "1000", "101.972", "101971.6")]
    [InlineData(10000f, "10000", "1019.716", "1019716")]
    public void FormattedOutputs_ForRealisticLoads_ProduceExpectedStrings(float newtons, string expectedN, string expectedKg, string expectedG)
    {
        var model = new LoadRegion { MagnitudeNewtons = newtons };
        var vm = new LoadItemViewModel(model);

        string formattedN = vm.MagnitudeNewtons.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string formattedKg = vm.MagnitudeKg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string formattedG = vm.MagnitudeGrams.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedN, formattedN);
        Assert.Equal(expectedKg, formattedKg);
        Assert.Equal(expectedG, formattedG);

        // Verify string lengths fit comfortably within 180px control (approx 18 chars capacity)
        Assert.True(formattedN.Length <= 12, $"N string length {formattedN.Length} should fit in expanded input");
        Assert.True(formattedKg.Length <= 12, $"Kg string length {formattedKg.Length} should fit in expanded input");
        Assert.True(formattedG.Length <= 12, $"Grams string length {formattedG.Length} should fit in expanded input");
    }
}

