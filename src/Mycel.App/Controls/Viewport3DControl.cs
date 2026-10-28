using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.OpenGL;
using Mycel.App.Diagnostics;
using Mycel.App.Rendering;
using Mycel.Core.Geometry;

namespace Mycel.App.Controls;

public enum ViewRenderMode
{
    OriginalStl,
    Voxels
}

/// <summary>
/// Hardware-accelerated 3D viewport control built on Avalonia's <see cref="OpenGlControlBase"/>
/// and <see cref="Silk.NET.OpenGL"/>. Supports both Desktop OpenGL 3.3+ Core and OpenGL ES 3.0+ (ANGLE/Mesa).
/// All GPU resources are created, updated, and destroyed strictly on the render thread.
/// </summary>
public sealed class Viewport3DControl : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct GpuVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Color;
    }

    private sealed class OverlayBuffer
    {
        public uint Ebo;
        public int IndexCount;
        public Vector4 Color;
        public int[]? PendingTriangles;
        public bool Dirty;
    }

    private GL? _gl;
    private bool _glInitialized;

    private uint _meshShader;
    private int _uViewProjLoc = -1;
    private int _uEyePosLoc = -1;
    private int _uOverrideColorLoc = -1;
    private int _uUseVertexColorLoc = -1;

    private uint _lineShader;
    private int _uLineViewProjLoc = -1;

    private uint _meshVao;
    private uint _meshVbo;
    private uint _meshEbo;
    private int _meshIndexCount;

    private uint _hoverEbo;
    private int _hoverIndexCount;
    private Vector4 _hoverColor = new(1.0f, 0.78f, 0.15f, 0.65f);

    private uint _gridVao;
    private uint _gridVbo;
    private int _gridVertexCount;

    private uint _bboxVao;
    private uint _bboxVbo;
    private int _bboxVertexCount;
    private bool _showDimensions = true;

    public bool ShowDimensions
    {
        get => _showDimensions;
        set
        {
            if (_showDimensions != value)
            {
                _showDimensions = value;
                RequestNextFrameRendering();
            }
        }
    }

    private uint _voxelVao;
    private uint _voxelVbo;
    private uint _voxelEbo;
    private int _voxelIndexCount;
    private IndexedMesh? _pendingVoxelMesh;
    private bool _voxelMeshUploadPending;

    private uint _ghostVao;
    private uint _ghostVbo;
    private uint _ghostEbo;
    private int _ghostIndexCount;
    private IndexedMesh? _pendingGhostMesh;
    private bool _ghostMeshUploadPending;
    private bool _showGhostOriginal;

    private uint _patchPointsVao;
    private uint _patchPointsVbo;
    private int _patchPointsCount;
    private GpuVertex[]? _pendingPatchPoints;
    private bool _patchPointsUploadPending;

    private uint _glyphVao;
    private uint _glyphVbo;
    private int _glyphVertexCount;
    private GpuVertex[]? _pendingGlyphVertices;
    private bool _glyphUploadPending;

    private uint _resultantArrowVao;
    private uint _resultantArrowVbo;
    private int _resultantArrowVertexCount;
    private GpuVertex[]? _pendingResultantArrowVertices;
    private bool _resultantArrowUploadPending;

    private ViewRenderMode _renderMode = ViewRenderMode.OriginalStl;
    public ViewRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (_renderMode != value)
            {
                _renderMode = value;
                RequestNextFrameRendering();
            }
        }
    }

    private readonly Dictionary<Guid, OverlayBuffer> _overlays = new();
    private readonly List<Guid> _overlaysToRemove = new();
    private bool _clearAllOverlaysPending;

    private readonly object _syncLock = new();
    private IndexedMesh? _currentMesh;
    private IndexedMesh? _pendingMesh;
    private bool _meshUploadPending;
    private TaskCompletionSource? _pendingMeshTcs;

    private GpuVertex[]? _stagedVertices;
    private bool _vertexColorUpdatePending;
    private bool _useVertexColors;

    private int[]? _pendingHoverTriangles;
    private bool _hoverUploadPending;

    private readonly OrbitCamera _camera = new();
    private Point _lastPointerPos;
    private Point _pressPointerPos;
    private bool _isOrbiting;
    private bool _isPanning;
    private bool _pointerMovedSignificantly;

    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private int _framesInSecond;

    public bool GlInitFailed { get; private set; }
    public bool IsGlInitialized => _glInitialized;
    public string? GlInitErrorMessage { get; private set; }
    public event Action<string>? GlInitFailedNotification;

    public OrbitCamera Camera => _camera;
    public double CurrentFps { get; private set; }
    public int TotalRenderedFrames { get; private set; }
    public string GlProfileSummary { get; private set; } = "Initializing OpenGL...";

    public event Action<Vector2>? ViewportPointerMoved;
    public event Action<Vector2, KeyModifiers>? ViewportPointerClicked;
    public event Action<double, string>? DiagnosticsUpdated;

    public Viewport3DControl()
    {
        Focusable = true;
    }

    public bool HitTest(Point point)
    {
        return new Rect(Bounds.Size).Contains(point);
    }

    public new void RequestNextFrameRendering()
    {
        RenderLoopProbe.Instance.OnFrameRequested();
        base.RequestNextFrameRendering();
    }

    public void SetMesh(IndexedMesh? mesh)
    {
        lock (_syncLock)
        {
            _currentMesh = mesh;
            _pendingMesh = mesh;
            _meshUploadPending = true;
            _useVertexColors = false;
            _clearAllOverlaysPending = true;
            _pendingHoverTriangles = [];
            _hoverUploadPending = true;

            if (mesh is not null && mesh.TriangleCount > 0)
            {
                _camera.FitToBounds(mesh.BBox.Min, mesh.BBox.Max);
                PrepareStagedVerticesLocked(mesh, null, ColorMapKind.Viridis, 0f, 1f);
            }
            else
            {
                _stagedVertices = null;
            }
        }
        RequestNextFrameRendering();
    }

    public Task UpdateMeshAsync(IndexedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pre-build GPU vertex staging array on the calling background thread so render thread only calls BufferData
        int vCount = mesh.VertexCount;
        GpuVertex[] staged = new GpuVertex[vCount];
        ReadOnlySpan<Vector3> pos = mesh.Positions.Span;
        ReadOnlySpan<Vector3> norm = mesh.SmoothVertexNormals.Span;
        Vector3 defaultCol = new(0.72f, 0.76f, 0.82f);

        for (int i = 0; i < vCount; i++)
        {
            staged[i] = new GpuVertex
            {
                Position = pos[i],
                Normal = norm[i],
                Color = defaultCol
            };
        }

        lock (_syncLock)
        {
            _pendingMeshTcs?.TrySetResult();
            _currentMesh = mesh;
            _pendingMesh = mesh;
            _stagedVertices = staged;
            _useVertexColors = false;
            _meshUploadPending = true;
            _pendingMeshTcs = tcs;
        }

        Dispatcher.UIThread.Post(RequestNextFrameRendering);
        return tcs.Task;
    }

    public void SetVertexScalars(ReadOnlySpan<float> values, ColorMapKind map, float min, float max)
    {
        lock (_syncLock)
        {
            if (_currentMesh is null || values.Length != _currentMesh.VertexCount)
            {
                return;
            }

            PrepareStagedVerticesLocked(_currentMesh, values, map, min, max);
            _useVertexColors = true;
            _vertexColorUpdatePending = true;
        }
        RequestNextFrameRendering();
    }

    public void SetOverlayTriangles(Guid selectionId, ReadOnlySpan<int> triangles, uint colorRgba)
    {
        int[] copy = triangles.ToArray();
        Vector4 col = ColorMap.UnpackRgba(colorRgba);
        col.W = Math.Clamp(col.W * 0.78f, 0.35f, 0.85f);

        lock (_syncLock)
        {
            if (!_overlays.TryGetValue(selectionId, out OverlayBuffer? buf))
            {
                buf = new OverlayBuffer();
                _overlays[selectionId] = buf;
            }
            buf.Color = col;
            buf.PendingTriangles = copy;
            buf.Dirty = true;
        }
        RequestNextFrameRendering();
    }

    public void RemoveOverlay(Guid selectionId)
    {
        lock (_syncLock)
        {
            _overlaysToRemove.Add(selectionId);
        }
        RequestNextFrameRendering();
    }

    public void ClearOverlays()
    {
        lock (_syncLock)
        {
            _clearAllOverlaysPending = true;
        }
        RequestNextFrameRendering();
    }

    public void SetHoverTriangles(ReadOnlySpan<int> triangles, uint colorRgba = 0xFBBF24B8u)
    {
        int[] copy = triangles.ToArray();
        lock (_syncLock)
        {
            _pendingHoverTriangles = copy;
            _hoverColor = ColorMap.UnpackRgba(colorRgba);
            _hoverUploadPending = true;
        }
        RequestNextFrameRendering();
    }

    public Point? WorldToScreen(Vector3 worldPoint)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 1.0 || h <= 1.0) return null;

        float aspect = (float)(w / Math.Max(1.0, h));
        Matrix4x4 vp = _camera.GetViewMatrix() * _camera.GetProjectionMatrix(aspect);
        Vector4 clip = Vector4.Transform(new Vector4(worldPoint, 1.0f), vp);
        if (clip.W <= 0.001f) return null;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f) return null;

        double screenX = (ndcX + 1.0) * 0.5 * w;
        double screenY = (1.0 - ndcY) * 0.5 * h;
        return new Point(screenX, screenY);
    }

    public void SetVoxelMesh(IndexedMesh? mesh)
    {
        lock (_syncLock)
        {
            _pendingVoxelMesh = mesh;
            _voxelMeshUploadPending = true;
        }
        RequestNextFrameRendering();
    }

    public void SetGhostOriginalMesh(IndexedMesh? mesh, bool visible)
    {
        lock (_syncLock)
        {
            _pendingGhostMesh = mesh;
            _ghostMeshUploadPending = true;
            _showGhostOriginal = visible;
        }
        RequestNextFrameRendering();
    }

    public void SetShowGhostOriginal(bool visible)
    {
        lock (_syncLock)
        {
            _showGhostOriginal = visible;
        }
        RequestNextFrameRendering();
    }

    public void SetPatchPoints(ReadOnlySpan<Vector3> points, uint colorRgba)
    {
        Vector4 col = ColorMap.UnpackRgba(colorRgba);
        GpuVertex[] arr = new GpuVertex[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            arr[i] = new GpuVertex
            {
                Position = points[i],
                Normal = Vector3.Zero,
                Color = new Vector3(col.X, col.Y, col.Z)
            };
        }

        lock (_syncLock)
        {
            _pendingPatchPoints = arr;
            _patchPointsUploadPending = true;
        }
        RequestNextFrameRendering();
    }

    public sealed record BoundaryGlyphData(
        Guid RegionId,
        bool IsLoad,
        Vector3 Position,
        Vector3 Direction,
        float Magnitude,
        bool IsHovered = false,
        IReadOnlyList<(Vector3 Point, Vector3 Normal)>? DistributedSamples = null);

    public void SetBoundaryGlyphs(IReadOnlyList<BoundaryGlyphData>? glyphs, float bboxDiag)
    {
        var (resultant, others) = (glyphs != null && glyphs.Count > 0)
            ? BuildGlyphMeshes(glyphs, bboxDiag)
            : (null, null);

        lock (_syncLock)
        {
            _pendingResultantArrowVertices = resultant;
            _resultantArrowUploadPending = true;

            _pendingGlyphVertices = others;
            _glyphUploadPending = true;
        }
        RequestNextFrameRendering();
    }

    public void SetFeaScalarField(ReadOnlySpan<float> values, LutKind lutKind, float min, float max)
    {
        lock (_syncLock)
        {
            if (_currentMesh is null || values.Length != _currentMesh.VertexCount)
                return;

            int vCount = _currentMesh.VertexCount;
            _stagedVertices ??= new GpuVertex[vCount];
            ReadOnlySpan<Vector3> pos = _currentMesh.Positions.Span;
            ReadOnlySpan<Vector3> norm = _currentMesh.SmoothVertexNormals.Span;

            for (int i = 0; i < vCount; i++)
            {
                Vector3 col = LutColorMap.Evaluate(lutKind, values[i], min, max);
                _stagedVertices[i] = new GpuVertex
                {
                    Position = pos[i],
                    Normal = i < norm.Length ? norm[i] : Vector3.UnitZ,
                    Color = col
                };
            }

            _useVertexColors = true;
            _vertexColorUpdatePending = true;
        }
        RequestNextFrameRendering();
    }

    public void ClearFeaScalarField()
    {
        lock (_syncLock)
        {
            if (_currentMesh is not null)
            {
                PrepareStagedVerticesLocked(_currentMesh, null, ColorMapKind.Viridis, 0f, 1f);
                _useVertexColors = false;
                _vertexColorUpdatePending = true;
            }
        }
        RequestNextFrameRendering();
    }

    private static (GpuVertex[]? Resultant, GpuVertex[]? Others) BuildGlyphMeshes(
        IReadOnlyList<BoundaryGlyphData> glyphs,
        float bboxDiag)
    {
        var resultantList = new List<GpuVertex>();
        var othersList = new List<GpuVertex>();

        float arrowLen = Math.Clamp(bboxDiag * 0.22f, 8.0f, 180.0f);
        float shaftRadius = 0.06f * arrowLen;
        float headLen = 0.30f * arrowLen;
        float headRadius = 0.16f * arrowLen;

        float smallArrowLen = arrowLen * 0.35f;
        float smallShaftRadius = shaftRadius * 0.45f;
        float smallHeadLen = headLen * 0.40f;
        float smallHeadRadius = headRadius * 0.50f;

        float anchorSize = Math.Clamp(bboxDiag * 0.06f, 3.0f, 60.0f);

        Vector3 orangeNormal = new(0.976f, 0.451f, 0.086f); // #F97316
        Vector3 orangeHovered = new(1.0f, 0.65f, 0.20f);    // #FFA726
        Vector3 blueNormal = new(0.231f, 0.510f, 0.965f);   // #3B82F6
        Vector3 blueHovered = new(0.38f, 0.68f, 1.0f);

        foreach (var g in glyphs)
        {
            if (g.IsLoad)
            {
                Vector3 dir = g.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(g.Direction) : -Vector3.UnitZ;
                Vector3 color = g.IsHovered ? orangeHovered : orangeNormal;
                float currentShaftRadius = g.IsHovered ? shaftRadius * 1.25f : shaftRadius;

                // 1. Resultant Arrow (anchored at surface AnchorPoint, pointing along force direction)
                Vector3 start = g.Position - dir * arrowLen;
                Vector3 headBase = g.Position - dir * headLen;
                Vector3 tip = g.Position;

                AddCylinderTriangles(resultantList, start, headBase, currentShaftRadius, dir, color);
                AddConeTriangles(resultantList, headBase, tip, headRadius, dir, color);

                // 2. Distributed Small Arrows over patch
                if (g.DistributedSamples != null && g.DistributedSamples.Count > 0)
                {
                    Vector3 sampleColor = color * 0.9f;
                    foreach (var (pt, _) in g.DistributedSamples)
                    {
                        Vector3 sStart = pt - dir * smallArrowLen;
                        Vector3 sHeadBase = pt - dir * smallHeadLen;
                        Vector3 sTip = pt;

                        AddCylinderTriangles(othersList, sStart, sHeadBase, smallShaftRadius, dir, sampleColor);
                        AddConeTriangles(othersList, sHeadBase, sTip, smallHeadRadius, dir, sampleColor);
                    }
                }
            }
            else
            {
                // Support anchor: 4-sided pyramid pointing at AnchorPoint on surface
                Vector3 color = g.IsHovered ? blueHovered : blueNormal;
                AddPyramidAnchor(othersList, g.Position, anchorSize, color);
            }
        }

        return (
            resultantList.Count > 0 ? resultantList.ToArray() : null,
            othersList.Count > 0 ? othersList.ToArray() : null);
    }

    private static void AddCylinderTriangles(
        List<GpuVertex> vertices,
        Vector3 p0,
        Vector3 p1,
        float radius,
        Vector3 axis,
        Vector3 color)
    {
        // Build perpendicular basis
        Vector3 up = Math.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 side = Vector3.Normalize(Vector3.Cross(axis, up));
        Vector3 normUp = Vector3.Cross(side, axis);

        int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float a0 = (float)(2.0 * Math.PI * i / segments);
            float a1 = (float)(2.0 * Math.PI * (i + 1) / segments);

            Vector3 r0 = side * (float)Math.Cos(a0) + normUp * (float)Math.Sin(a0);
            Vector3 r1 = side * (float)Math.Cos(a1) + normUp * (float)Math.Sin(a1);

            Vector3 v00 = p0 + r0 * radius;
            Vector3 v01 = p0 + r1 * radius;
            Vector3 v10 = p1 + r0 * radius;
            Vector3 v11 = p1 + r1 * radius;

            // Two triangles for cylinder quad
            vertices.Add(new GpuVertex { Position = v00, Normal = r0, Color = color });
            vertices.Add(new GpuVertex { Position = v10, Normal = r0, Color = color });
            vertices.Add(new GpuVertex { Position = v11, Normal = r1, Color = color });

            vertices.Add(new GpuVertex { Position = v00, Normal = r0, Color = color });
            vertices.Add(new GpuVertex { Position = v11, Normal = r1, Color = color });
            vertices.Add(new GpuVertex { Position = v01, Normal = r1, Color = color });
        }
    }

    private static void AddConeTriangles(
        List<GpuVertex> vertices,
        Vector3 pBase,
        Vector3 tip,
        float radius,
        Vector3 axis,
        Vector3 color)
    {
        Vector3 up = Math.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 side = Vector3.Normalize(Vector3.Cross(axis, up));
        Vector3 normUp = Vector3.Cross(side, axis);

        int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float a0 = (float)(2.0 * Math.PI * i / segments);
            float a1 = (float)(2.0 * Math.PI * (i + 1) / segments);

            Vector3 r0 = side * (float)Math.Cos(a0) + normUp * (float)Math.Sin(a0);
            Vector3 r1 = side * (float)Math.Cos(a1) + normUp * (float)Math.Sin(a1);

            Vector3 b0 = pBase + r0 * radius;
            Vector3 b1 = pBase + r1 * radius;

            Vector3 coneNormal0 = Vector3.Normalize(r0 + axis * 0.5f);
            Vector3 coneNormal1 = Vector3.Normalize(r1 + axis * 0.5f);

            // Cone side triangle
            vertices.Add(new GpuVertex { Position = b0, Normal = coneNormal0, Color = color });
            vertices.Add(new GpuVertex { Position = tip, Normal = (coneNormal0 + coneNormal1) * 0.5f, Color = color });
            vertices.Add(new GpuVertex { Position = b1, Normal = coneNormal1, Color = color });

            // Base disk triangle
            vertices.Add(new GpuVertex { Position = pBase, Normal = -axis, Color = color });
            vertices.Add(new GpuVertex { Position = b1, Normal = -axis, Color = color });
            vertices.Add(new GpuVertex { Position = b0, Normal = -axis, Color = color });
        }
    }

    private static void AddPyramidAnchor(
        List<GpuVertex> vertices,
        Vector3 tip,
        float size,
        Vector3 color)
    {
        // 4-sided pyramid pointing at tip, elevated along +Z
        float half = size * 0.5f;
        Vector3 baseC = tip + Vector3.UnitZ * size;
        Vector3 c0 = baseC + new Vector3(-half, -half, 0);
        Vector3 c1 = baseC + new Vector3( half, -half, 0);
        Vector3 c2 = baseC + new Vector3( half,  half, 0);
        Vector3 c3 = baseC + new Vector3(-half,  half, 0);

        // 4 side triangles
        AddTri(vertices, tip, c0, c1, color);
        AddTri(vertices, tip, c1, c2, color);
        AddTri(vertices, tip, c2, c3, color);
        AddTri(vertices, tip, c3, c0, color);

        // Base square
        AddTri(vertices, c0, c2, c1, color);
        AddTri(vertices, c0, c3, c2, color);
    }

    private static void AddTri(List<GpuVertex> vertices, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 color)
    {
        Vector3 norm = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
        vertices.Add(new GpuVertex { Position = p0, Normal = norm, Color = color });
        vertices.Add(new GpuVertex { Position = p1, Normal = norm, Color = color });
        vertices.Add(new GpuVertex { Position = p2, Normal = norm, Color = color });
    }

    public void FitView()
    {
        lock (_syncLock)
        {
            if (_currentMesh is not null && _currentMesh.TriangleCount > 0)
            {
                _camera.FitToBounds(_currentMesh.BBox.Min, _currentMesh.BBox.Max);
            }
        }
        RequestNextFrameRendering();
    }

    public void RequestRedraw() => RequestNextFrameRendering();

    private void PrepareStagedVerticesLocked(
        IndexedMesh mesh,
        ReadOnlySpan<float> scalars,
        ColorMapKind map,
        float min,
        float max)
    {
        int vCount = mesh.VertexCount;
        if (_stagedVertices is null || _stagedVertices.Length != vCount)
        {
            _stagedVertices = new GpuVertex[vCount];
        }

        ReadOnlySpan<Vector3> pos = mesh.Positions.Span;
        ReadOnlySpan<Vector3> norm = mesh.SmoothVertexNormals.Span;
        bool hasScalars = !scalars.IsEmpty && scalars.Length == vCount;
        Vector3 defaultCol = new(0.72f, 0.76f, 0.82f);

        for (int i = 0; i < vCount; i++)
        {
            _stagedVertices[i] = new GpuVertex
            {
                Position = pos[i],
                Normal = norm[i],
                Color = hasScalars ? ColorMap.Evaluate(map, scalars[i], min, max) : defaultCol
            };
        }
    }

    private static GpuVertex[] BuildGpuVertices(IndexedMesh mesh)
    {
        int vCount = mesh.VertexCount;
        GpuVertex[] arr = new GpuVertex[vCount];
        ReadOnlySpan<Vector3> pos = mesh.Positions.Span;
        ReadOnlySpan<Vector3> norm = mesh.SmoothVertexNormals.Span;
        Vector3 defaultCol = new(0.72f, 0.76f, 0.82f);

        for (int i = 0; i < vCount; i++)
        {
            arr[i] = new GpuVertex
            {
                Position = pos[i],
                Normal = i < norm.Length ? norm[i] : Vector3.UnitZ,
                Color = defaultCol
            };
        }
        return arr;
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);

        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);

            bool isGles = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
            string preamble = isGles
                ? "#version 300 es\nprecision highp float;\nprecision highp int;\n"
                : "#version 330 core\n";

            string renderer = _gl.GetStringS(StringName.Renderer) ?? "Unknown GPU";
            string version = _gl.GetStringS(StringName.Version) ?? "Unknown GL";
            string glsl = _gl.GetStringS(StringName.ShadingLanguageVersion) ?? "Unknown GLSL";
            GlProfileSummary = $"{renderer} | {version} ({glsl})";

            _meshShader = CompileProgram(_gl, preamble + MeshVertexShaderBody, preamble + MeshFragmentShaderBody);
            _uViewProjLoc = _gl.GetUniformLocation(_meshShader, "uViewProj");
            _uEyePosLoc = _gl.GetUniformLocation(_meshShader, "uEyePos");
            _uOverrideColorLoc = _gl.GetUniformLocation(_meshShader, "uOverrideColor");
            _uUseVertexColorLoc = _gl.GetUniformLocation(_meshShader, "uUseVertexColor");

            _lineShader = CompileProgram(_gl, preamble + LineVertexShaderBody, preamble + LineFragmentShaderBody);
            _uLineViewProjLoc = _gl.GetUniformLocation(_lineShader, "uViewProj");

            _meshVao = _gl.GenVertexArray();
            _meshVbo = _gl.GenBuffer();
            _meshEbo = _gl.GenBuffer();
            _hoverEbo = _gl.GenBuffer();

            _gl.BindVertexArray(_meshVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _meshVbo);

            uint stride = (uint)sizeof(GpuVertex);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);

            _gl.BindVertexArray(0);

            _glyphVao = _gl.GenVertexArray();
            _glyphVbo = _gl.GenBuffer();
            _gl.BindVertexArray(_glyphVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _glyphVbo);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);
            _gl.BindVertexArray(0);

            _resultantArrowVao = _gl.GenVertexArray();
            _resultantArrowVbo = _gl.GenBuffer();
            _gl.BindVertexArray(_resultantArrowVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _resultantArrowVbo);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);
            _gl.BindVertexArray(0);

            BuildReferenceGrid(_gl, new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 0f));

            _glInitialized = true;
            GlInitFailed = false;

            lock (_syncLock)
            {
                if (_currentMesh is not null)
                {
                    _meshUploadPending = true;
                }
            }
        }
        catch (Exception ex)
        {
            _glInitialized = false;
            GlInitFailed = true;
            string renderer = "Unknown";
            try { renderer = _gl?.GetStringS(StringName.Renderer) ?? "Unknown"; } catch { }
            GlInitErrorMessage = $"OpenGL initialization failed on GPU '{renderer}': {ex.Message}. " +
                                 $"Ensure OpenGL 3.3+ or OpenGL ES 3.0+ drivers are installed, or run with LIBGL_ALWAYS_SOFTWARE=1.";
            GlProfileSummary = $"GL Failure: {renderer}";
            DiagnosticsUpdated?.Invoke(0.0, GlProfileSummary);
            GlInitFailedNotification?.Invoke(GlInitErrorMessage);
            Trace.WriteLine($"[GL Error] {GlInitErrorMessage}");
        }
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_glInitialized || GlInitFailed || _gl is null)
        {
            OrbitDiagnostics.RecordRenderSkipped($"Initialized={_glInitialized}, Failed={GlInitFailed}, GlNull={_gl is null}");
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int fbWidth = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int fbHeight = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        OrbitDiagnostics.RecordRenderBegin(TotalRenderedFrames + 1, fbWidth, fbHeight);
        long renderStartTs = Stopwatch.GetTimestamp();

        try
        {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Deep dark slate background (#111318)
        _gl.ClearColor(0.067f, 0.075f, 0.094f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        SyncPendingGpuUploads(_gl);

        float aspect = (float)fbWidth / fbHeight;
        Matrix4x4 viewProj = _camera.GetViewMatrix() * _camera.GetProjectionMatrix(aspect);
        Vector3 eyePos = _camera.GetEyePosition();

        // 1. Render reference build-plate grid and XYZ axes
        if (_gridVertexCount > 0)
        {
            _gl.UseProgram(_lineShader);
            _gl.UniformMatrix4(_uLineViewProjLoc, 1, false, (float*)&viewProj);
            _gl.BindVertexArray(_gridVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
        }

        // 1b. Render 3D Model Bounding Box Dimensions Wireframe
        if (_showDimensions && _bboxVertexCount > 0)
        {
            _gl.UseProgram(_lineShader);
            _gl.UniformMatrix4(_uLineViewProjLoc, 1, false, (float*)&viewProj);
            _gl.BindVertexArray(_bboxVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_bboxVertexCount);
        }

        // 2. Render base STL mesh or Voxel mesh
        if (_renderMode == ViewRenderMode.Voxels && _voxelIndexCount > 0)
        {
            _gl.UseProgram(_meshShader);
            _gl.UniformMatrix4(_uViewProjLoc, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uEyePosLoc, eyePos.X, eyePos.Y, eyePos.Z);
            _gl.Uniform4(_uOverrideColorLoc, 0.42f, 0.48f, 0.58f, 1.0f);
            _gl.Uniform1(_uUseVertexColorLoc, 0);

            _gl.BindVertexArray(_voxelVao);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _voxelEbo);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_voxelIndexCount, DrawElementsType.UnsignedInt, null);
        }
        else if (_renderMode == ViewRenderMode.OriginalStl && _meshIndexCount > 0)
        {
            _gl.UseProgram(_meshShader);
            _gl.UniformMatrix4(_uViewProjLoc, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uEyePosLoc, eyePos.X, eyePos.Y, eyePos.Z);
            _gl.Uniform4(_uOverrideColorLoc, 0f, 0f, 0f, 0f);
            _gl.Uniform1(_uUseVertexColorLoc, _useVertexColors ? 1 : 0);

            _gl.BindVertexArray(_meshVao);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshEbo);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_meshIndexCount, DrawElementsType.UnsignedInt, null);

            // 3. Render selection overlays with polygon offset
            _gl.Enable(EnableCap.PolygonOffsetFill);
            _gl.PolygonOffset(-1.0f, -1.0f);

            foreach (OverlayBuffer overlay in _overlays.Values)
            {
                if (overlay.IndexCount > 0 && overlay.Ebo != 0)
                {
                    Vector4 c = overlay.Color;
                    _gl.Uniform4(_uOverrideColorLoc, c.X, c.Y, c.Z, c.W);
                    _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, overlay.Ebo);
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)overlay.IndexCount, DrawElementsType.UnsignedInt, null);
                }
            }

            // 4. Render hover preview overlay
            if (_hoverIndexCount > 0)
            {
                _gl.PolygonOffset(-2.0f, -2.0f);
                _gl.Uniform4(_uOverrideColorLoc, _hoverColor.X, _hoverColor.Y, _hoverColor.Z, _hoverColor.W);
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _hoverEbo);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)_hoverIndexCount, DrawElementsType.UnsignedInt, null);
            }

            _gl.Disable(EnableCap.PolygonOffsetFill);
        }

        // 5. Render patch points overlay
        if (_patchPointsCount > 0)
        {
            _gl.Disable(EnableCap.DepthTest);
            _gl.UseProgram(_lineShader);
            _gl.UniformMatrix4(_uLineViewProjLoc, 1, false, (float*)&viewProj);
            _gl.BindVertexArray(_patchPointsVao);
            _gl.PointSize(8.0f);
            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_patchPointsCount);
            _gl.Enable(EnableCap.DepthTest);
        }

        _gl.BindVertexArray(0);

        // 6. Render 3D boundary condition glyphs (Resultant force arrows with 2-pass occlusion ghosting + distributed arrows & support anchors)
        if (_resultantArrowVertexCount > 0)
        {
            _gl.UseProgram(_meshShader);
            _gl.UniformMatrix4(_uViewProjLoc, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uEyePosLoc, eyePos.X, eyePos.Y, eyePos.Z);

            // Pass 1: Occluded Ghost Arrow (Greater depth, semi-transparent ghost visible through model)
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthFunc(DepthFunction.Greater);
            _gl.DepthMask(false);
            _gl.Uniform4(_uOverrideColorLoc, 0.976f, 0.451f, 0.086f, 0.30f);
            _gl.Uniform1(_uUseVertexColorLoc, 0);

            _gl.BindVertexArray(_resultantArrowVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_resultantArrowVertexCount);

            // Pass 2: Foreground Visible Arrow (Lequal depth, solid full color)
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(true);
            _gl.Uniform4(_uOverrideColorLoc, 0f, 0f, 0f, 0f);
            _gl.Uniform1(_uUseVertexColorLoc, 1);

            _gl.BindVertexArray(_resultantArrowVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_resultantArrowVertexCount);
        }

        if (_glyphVertexCount > 0)
        {
            _gl.UseProgram(_meshShader);
            _gl.UniformMatrix4(_uViewProjLoc, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uEyePosLoc, eyePos.X, eyePos.Y, eyePos.Z);
            _gl.Uniform4(_uOverrideColorLoc, 0f, 0f, 0f, 0f);
            _gl.Uniform1(_uUseVertexColorLoc, 1);

            _gl.BindVertexArray(_glyphVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_glyphVertexCount);
        }

        // 7. Render translucent ghost original mesh if enabled
        if (_showGhostOriginal && _ghostIndexCount > 0)
        {
            _gl.UseProgram(_meshShader);
            _gl.UniformMatrix4(_uViewProjLoc, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uEyePosLoc, eyePos.X, eyePos.Y, eyePos.Z);
            _gl.Uniform4(_uOverrideColorLoc, 0.35f, 0.65f, 0.95f, 0.18f);
            _gl.Uniform1(_uUseVertexColorLoc, 0);

            _gl.DepthMask(false);
            _gl.BindVertexArray(_ghostVao);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ghostEbo);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_ghostIndexCount, DrawElementsType.UnsignedInt, null);
            _gl.DepthMask(true);
            _gl.BindVertexArray(0);
        }

        _gl.UseProgram(0);

        UpdateFpsCounter();
        }
        catch (Exception ex)
        {
            OrbitDiagnostics.RecordRenderException(ex);
            throw;
        }
        finally
        {
            double durationMs = (Stopwatch.GetTimestamp() - renderStartTs) * 1000.0 / Stopwatch.Frequency;
            OrbitDiagnostics.RecordRenderEnd(TotalRenderedFrames, durationMs);
        }
    }

    private unsafe void SyncPendingGpuUploads(GL gl)
    {
        TaskCompletionSource? completedTcs = null;

        lock (_syncLock)
        {
            if (_clearAllOverlaysPending)
            {
                foreach (OverlayBuffer buf in _overlays.Values)
                {
                    if (buf.Ebo != 0) gl.DeleteBuffer(buf.Ebo);
                }
                _overlays.Clear();
                _clearAllOverlaysPending = false;
            }

            if (_overlaysToRemove.Count > 0)
            {
                foreach (Guid id in _overlaysToRemove)
                {
                    if (_overlays.Remove(id, out OverlayBuffer? buf) && buf.Ebo != 0)
                    {
                        gl.DeleteBuffer(buf.Ebo);
                    }
                }
                _overlaysToRemove.Clear();
            }

            if (_meshUploadPending)
            {
                _meshUploadPending = false;
                _vertexColorUpdatePending = false;
                completedTcs = _pendingMeshTcs;
                _pendingMeshTcs = null;

                if (_pendingMesh is not null && _stagedVertices is not null && _pendingMesh.TriangleCount > 0)
                {
                    ReadOnlySpan<GpuVertex> vSpan = _stagedVertices;
                    ReadOnlySpan<int> iSpan = _pendingMesh.Indices.Span;

                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _meshVbo);
                    fixed (GpuVertex* vPtr = vSpan)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vSpan.Length * sizeof(GpuVertex)), vPtr, BufferUsageARB.DynamicDraw);
                    }

                    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _meshEbo);
                    fixed (int* iPtr = iSpan)
                    {
                        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iSpan.Length * sizeof(int)), iPtr, BufferUsageARB.StaticDraw);
                    }

                    _meshIndexCount = iSpan.Length;
                    BuildReferenceGrid(gl, _pendingMesh.BBox.Min, _pendingMesh.BBox.Max);
                    BuildBoundingBoxLines(gl, _pendingMesh.BBox.Min, _pendingMesh.BBox.Max);
                }
                else
                {
                    _meshIndexCount = 0;
                    _hoverIndexCount = 0;
                    _bboxVertexCount = 0;
                }
            }
            else if (_vertexColorUpdatePending && _stagedVertices is not null)
            {
                _vertexColorUpdatePending = false;
                ReadOnlySpan<GpuVertex> vSpan = _stagedVertices;
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, _meshVbo);
                fixed (GpuVertex* vPtr = vSpan)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vSpan.Length * sizeof(GpuVertex)), vPtr, BufferUsageARB.DynamicDraw);
                }
            }

            if (_currentMesh is not null)
            {
                ReadOnlySpan<int> meshIndices = _currentMesh.Indices.Span;
                int totalTris = _currentMesh.TriangleCount;

                foreach (OverlayBuffer overlay in _overlays.Values)
                {
                    if (!overlay.Dirty) continue;
                    overlay.Dirty = false;

                    int[]? tris = overlay.PendingTriangles;
                    if (tris is null || tris.Length == 0)
                    {
                        overlay.IndexCount = 0;
                        continue;
                    }

                    if (overlay.Ebo == 0)
                    {
                        overlay.Ebo = gl.GenBuffer();
                    }

                    int[] expanded = ExpandTriangleIndices(meshIndices, totalTris, tris);
                    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, overlay.Ebo);
                    fixed (int* ptr = expanded)
                    {
                        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(expanded.Length * sizeof(int)), ptr, BufferUsageARB.DynamicDraw);
                    }
                    overlay.IndexCount = expanded.Length;
                }

                if (_hoverUploadPending)
                {
                    _hoverUploadPending = false;
                    if (_pendingHoverTriangles is null || _pendingHoverTriangles.Length == 0)
                    {
                        _hoverIndexCount = 0;
                    }
                    else
                    {
                        int[] expanded = ExpandTriangleIndices(meshIndices, totalTris, _pendingHoverTriangles);
                        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _hoverEbo);
                        fixed (int* ptr = expanded)
                        {
                            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(expanded.Length * sizeof(int)), ptr, BufferUsageARB.DynamicDraw);
                        }
                        _hoverIndexCount = expanded.Length;
                    }
                }
            }

            if (_voxelMeshUploadPending)
            {
                _voxelMeshUploadPending = false;
                if (_pendingVoxelMesh is not null && _pendingVoxelMesh.TriangleCount > 0)
                {
                    if (_voxelVao == 0)
                    {
                        _voxelVao = gl.GenVertexArray();
                        _voxelVbo = gl.GenBuffer();
                        _voxelEbo = gl.GenBuffer();
                    }

                    GpuVertex[] vList = BuildGpuVertices(_pendingVoxelMesh);
                    gl.BindVertexArray(_voxelVao);

                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _voxelVbo);
                    fixed (GpuVertex* vPtr = vList)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vList.Length * sizeof(GpuVertex)), vPtr, BufferUsageARB.StaticDraw);
                    }

                    int[] indArr = _pendingVoxelMesh.Indices.ToArray();
                    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _voxelEbo);
                    fixed (int* iPtr = indArr)
                    {
                        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indArr.Length * sizeof(int)), iPtr, BufferUsageARB.StaticDraw);
                    }

                    uint stride = (uint)sizeof(GpuVertex);
                    gl.EnableVertexAttribArray(0);
                    gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
                    gl.EnableVertexAttribArray(1);
                    gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
                    gl.EnableVertexAttribArray(2);
                    gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);

                    gl.BindVertexArray(0);
                    _voxelIndexCount = indArr.Length;
                }
                else
                {
                    _voxelIndexCount = 0;
                }
            }

            if (_patchPointsUploadPending)
            {
                _patchPointsUploadPending = false;
                if (_pendingPatchPoints is not null && _pendingPatchPoints.Length > 0)
                {
                    if (_patchPointsVao == 0)
                    {
                        _patchPointsVao = gl.GenVertexArray();
                        _patchPointsVbo = gl.GenBuffer();
                    }

                    gl.BindVertexArray(_patchPointsVao);
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _patchPointsVbo);
                    fixed (GpuVertex* pPtr = _pendingPatchPoints)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_pendingPatchPoints.Length * sizeof(GpuVertex)), pPtr, BufferUsageARB.DynamicDraw);
                    }

                    uint stride = (uint)sizeof(GpuVertex);
                    gl.EnableVertexAttribArray(0);
                    gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
                    gl.EnableVertexAttribArray(2);
                    gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);

                    gl.BindVertexArray(0);
                    _patchPointsCount = _pendingPatchPoints.Length;
                }
                else
                {
                    _patchPointsCount = 0;
                }
            }

            if (_glyphUploadPending)
            {
                _glyphUploadPending = false;
                if (_pendingGlyphVertices is not null && _pendingGlyphVertices.Length > 0)
                {
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _glyphVbo);
                    fixed (GpuVertex* gPtr = _pendingGlyphVertices)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_pendingGlyphVertices.Length * sizeof(GpuVertex)), gPtr, BufferUsageARB.DynamicDraw);
                    }
                    _glyphVertexCount = _pendingGlyphVertices.Length;
                }
                else
                {
                    _glyphVertexCount = 0;
                }
            }

            if (_resultantArrowUploadPending)
            {
                _resultantArrowUploadPending = false;
                if (_pendingResultantArrowVertices is not null && _pendingResultantArrowVertices.Length > 0)
                {
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _resultantArrowVbo);
                    fixed (GpuVertex* gPtr = _pendingResultantArrowVertices)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_pendingResultantArrowVertices.Length * sizeof(GpuVertex)), gPtr, BufferUsageARB.DynamicDraw);
                    }
                    _resultantArrowVertexCount = _pendingResultantArrowVertices.Length;
                }
                else
                {
                    _resultantArrowVertexCount = 0;
                }
            }

            if (_ghostMeshUploadPending)
            {
                _ghostMeshUploadPending = false;
                if (_pendingGhostMesh is not null && _pendingGhostMesh.TriangleCount > 0)
                {
                    if (_ghostVao == 0)
                    {
                        _ghostVao = gl.GenVertexArray();
                        _ghostVbo = gl.GenBuffer();
                        _ghostEbo = gl.GenBuffer();
                    }

                    GpuVertex[] vList = BuildGpuVertices(_pendingGhostMesh);
                    gl.BindVertexArray(_ghostVao);

                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _ghostVbo);
                    fixed (GpuVertex* vPtr = vList)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vList.Length * sizeof(GpuVertex)), vPtr, BufferUsageARB.StaticDraw);
                    }

                    int[] indArr = _pendingGhostMesh.Indices.ToArray();
                    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ghostEbo);
                    fixed (int* iPtr = indArr)
                    {
                        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indArr.Length * sizeof(int)), iPtr, BufferUsageARB.StaticDraw);
                    }

                    uint stride = (uint)sizeof(GpuVertex);
                    gl.EnableVertexAttribArray(0);
                    gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
                    gl.EnableVertexAttribArray(1);
                    gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
                    gl.EnableVertexAttribArray(2);
                    gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);

                    gl.BindVertexArray(0);
                    _ghostIndexCount = indArr.Length;
                }
                else
                {
                    _ghostIndexCount = 0;
                }
            }
        }

        completedTcs?.TrySetResult();
    }

    private static int[] ExpandTriangleIndices(ReadOnlySpan<int> meshIndices, int totalTris, ReadOnlySpan<int> selectedTriangles)
    {
        int count = 0;
        for (int i = 0; i < selectedTriangles.Length; i++)
        {
            int t = selectedTriangles[i];
            if ((uint)t < (uint)totalTris) count++;
        }

        int[] expanded = new int[count * 3];
        int outIdx = 0;
        for (int i = 0; i < selectedTriangles.Length; i++)
        {
            int t = selectedTriangles[i];
            if ((uint)t < (uint)totalTris)
            {
                expanded[outIdx++] = meshIndices[t * 3];
                expanded[outIdx++] = meshIndices[t * 3 + 1];
                expanded[outIdx++] = meshIndices[t * 3 + 2];
            }
        }
        return expanded;
    }

    private unsafe void BuildReferenceGrid(GL gl, Vector3 bboxMin, Vector3 bboxMax)
    {
        Vector3 ext = bboxMax - bboxMin;
        float maxSpan = MathF.Max(20f, MathF.Max(ext.X, ext.Y) * 1.6f);
        float rawStep = maxSpan / 12f;
        float step = MathF.Pow(10f, MathF.Floor(MathF.Log10(rawStep)));
        if (rawStep / step >= 5f) step *= 5f;
        else if (rawStep / step >= 2f) step *= 2f;

        int halfLines = Math.Clamp((int)MathF.Ceiling(maxSpan * 0.5f / step), 5, 25);
        float extent = halfLines * step;
        Vector2 center = new((bboxMin.X + bboxMax.X) * 0.5f, (bboxMin.Y + bboxMax.Y) * 0.5f);
        float zPlane = bboxMin.Z;

        List<GpuVertex> lines = new((halfLines * 4 + 6) * 2);
        Vector3 minorCol = new(0.18f, 0.20f, 0.25f);
        Vector3 majorCol = new(0.26f, 0.29f, 0.36f);

        for (int i = -halfLines; i <= halfLines; i++)
        {
            Vector3 col = (i % 5 == 0) ? majorCol : minorCol;
            float offset = i * step;

            lines.Add(new GpuVertex { Position = new Vector3(center.X - extent, center.Y + offset, zPlane), Color = col });
            lines.Add(new GpuVertex { Position = new Vector3(center.X + extent, center.Y + offset, zPlane), Color = col });

            lines.Add(new GpuVertex { Position = new Vector3(center.X + offset, center.Y - extent, zPlane), Color = col });
            lines.Add(new GpuVertex { Position = new Vector3(center.X + offset, center.Y + extent, zPlane), Color = col });
        }

        float axisLen = step * 3f;
        // X Axis (Red), Y Axis (Green), Z Axis (Blue) at origin or bbox corner
        Vector3 origin = new(center.X - extent, center.Y - extent, zPlane);
        lines.Add(new GpuVertex { Position = origin, Color = new Vector3(0.94f, 0.27f, 0.27f) });
        lines.Add(new GpuVertex { Position = origin + new Vector3(axisLen, 0f, 0f), Color = new Vector3(0.94f, 0.27f, 0.27f) });

        lines.Add(new GpuVertex { Position = origin, Color = new Vector3(0.13f, 0.77f, 0.37f) });
        lines.Add(new GpuVertex { Position = origin + new Vector3(0f, axisLen, 0f), Color = new Vector3(0.13f, 0.77f, 0.37f) });

        lines.Add(new GpuVertex { Position = origin, Color = new Vector3(0.23f, 0.51f, 0.96f) });
        lines.Add(new GpuVertex { Position = origin + new Vector3(0f, 0f, axisLen), Color = new Vector3(0.23f, 0.51f, 0.96f) });

        if (_gridVao == 0)
        {
            _gridVao = gl.GenVertexArray();
            _gridVbo = gl.GenBuffer();
        }

        GpuVertex[] arr = lines.ToArray();
        _gridVertexCount = arr.Length;

        gl.BindVertexArray(_gridVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        fixed (GpuVertex* ptr = arr)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(GpuVertex)), ptr, BufferUsageARB.StaticDraw);
        }

        uint stride = (uint)sizeof(GpuVertex);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);
        gl.BindVertexArray(0);
    }

    private unsafe void BuildBoundingBoxLines(GL gl, Vector3 min, Vector3 max)
    {
        Vector3 span = max - min;
        if (span.X <= 0f || span.Y <= 0f || span.Z <= 0f)
        {
            _bboxVertexCount = 0;
            return;
        }

        Vector3 p000 = new(min.X, min.Y, min.Z);
        Vector3 p100 = new(max.X, min.Y, min.Z);
        Vector3 p110 = new(max.X, max.Y, min.Z);
        Vector3 p010 = new(min.X, max.Y, min.Z);
        Vector3 p001 = new(min.X, min.Y, max.Z);
        Vector3 p101 = new(max.X, min.Y, max.Z);
        Vector3 p111 = new(max.X, max.Y, max.Z);
        Vector3 p011 = new(min.X, max.Y, max.Z);

        Vector3 colX = new(0.94f, 0.27f, 0.27f); // Bright Red (#EF4444)
        Vector3 colY = new(0.36f, 0.82f, 0.55f); // Bright Green (#5BD18B)
        Vector3 colZ = new(0.15f, 0.39f, 0.92f); // Bright Blue (#2563EB)
        Vector3 colBox = new(0.35f, 0.40f, 0.50f); // Subtle slate gray

        List<GpuVertex> lines = new(64);

        // Primary dimension edges:
        // X edge (Red)
        lines.Add(new GpuVertex { Position = p000, Color = colX });
        lines.Add(new GpuVertex { Position = p100, Color = colX });

        // Y edge (Green)
        lines.Add(new GpuVertex { Position = p000, Color = colY });
        lines.Add(new GpuVertex { Position = p010, Color = colY });

        // Z edge (Blue)
        lines.Add(new GpuVertex { Position = p000, Color = colZ });
        lines.Add(new GpuVertex { Position = p001, Color = colZ });

        // Remaining 9 bounding box edges:
        // X-parallel
        lines.Add(new GpuVertex { Position = p010, Color = colBox });
        lines.Add(new GpuVertex { Position = p110, Color = colBox });

        lines.Add(new GpuVertex { Position = p001, Color = colBox });
        lines.Add(new GpuVertex { Position = p101, Color = colBox });

        lines.Add(new GpuVertex { Position = p011, Color = colBox });
        lines.Add(new GpuVertex { Position = p111, Color = colBox });

        // Y-parallel
        lines.Add(new GpuVertex { Position = p100, Color = colBox });
        lines.Add(new GpuVertex { Position = p110, Color = colBox });

        lines.Add(new GpuVertex { Position = p001, Color = colBox });
        lines.Add(new GpuVertex { Position = p011, Color = colBox });

        lines.Add(new GpuVertex { Position = p101, Color = colBox });
        lines.Add(new GpuVertex { Position = p111, Color = colBox });

        // Z-parallel
        lines.Add(new GpuVertex { Position = p100, Color = colBox });
        lines.Add(new GpuVertex { Position = p101, Color = colBox });

        lines.Add(new GpuVertex { Position = p110, Color = colBox });
        lines.Add(new GpuVertex { Position = p111, Color = colBox });

        lines.Add(new GpuVertex { Position = p010, Color = colBox });
        lines.Add(new GpuVertex { Position = p011, Color = colBox });

        // Dimension tick marks at ends of primary edges:
        float tickLen = Math.Clamp(MathF.Min(span.X, MathF.Min(span.Y, span.Z)) * 0.05f, 1.5f, 8.0f);

        // X axis ticks pointing in -Y
        lines.Add(new GpuVertex { Position = p000, Color = colX });
        lines.Add(new GpuVertex { Position = p000 - new Vector3(0f, tickLen, 0f), Color = colX });
        lines.Add(new GpuVertex { Position = p100, Color = colX });
        lines.Add(new GpuVertex { Position = p100 - new Vector3(0f, tickLen, 0f), Color = colX });

        // Y axis ticks pointing in -X
        lines.Add(new GpuVertex { Position = p000, Color = colY });
        lines.Add(new GpuVertex { Position = p000 - new Vector3(tickLen, 0f, 0f), Color = colY });
        lines.Add(new GpuVertex { Position = p010, Color = colY });
        lines.Add(new GpuVertex { Position = p010 - new Vector3(tickLen, 0f, 0f), Color = colY });

        // Z axis ticks pointing in -X
        lines.Add(new GpuVertex { Position = p000, Color = colZ });
        lines.Add(new GpuVertex { Position = p000 - new Vector3(tickLen, 0f, 0f), Color = colZ });
        lines.Add(new GpuVertex { Position = p001, Color = colZ });
        lines.Add(new GpuVertex { Position = p001 - new Vector3(tickLen, 0f, 0f), Color = colZ });

        if (_bboxVao == 0)
        {
            _bboxVao = gl.GenVertexArray();
            _bboxVbo = gl.GenBuffer();
        }

        GpuVertex[] arr = lines.ToArray();
        _bboxVertexCount = arr.Length;

        gl.BindVertexArray(_bboxVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _bboxVbo);
        fixed (GpuVertex* ptr = arr)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(GpuVertex)), ptr, BufferUsageARB.StaticDraw);
        }

        uint stride = (uint)sizeof(GpuVertex);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)24);
        gl.BindVertexArray(0);
    }

    private void UpdateFpsCounter()
    {
        TotalRenderedFrames++;
        _framesInSecond++;
        double elapsed = _fpsWatch.Elapsed.TotalSeconds;
        if (elapsed >= 0.5)
        {
            CurrentFps = _framesInSecond / elapsed;
            _framesInSecond = 0;
            _fpsWatch.Restart();
            double fps = CurrentFps;
            string profile = GlProfileSummary;
            Dispatcher.UIThread.Post(() => DiagnosticsUpdated?.Invoke(fps, profile));
        }

        RenderLoopProbe.Instance.OnRenderExecuted(CurrentFps);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is not null)
        {
            lock (_syncLock)
            {
                foreach (OverlayBuffer buf in _overlays.Values)
                {
                    if (buf.Ebo != 0) _gl.DeleteBuffer(buf.Ebo);
                }
                _overlays.Clear();
            }

            if (_glyphVbo != 0) _gl.DeleteBuffer(_glyphVbo);
            if (_glyphVao != 0) _gl.DeleteVertexArray(_glyphVao);
            if (_resultantArrowVbo != 0) _gl.DeleteBuffer(_resultantArrowVbo);
            if (_resultantArrowVao != 0) _gl.DeleteVertexArray(_resultantArrowVao);
            if (_ghostEbo != 0) _gl.DeleteBuffer(_ghostEbo);
            if (_ghostVbo != 0) _gl.DeleteBuffer(_ghostVbo);
            if (_ghostVao != 0) _gl.DeleteVertexArray(_ghostVao);
            if (_voxelEbo != 0) _gl.DeleteBuffer(_voxelEbo);
            if (_voxelVbo != 0) _gl.DeleteBuffer(_voxelVbo);
            if (_voxelVao != 0) _gl.DeleteVertexArray(_voxelVao);
            if (_patchPointsVbo != 0) _gl.DeleteBuffer(_patchPointsVbo);
            if (_patchPointsVao != 0) _gl.DeleteVertexArray(_patchPointsVao);
            if (_hoverEbo != 0) _gl.DeleteBuffer(_hoverEbo);
            if (_meshEbo != 0) _gl.DeleteBuffer(_meshEbo);
            if (_meshVbo != 0) _gl.DeleteBuffer(_meshVbo);
            if (_meshVao != 0) _gl.DeleteVertexArray(_meshVao);
            if (_gridVbo != 0) _gl.DeleteBuffer(_gridVbo);
            if (_gridVao != 0) _gl.DeleteVertexArray(_gridVao);
            if (_bboxVbo != 0) _gl.DeleteBuffer(_bboxVbo);
            if (_bboxVao != 0) _gl.DeleteVertexArray(_bboxVao);
            _bboxVao = 0;
            _bboxVbo = 0;
            _bboxVertexCount = 0;
            if (_meshShader != 0) _gl.DeleteProgram(_meshShader);
            if (_lineShader != 0) _gl.DeleteProgram(_lineShader);

            _gl.Dispose();
            _gl = null;
        }

        _glInitialized = false;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPoint point = e.GetCurrentPoint(this);
        _lastPointerPos = point.Position;
        _pressPointerPos = point.Position;
        _pointerMovedSignificantly = false;

        if (point.Properties.IsRightButtonPressed ||
            (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            _isOrbiting = true;
            e.Pointer.Capture(this);
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            e.Pointer.Capture(this);
        }

        OrbitDiagnostics.RecordPointerPressed(point, e.KeyModifiers, _isOrbiting, _isPanning);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point pos = e.GetPosition(this);
        Avalonia.Vector delta = pos - _lastPointerPos;
        _lastPointerPos = pos;

        Avalonia.Vector moveFromPress = pos - _pressPointerPos;
        if (moveFromPress.Length > 4.0)
        {
            _pointerMovedSignificantly = true;
        }

        OrbitDiagnostics.RecordPointerMoved(pos, delta, _isOrbiting, _isPanning);

        if (_isOrbiting)
        {
            _camera.Orbit((float)delta.X, (float)delta.Y);
            OrbitDiagnostics.RecordRenderRequested("OnPointerMoved:Orbit");
            RequestNextFrameRendering();
        }
        else if (_isPanning)
        {
            _camera.Pan((float)delta.X, (float)delta.Y, (float)Bounds.Height);
            OrbitDiagnostics.RecordRenderRequested("OnPointerMoved:Pan");
            RequestNextFrameRendering();
        }
        else
        {
            ViewportPointerMoved?.Invoke(new Vector2((float)pos.X, (float)pos.Y));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Point pos = e.GetPosition(this);

        OrbitDiagnostics.RecordPointerReleased(e.InitialPressMouseButton, _isOrbiting, _isPanning);

        if (_isOrbiting || _isPanning)
        {
            _isOrbiting = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left && !_pointerMovedSignificantly)
        {
            ViewportPointerClicked?.Invoke(new Vector2((float)pos.X, (float)pos.Y), e.KeyModifiers);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Point pos = e.GetPosition(this);
        OrbitDiagnostics.RecordPointerWheel(pos, e.Delta.Y);
        _camera.ZoomToCursor(
            (float)e.Delta.Y,
            new Vector2((float)pos.X, (float)pos.Y),
            (float)Bounds.Width,
            (float)Bounds.Height);
        OrbitDiagnostics.RecordRenderRequested("OnPointerWheelChanged:Zoom");
        RequestNextFrameRendering();
    }

    public void DiagnosticSimulatePointerDrag(MouseButton button, KeyModifiers modifiers, Point start, Point end)
    {
        OrbitDiagnostics.Log($"--- Simulating Drag: Button={button}, Modifiers={modifiers}, Start={start}, End={end} ---");
        _lastPointerPos = start;
        _pressPointerPos = start;
        _pointerMovedSignificantly = false;

        if (button == MouseButton.Right || (button == MouseButton.Left && modifiers.HasFlag(KeyModifiers.Alt)))
        {
            _isOrbiting = true;
        }
        else if (button == MouseButton.Middle)
        {
            _isPanning = true;
        }

        OrbitDiagnostics.Log($"[SimulatePress] Resulting state: _isOrbiting={_isOrbiting}, _isPanning={_isPanning}");

        Point pos = end;
        Avalonia.Vector delta = pos - _lastPointerPos;
        _lastPointerPos = pos;
        _pointerMovedSignificantly = true;

        OrbitDiagnostics.RecordPointerMoved(pos, delta, _isOrbiting, _isPanning);

        if (_isOrbiting)
        {
            _camera.Orbit((float)delta.X, (float)delta.Y);
            OrbitDiagnostics.RecordRenderRequested("SimulatePointerDrag:Orbit");
            RequestNextFrameRendering();
        }
        else if (_isPanning)
        {
            _camera.Pan((float)delta.X, (float)delta.Y, (float)Bounds.Height);
            OrbitDiagnostics.RecordRenderRequested("SimulatePointerDrag:Pan");
            RequestNextFrameRendering();
        }

        OrbitDiagnostics.RecordPointerReleased(button, _isOrbiting, _isPanning);
        _isOrbiting = false;
        _isPanning = false;
    }

    private static uint CompileProgram(GL gl, string vertexSrc, string fragmentSrc)
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSrc);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSrc);

        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.LinkProgram(prog);

        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(prog);
            throw new InvalidOperationException($"OpenGL shader link error: {log}");
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }

    private static uint CompileShader(GL gl, ShaderType type, string src)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, src);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"OpenGL {type} compile error: {log}");
        }
        return shader;
    }

    private const string MeshVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;

        uniform mat4 uViewProj;

        out vec3 vWorldPos;
        out vec3 vSmoothNormal;
        out vec3 vColor;

        void main()
        {
            vWorldPos = aPos;
            vSmoothNormal = aNormal;
            vColor = aColor;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string MeshFragmentShaderBody = """
        in vec3 vWorldPos;
        in vec3 vSmoothNormal;
        in vec3 vColor;

        uniform vec3 uEyePos;
        uniform vec4 uOverrideColor;
        uniform int uUseVertexColor;

        out vec4 FragColor;

        void main()
        {
            vec3 dx = dFdx(vWorldPos);
            vec3 dy = dFdy(vWorldPos);
            vec3 flatN = cross(dx, dy);
            float flatLen = length(flatN);
            vec3 sN = normalize(vSmoothNormal);
            flatN = (flatLen > 1e-12) ? (flatN / flatLen) : sN;
            if (dot(flatN, sN) < 0.0) flatN = -flatN;

            // Smooth shading across gentle curvature (< 32 deg), crisp facets across sharp CAD creases
            vec3 N = (dot(flatN, sN) > 0.848) ? sN : flatN;

            vec3 V = normalize(uEyePos - vWorldPos);
            if (dot(N, V) < 0.0) N = -N;

            vec3 L1 = V;
            vec3 L2 = normalize(V + vec3(0.35, -0.25, 0.65));
            float diff1 = max(dot(N, L1), 0.0) * 0.48;
            float diff2 = max(dot(N, L2), 0.0) * 0.34;
            float hemi = 0.22 + 0.12 * (0.5 * N.z + 0.5);

            vec3 H = normalize(L2 + V);
            float spec = pow(max(dot(N, H), 0.0), 36.0) * 0.16;

            vec3 baseCol = (uOverrideColor.a > 0.0)
                ? uOverrideColor.rgb
                : ((uUseVertexColor != 0) ? vColor : vec3(0.70, 0.75, 0.82));

            vec3 lit = baseCol * (hemi + diff1 + diff2) + vec3(spec);
            float alpha = (uOverrideColor.a > 0.0) ? uOverrideColor.a : 1.0;
            FragColor = vec4(lit, alpha);
        }
        """;

    private const string LineVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 2) in vec3 aColor;

        uniform mat4 uViewProj;

        out vec3 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string LineFragmentShaderBody = """
        in vec3 vColor;
        out vec4 FragColor;

        void main()
        {
            FragColor = vec4(vColor, 0.75);
        }
        """;
}

