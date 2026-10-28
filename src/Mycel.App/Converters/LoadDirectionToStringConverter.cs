using System;
using System.Globalization;
using System.Resources;
using Avalonia.Data.Converters;
using Mycel.Core.Workflow;

namespace Mycel.App.Converters;

/// <summary>
/// Converts a LoadDirectionMode enum value into a friendly human-readable localized string.
/// </summary>
public sealed class LoadDirectionToStringConverter : IValueConverter
{
    public static readonly LoadDirectionToStringConverter Instance = new();
    private static readonly ResourceManager s_rm = new("Mycel.App.Resources.Strings", typeof(LoadDirectionToStringConverter).Assembly);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LoadDirectionMode mode)
        {
            return value?.ToString() ?? string.Empty;
        }

        return mode switch
        {
            LoadDirectionMode.SurfaceNormalInward => s_rm.GetString("Stepper.Loads.Direction.SurfaceNormalInward", culture) ?? "Into the surface (along normal)",
            LoadDirectionMode.SurfaceNormalOutward => s_rm.GetString("Stepper.Loads.Direction.SurfaceNormalOutward", culture) ?? "Out of the surface",
            LoadDirectionMode.AxisPositiveX => s_rm.GetString("Stepper.Loads.Direction.AxisPositiveX", culture) ?? "+X (Right)",
            LoadDirectionMode.AxisNegativeX => s_rm.GetString("Stepper.Loads.Direction.AxisNegativeX", culture) ?? "−X (Left)",
            LoadDirectionMode.AxisPositiveY => s_rm.GetString("Stepper.Loads.Direction.AxisPositiveY", culture) ?? "+Y (Front)",
            LoadDirectionMode.AxisNegativeY => s_rm.GetString("Stepper.Loads.Direction.AxisNegativeY", culture) ?? "−Y (Back)",
            LoadDirectionMode.AxisPositiveZ => s_rm.GetString("Stepper.Loads.Direction.AxisPositiveZ", culture) ?? "+Z (Up)",
            LoadDirectionMode.AxisNegativeZ => s_rm.GetString("Stepper.Loads.Direction.AxisNegativeZ", culture) ?? "−Z (Down)",
            LoadDirectionMode.CustomVector => s_rm.GetString("Stepper.Loads.Direction.CustomVector", culture) ?? "Custom vector…",
            _ => mode.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
