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
}

