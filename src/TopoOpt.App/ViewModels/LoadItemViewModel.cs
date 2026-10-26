using System;
using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TopoOpt.Core.Workflow;

namespace TopoOpt.App.ViewModels;

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
}
