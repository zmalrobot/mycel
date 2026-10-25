using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TopoOpt.Core.Workflow;

namespace TopoOpt.App.ViewModels;

public partial class SupportItemViewModel : ObservableObject
{
    public SupportRegion Model { get; }
    public Guid Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _fixX;

    [ObservableProperty]
    private bool _fixY;

    [ObservableProperty]
    private bool _fixZ;

    [ObservableProperty]
    private int _triangleCount;

    [ObservableProperty]
    private bool _isPicking;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // #3B82F6 Blue
    public uint ColorRgba => 0x3B82F6FFu;

    public event Action<SupportItemViewModel>? Changed;

    public SupportItemViewModel(SupportRegion model)
    {
        Model = model;
        _name = model.Name;
        _fixX = model.FixX;
        _fixY = model.FixY;
        _fixZ = model.FixZ;
        _triangleCount = model.Triangles.Length;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
        Changed?.Invoke(this);
    }

    partial void OnFixXChanged(bool value)
    {
        Model.FixX = value;
        Changed?.Invoke(this);
    }

    partial void OnFixYChanged(bool value)
    {
        Model.FixY = value;
        Changed?.Invoke(this);
    }

    partial void OnFixZChanged(bool value)
    {
        Model.FixZ = value;
        Changed?.Invoke(this);
    }

    public void UpdateTriangles(int[] triangles)
    {
        Model.Triangles = triangles;
        TriangleCount = triangles.Length;
        Changed?.Invoke(this);
    }
}

