using System;
using System.Numerics;

namespace TopoOpt.Core.Geometry;

/// <summary>
/// Immutable indexed triangle mesh with precomputed face normals, smooth vertex normals,
/// axis-aligned bounding box, surface area, and signed volume.
/// </summary>
public sealed class IndexedMesh
{
    private readonly Vector3[] _positions;
    private readonly int[] _indices;
    private readonly Vector3[] _triangleNormals;
    private readonly Vector3[] _smoothVertexNormals;

    public ReadOnlyMemory<Vector3> Positions => _positions;
    public ReadOnlyMemory<int> Indices => _indices;
    public ReadOnlyMemory<Vector3> TriangleNormals => _triangleNormals;
    public ReadOnlyMemory<Vector3> SmoothVertexNormals => _smoothVertexNormals;

    public (Vector3 Min, Vector3 Max) BBox { get; }
    public double SurfaceArea { get; }
    public double SignedVolume { get; }

    public int VertexCount => _positions.Length;
    public int TriangleCount => _triangleNormals.Length;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void GetTriangle(int triangleIndex, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        int i3 = triangleIndex * 3;
        a = _positions[_indices[i3]];
        b = _positions[_indices[i3 + 1]];
        c = _positions[_indices[i3 + 2]];
    }

    public IndexedMesh(Vector3[] positions, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("Indices length must be a multiple of 3.", nameof(indices));
        }

        _positions = positions;
        _indices = indices;

        int triCount = indices.Length / 3;
        _triangleNormals = new Vector3[triCount];
        _smoothVertexNormals = new Vector3[positions.Length];

        if (positions.Length == 0 || triCount == 0)
        {
            BBox = (Vector3.Zero, Vector3.Zero);
            SurfaceArea = 0.0;
            SignedVolume = 0.0;
            return;
        }

        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        BBox = (min, max);

        double areaSum = 0.0;
        double volumeSum6 = 0.0;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            Vector3 a = positions[i0];
            Vector3 b = positions[i1];
            Vector3 c = positions[i2];

            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 cross = Vector3.Cross(e1, e2);
            float crossLen = cross.Length();

            _triangleNormals[t] = crossLen > 1e-20f ? cross / crossLen : new Vector3(0f, 0f, 1f);
            areaSum += 0.5 * crossLen;

            // Area-weighted vertex normal accumulation
            _smoothVertexNormals[i0] += cross;
            _smoothVertexNormals[i1] += cross;
            _smoothVertexNormals[i2] += cross;

            // Double precision signed tetrahedron volume: a . (b x c)
            double ax = a.X, ay = a.Y, az = a.Z;
            double bx = b.X, by = b.Y, bz = b.Z;
            double cx = c.X, cy = c.Y, cz = c.Z;

            double cxProd = by * cz - bz * cy;
            double cyProd = bz * cx - bx * cz;
            double czProd = bx * cy - by * cx;

            volumeSum6 += ax * cxProd + ay * cyProd + az * czProd;
        }

        for (int i = 0; i < _smoothVertexNormals.Length; i++)
        {
            Vector3 n = _smoothVertexNormals[i];
            float len = n.Length();
            _smoothVertexNormals[i] = len > 1e-20f ? n / len : new Vector3(0f, 0f, 1f);
        }

        SurfaceArea = areaSum;
        SignedVolume = volumeSum6 / 6.0;
    }
}

