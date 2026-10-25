using System;
using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TopoOpt.Core.Workflow;

namespace TopoOpt.App.ViewModels;

public partial class LoadItemViewModel : ObservableObject
{
    public LoadRegion Model { get; }
    public Guid Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private float _magnitudeNewtons;

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
        Model.MagnitudeNewtons = value;
        Changed?.Invoke(this);
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
