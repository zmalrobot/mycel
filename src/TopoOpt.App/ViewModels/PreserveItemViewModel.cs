using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TopoOpt.Core.Workflow;

namespace TopoOpt.App.ViewModels;

public partial class PreserveItemViewModel : ObservableObject
{
    public PreserveRegion Model { get; }
    public Guid Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _triangleCount;

    [ObservableProperty]
    private bool _isPicking;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981 Green
    public uint ColorRgba => 0x10B981FFu;

    public event Action<PreserveItemViewModel>? Changed;

    public PreserveItemViewModel(PreserveRegion model)
    {
        Model = model;
        _name = model.Name;
        _triangleCount = model.Triangles.Length;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
        Changed?.Invoke(this);
    }

    public void UpdateTriangles(int[] triangles)
    {
        Model.Triangles = triangles;
        TriangleCount = triangles.Length;
        Changed?.Invoke(this);
    }
}

