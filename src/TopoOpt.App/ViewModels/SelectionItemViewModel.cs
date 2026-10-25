using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TopoOpt.Core.Geometry;

namespace TopoOpt.App.ViewModels;

public sealed record ColorOption(string Name, uint Rgba, IBrush Brush);

public partial class SelectionItemViewModel : ObservableObject
{
    public SurfaceSelection Model { get; }

    public Guid Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private SelectionKind _kind;

    [ObservableProperty]
    private uint _colorRgba;

    [ObservableProperty]
    private IBrush _colorBrush;

    [ObservableProperty]
    private int _triangleCount;

    public event Action<SelectionItemViewModel>? OverlayChanged;

    public SelectionItemViewModel(SurfaceSelection model)
    {
        Model = model;
        _name = model.Name;
        _kind = model.Kind;
        _colorRgba = model.ColorRgba;
        _colorBrush = ToBrush(model.ColorRgba);
        _triangleCount = model.TriangleIndices.Length;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
    }

    partial void OnKindChanged(SelectionKind value)
    {
        Model.Kind = value;
        uint defaultCol = SurfaceSelection.DefaultColorForKind(value);
        ColorRgba = defaultCol;
    }

    partial void OnColorRgbaChanged(uint value)
    {
        Model.ColorRgba = value;
        ColorBrush = ToBrush(value);
        OverlayChanged?.Invoke(this);
    }

    public void UpdateTriangles(int[] sortedIndices)
    {
        Model.TriangleIndices = sortedIndices;
        TriangleCount = sortedIndices.Length;
        OverlayChanged?.Invoke(this);
    }

    public static IBrush ToBrush(uint rgba)
    {
        byte r = (byte)((rgba >> 24) & 0xFFu);
        byte g = (byte)((rgba >> 16) & 0xFFu);
        byte b = (byte)((rgba >> 8) & 0xFFu);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}

