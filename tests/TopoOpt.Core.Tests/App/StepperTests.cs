using System;
using System.IO;
using System.Text.RegularExpressions;
using TopoOpt.App.ViewModels;
using Xunit;

namespace TopoOpt.Core.Tests.App;

/// <summary>
/// Regression tests for the left stepper (hotfix: headers were silently disabled and bodies never visible
/// because XAML passed the string "N" where an int was expected).
/// </summary>
public class StepperTests
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

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("6", 6)]
    public void HeaderCommand_AcceptsStringParameter_AndActivatesExactlyOneStep(string parameter, int expected)
    {
        var vm = new MainWindowViewModel();

        // XAML CommandParameter="N" is a string: the command must be executable with it.
        Assert.True(vm.SelectStepFromHeaderCommand.CanExecute(parameter));
        vm.SelectStepFromHeaderCommand.Execute(parameter);

        Assert.Equal(expected, vm.ActiveStep);
        bool[] flags = [vm.IsStep1Active, vm.IsStep2Active, vm.IsStep3Active, vm.IsStep4Active, vm.IsStep5Active, vm.IsStep6Active];
        for (int i = 0; i < flags.Length; i++)
        {
            Assert.Equal(i + 1 == expected, flags[i]);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("abc")]
    [InlineData(null)]
    public void HeaderCommand_IgnoresInvalidParameter(string? parameter)
    {
        var vm = new MainWindowViewModel();
        int before = vm.ActiveStep;
        vm.SelectStepFromHeaderCommand.Execute(parameter);
        Assert.Equal(before, vm.ActiveStep);
    }

    [Fact]
    public void ActiveStepChange_RaisesPropertyChangedForAllStepFlags()
    {
        var vm = new MainWindowViewModel();
        var raised = new System.Collections.Generic.HashSet<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        vm.SelectStep(4);

        for (int n = 1; n <= 6; n++)
        {
            Assert.Contains($"IsStep{n}Active", raised);
        }
    }

    [Fact]
    public void Xaml_StepBodies_DoNotCompareIntWithStringParameter()
    {
        string xaml = File.ReadAllText(FindMainWindowXaml());

        // Regression guard: ObjectConverters.Equal(int, "N") is always false.
        Assert.DoesNotMatch(new Regex(@"Binding ActiveStep[^}]*ObjectConverters\.Equal[^}]*ConverterParameter=\d"), xaml);

        // Every header uses the string-tolerant command, never a typed SelectStepCommand.
        Assert.DoesNotContain("{Binding SelectStepCommand}", xaml);
        Assert.Equal(6, Regex.Matches(xaml, @"Command=""\{Binding SelectStepFromHeaderCommand\}""").Count);

        // Each body is bound to its typed flag.
        for (int n = 1; n <= 6; n++)
        {
            Assert.Contains($"IsVisible=\"{{Binding IsStep{n}Active}}\"", xaml);
        }
    }

    [Fact]
    public void Xaml_StepHeaders_HaveNoLocalBackground_SoHoverTemplateApplies()
    {
        string xaml = File.ReadAllText(FindMainWindowXaml());
        foreach (Match m in Regex.Matches(xaml, @"<Button x:Name=""Step\dHeader""[^>]*>"))
        {
            Assert.DoesNotContain(" Background=", m.Value);
            Assert.Contains("Classes=\"step-header\"", m.Value);
        }
    }
}

