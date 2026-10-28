using System;
using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mycel.Core.Workflow;

namespace Mycel.App.ViewModels;

public partial class LoadItemViewModel : ObservableObject
{
    /// <summary>
    /// Standard gravitational acceleration (g0) according to ISO 80000-3 / NIST in m/s^2.
    /// Used for exact bidirectional conversion between force (N) and equivalent mass (kg, g): F = m * g.
    /// </summary>
    public const float StandardGravity = 9.80665f;

    public LoadRegion Model { get; }
    public Guid Id => Model.Id;

    private bool _isSyncing;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private float _magnitudeNewtons;

    [ObservableProperty]
    private float _magnitudeKg;

    [ObservableProperty]
    private float _magnitudeGrams;

    [ObservableProperty]
    private LoadDirectionMode _directionMode;

    [ObservableProperty]
    private float _customX;

    [ObservableProperty]
    private float _customY;

    [ObservableProperty]
    private float _customZ;

    [ObservableProperty]
    private int _triangleCount;

    [ObservableProperty]
    private float _areaMm2;

    [ObservableProperty]
    private string _summaryText = "0 triangles · 0.0 mm²";

    [ObservableProperty]
    private float _nominalPressureMpa;

    [ObservableProperty]
    private string _nominalPressureText = "0.00 MPa";

    [ObservableProperty]
    private IBrush _pressureBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // Neutral grey

    [ObservableProperty]
    private bool _isHighCoverage;

    [ObservableProperty]
    private string _highCoverageTooltip = string.Empty;

    [ObservableProperty]
    private bool _isWeakNormal;

    [ObservableProperty]
    private string _weakNormalTooltip = string.Empty;

    [ObservableProperty]
    private bool _isDisconnected;

    [ObservableProperty]
    private string _disconnectedTooltip = string.Empty;

    [ObservableProperty]
    private bool _isHovered;

    [ObservableProperty]
    private bool _isPicking;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(249, 115, 22)); // #F97316 Orange
    public uint ColorRgba => 0xF97316FFu;

    public event Action<LoadItemViewModel>? Changed;

    public LoadItemViewModel(LoadRegion model)
    {
        Model = model;
        _name = model.Name;
        _magnitudeNewtons = model.MagnitudeNewtons;
        _magnitudeKg = model.MagnitudeNewtons / StandardGravity;
        _magnitudeGrams = _magnitudeKg * 1000f;
        _directionMode = model.DirectionMode;
        _customX = model.CustomDirection.X;
        _customY = model.CustomDirection.Y;
        _customZ = model.CustomDirection.Z;
        _triangleCount = model.Triangles.Length;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
        Changed?.Invoke(this);
    }

    partial void OnMagnitudeNewtonsChanged(float value)
    {
        if (_isSyncing || float.IsNaN(value) || float.IsInfinity(value)) return;
        _isSyncing = true;
        try
        {
            MagnitudeKg = value / StandardGravity;
            MagnitudeGrams = MagnitudeKg * 1000f;
            Model.MagnitudeNewtons = value;
            Changed?.Invoke(this);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnMagnitudeKgChanged(float value)
    {
        if (_isSyncing || float.IsNaN(value) || float.IsInfinity(value)) return;
        _isSyncing = true;
        try
        {
            MagnitudeNewtons = value * StandardGravity;
            MagnitudeGrams = value * 1000f;
            Model.MagnitudeNewtons = MagnitudeNewtons;
            Changed?.Invoke(this);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnMagnitudeGramsChanged(float value)
    {
        if (_isSyncing || float.IsNaN(value) || float.IsInfinity(value)) return;
        _isSyncing = true;
        try
        {
            MagnitudeKg = value / 1000f;
            MagnitudeNewtons = MagnitudeKg * StandardGravity;
            Model.MagnitudeNewtons = MagnitudeNewtons;
            Changed?.Invoke(this);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnDirectionModeChanged(LoadDirectionMode value)
    {
        Model.DirectionMode = value;
        Changed?.Invoke(this);
    }

    partial void OnCustomXChanged(float value) => UpdateCustomDir();
    partial void OnCustomYChanged(float value) => UpdateCustomDir();
    partial void OnCustomZChanged(float value) => UpdateCustomDir();

    private void UpdateCustomDir()
    {
        Model.CustomDirection = new Vector3(CustomX, CustomY, CustomZ);
        Changed?.Invoke(this);
    }

    public void UpdateTriangles(int[] triangles)
    {
        Model.Triangles = triangles;
        TriangleCount = triangles.Length;
        Changed?.Invoke(this);
    }

    public void RefreshMetrics(float materialYieldStrength = 50f)
    {
        AreaMm2 = Model.SurfaceAreaMm2;
        SummaryText = $"{TriangleCount} triangles · {AreaMm2:N1} mm²";
        NominalPressureMpa = Model.NominalPressureMpa;
        NominalPressureText = $"{NominalPressureMpa:F2} MPa";

        float yield = materialYieldStrength > 0f ? materialYieldStrength : 50f;
        float ratio = NominalPressureMpa / yield;

        PressureBrush = ratio > 1.0f
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))    // Red (> 100% yield)
            : ratio >= 0.5f
                ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Amber (50-100% yield)
                : new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green (< 50% yield)

        IsHighCoverage = Model.IsHighCoverage;
        HighCoverageTooltip = $"Selection covers {Model.ModelSurfaceCoveragePercent:F1}% of the entire surface area (> 40%).";

        IsWeakNormal = Model.IsWeakNormal;
        WeakNormalTooltip = $"Opposing normals on curved/closed selection (|Σ A·n|/Σ A = {Model.NormalConsistencyRatio:F2} < 0.30).\nUsing seed normal ({Model.FallbackSeedNormal.X:F2}, {Model.FallbackSeedNormal.Y:F2}, {Model.FallbackSeedNormal.Z:F2}) for force vector.";

        IsDisconnected = Model.IsDisconnected;
        DisconnectedTooltip = $"Selection contains {Model.ConnectedComponentCount} separate disconnected pieces.";
    }
}
