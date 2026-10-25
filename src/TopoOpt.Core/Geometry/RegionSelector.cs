using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TopoOpt.Core.Geometry;

/// <summary>
/// Set operation applied when modifying an existing triangle selection.
/// </summary>
public enum SelectionModifyMode
{
    Replace,
    Add,
    Subtract
}

/// <summary>
/// Precomputes triangle-to-triangle edge adjacency and grows surface patches
/// across shared edges whose dihedral angle does not exceed a user threshold.
/// </summary>
public sealed class RegionSelector
{
    private readonly IndexedMesh _mesh;
    private readonly int[] _neighbors; // 3 * TriangleCount, -1 if boundary or non-manifold
    private readonly int[] _visitStamp;
    private readonly int[] _bfsQueue;
    private int _currentStamp;

    public RegionSelector(IndexedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _mesh = mesh;

        int triCount = mesh.TriangleCount;
        _neighbors = new int[triCount * 3];
        Array.Fill(_neighbors, -1);

        _visitStamp = new int[triCount];
        _bfsQueue = new int[triCount];
        _currentStamp = 0;

        if (triCount > 0)
        {
            BuildAdjacency(mesh.Indices.Span, triCount);
        }
    }

    private void BuildAdjacency(ReadOnlySpan<int> indices, int triCount)
    {
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(32, triCount * 3));
        int mask = capacity - 1;

        ulong[] edgeKeys = ArrayPool<ulong>.Shared.Rent(capacity);
        int[] firstHalfEdge = ArrayPool<int>.Shared.Rent(capacity);

        try
        {
            edgeKeys.AsSpan(0, capacity).Fill(ulong.MaxValue);
            firstHalfEdge.AsSpan(0, capacity).Fill(-1);

            for (int t = 0; t < triCount; t++)
            {
                int baseIdx = t * 3;
                int v0 = indices[baseIdx];
                int v1 = indices[baseIdx + 1];
                int v2 = indices[baseIdx + 2];

                RegisterHalfEdge(v0, v1, baseIdx, edgeKeys, firstHalfEdge, mask);
                RegisterHalfEdge(v1, v2, baseIdx + 1, edgeKeys, firstHalfEdge, mask);
                RegisterHalfEdge(v2, v0, baseIdx + 2, edgeKeys, firstHalfEdge, mask);
            }
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(edgeKeys);
            ArrayPool<int>.Shared.Return(firstHalfEdge);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterHalfEdge(
        int u,
        int v,
        int halfEdgeIdx,
        ulong[] edgeKeys,
        int[] firstHalfEdge,
        int mask)
    {
        uint min = u < v ? (uint)u : (uint)v;
        uint max = u < v ? (uint)v : (uint)u;
        ulong key = ((ulong)min << 32) | max;

        int slot = HashKey(key) & mask;
        while (true)
        {
            ulong existing = edgeKeys[slot];
            if (existing == ulong.MaxValue)
            {
                edgeKeys[slot] = key;
                firstHalfEdge[slot] = halfEdgeIdx;
                return;
            }
            if (existing == key)
            {
                int state = firstHalfEdge[slot];
                if (state >= 0)
                {
                    int otherHalfEdge = state;
                    int otherTri = otherHalfEdge / 3;
                    int thisTri = halfEdgeIdx / 3;
                    _neighbors[halfEdgeIdx] = otherTri;
                    _neighbors[otherHalfEdge] = thisTri;
                    // Encode paired firstHalfEdge as -(otherHalfEdge + 2) so a 3rd non-manifold face can sever both
                    firstHalfEdge[slot] = -(otherHalfEdge + 2);
                }
                else if (state <= -2)
                {
                    // Non-manifold edge (>= 3 incident faces): sever adjacency on this edge
                    int firstHe = -(state + 2);
                    int secondTri = _neighbors[firstHe];
                    if (secondTri >= 0)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            if (_neighbors[secondTri * 3 + e] == firstHe / 3)
                            {
                                _neighbors[secondTri * 3 + e] = -1;
                            }
                        }
                    }
                    _neighbors[firstHe] = -1;
                    firstHalfEdge[slot] = int.MinValue;
                }
                return;
            }
            slot = (slot + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashKey(ulong key)
    {
        key ^= key >> 33;
        key *= 0xff51afd7ed558ccdUL;
        key ^= key >> 33;
        return (int)key;
    }

    /// <summary>
    /// Grows a connected surface region starting at <paramref name="seedTriangle"/> while the dihedral
    /// angle between adjacent face normals is at most <paramref name="maxDihedralAngleDegrees"/>.
    /// Returns a sorted array of selected triangle indices.
    /// </summary>
    public int[] GrowRegion(int seedTriangle, float maxDihedralAngleDegrees = 30.0f)
    {
        int triCount = _mesh.TriangleCount;
        if (seedTriangle < 0 || seedTriangle >= triCount)
        {
            return [];
        }

        float cosThreshold = MathF.Cos(Math.Clamp(maxDihedralAngleDegrees, 0f, 180f) * (MathF.PI / 180.0f));
        ReadOnlySpan<Vector3> normals = _mesh.TriangleNormals.Span;

        int stamp = ++_currentStamp;
        if (stamp == int.MaxValue)
        {
            Array.Clear(_visitStamp);
            _currentStamp = 1;
            stamp = 1;
        }

        int head = 0;
        int tail = 0;
        _visitStamp[seedTriangle] = stamp;
        _bfsQueue[tail++] = seedTriangle;

        while (head < tail)
        {
            int curr = _bfsQueue[head++];
            Vector3 nCurr = normals[curr];
            int baseEdge = curr * 3;

            for (int e = 0; e < 3; e++)
            {
                int nb = _neighbors[baseEdge + e];
                if (nb < 0 || _visitStamp[nb] == stamp)
                {
                    continue;
                }

                Vector3 nNb = normals[nb];
                if (Vector3.Dot(nCurr, nNb) >= cosThreshold)
                {
                    _visitStamp[nb] = stamp;
                    _bfsQueue[tail++] = nb;
                }
            }
        }

        int[] result = new int[tail];
        Array.Copy(_bfsQueue, 0, result, 0, tail);
        Array.Sort(result);
        return result;
    }

    /// <summary>
    /// Combines an existing sorted triangle index list with a new sorted delta list using Replace, Add, or Subtract.
    /// </summary>
    public static int[] ModifySelection(
        ReadOnlySpan<int> currentSorted,
        ReadOnlySpan<int> deltaSorted,
        SelectionModifyMode mode)
    {
        if (mode == SelectionModifyMode.Replace)
        {
            return deltaSorted.ToArray();
        }

        if (mode == SelectionModifyMode.Add)
        {
            if (currentSorted.IsEmpty) return deltaSorted.ToArray();
            if (deltaSorted.IsEmpty) return currentSorted.ToArray();

            List<int> union = new(currentSorted.Length + deltaSorted.Length);
            int i = 0, j = 0;
            while (i < currentSorted.Length && j < deltaSorted.Length)
            {
                int a = currentSorted[i];
                int b = deltaSorted[j];
                if (a < b) { union.Add(a); i++; }
                else if (b < a) { union.Add(b); j++; }
                else { union.Add(a); i++; j++; }
            }
            while (i < currentSorted.Length) union.Add(currentSorted[i++]);
            while (j < deltaSorted.Length) union.Add(deltaSorted[j++]);
            return union.ToArray();
        }

        if (mode == SelectionModifyMode.Subtract)
        {
            if (currentSorted.IsEmpty || deltaSorted.IsEmpty)
            {
                return currentSorted.ToArray();
            }

            List<int> diff = new(currentSorted.Length);
            int i = 0, j = 0;
            while (i < currentSorted.Length && j < deltaSorted.Length)
            {
                int a = currentSorted[i];
                int b = deltaSorted[j];
                if (a < b) { diff.Add(a); i++; }
                else if (b < a) { j++; }
                else { i++; j++; }
            }
            while (i < currentSorted.Length) diff.Add(currentSorted[i++]);
            return diff.ToArray();
        }

        return currentSorted.ToArray();
    }
}
