namespace TopoOpt.Core.Geometry;

/// <summary>
/// Physical length unit of an imported CAD/STL model.
/// Internal solver units are always Millimeters (mm).
/// </summary>
public enum LengthUnit
{
    Millimeter,
    Centimeter,
    Meter,
    Inch
}

public static class LengthUnitExtensions
{
    public static float ToMillimetersScale(this LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => 1.0f,
        LengthUnit.Centimeter => 10.0f,
        LengthUnit.Meter => 1000.0f,
        LengthUnit.Inch => 25.4f,
        _ => 1.0f
    };
}

