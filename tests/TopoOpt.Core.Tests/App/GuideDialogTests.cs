using System;
using System.IO;
using TopoOpt.App.ViewModels;
using Xunit;

namespace TopoOpt.Core.Tests.App;

public sealed class GuideDialogTests
{
    private static string FindMainWindowXaml()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "src", "TopoOpt.App", "Views", "MainWindow.axaml");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("MainWindow.axaml not found from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void GuideDialog_InitialState_IsClosed()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsGuideDialogOpen);
    }

    [Fact]
    public void OpenGuideDialog_SetsIsOpenTrue()
    {
        var vm = new MainWindowViewModel();
        vm.OpenGuideDialogCommand.Execute(null);
        Assert.True(vm.IsGuideDialogOpen);
    }

    [Fact]
    public void CloseGuideDialog_SetsIsOpenFalse()
    {
        var vm = new MainWindowViewModel();
        vm.OpenGuideDialogCommand.Execute(null);
        Assert.True(vm.IsGuideDialogOpen);

        vm.CloseGuideDialogCommand.Execute(null);
        Assert.False(vm.IsGuideDialogOpen);
    }

    [Fact]
    public void OpenGuideDialog_DisarmsActivePicking()
    {
        var vm = new MainWindowViewModel();
        var load = new LoadItemViewModel(new TopoOpt.Core.Workflow.LoadRegion { Name = "Test Load" });
        vm.Loads.Add(load);
        vm.ArmPicking(load);

        Assert.True(vm.IsPickingActive);
        Assert.True(load.IsPicking);

        vm.OpenGuideDialogCommand.Execute(null);

        Assert.True(vm.IsGuideDialogOpen);
        Assert.False(vm.IsPickingActive);
        Assert.False(load.IsPicking);
        Assert.Null(vm.ActivePickingTarget);
    }

    [Fact]
    public void MainWindowXaml_ContainsGuideToolbarButtonAndModal()
    {
        string xaml = File.ReadAllText(FindMainWindowXaml());

        // Verify toolbar button is bound to OpenGuideDialogCommand
        Assert.Contains("Toolbar.Guide", xaml);
        Assert.Contains("OpenGuideDialogCommand", xaml);

        // Verify modal overlay binds to IsGuideDialogOpen
        Assert.Contains("IsGuideDialogOpen", xaml);
        Assert.Contains("CloseGuideDialogCommand", xaml);

        // Verify all 5 tab headers are present in the XAML
        Assert.Contains("1. Overview &amp; Workflow", xaml);
        Assert.Contains("2. Boundary Conditions", xaml);
        Assert.Contains("3. Setup &amp; Parameters", xaml);
        Assert.Contains("4. Formulas &amp; Physics", xaml);
        Assert.Contains("5. Results &amp; Export", xaml);
    }
}
