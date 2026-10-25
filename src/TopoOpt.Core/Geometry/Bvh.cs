using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TopoOpt.Core.Geometry;

/// <summary>
/// Result of a ray-mesh intersection query.
/// </summary>
public readonly record struct RayHit(
    bool IsHit,
    float T,
    int TriangleIndex,
    Vector3 Point,
    Vector3 Barycentric);

/// <summary>
/// Result of a closest-point-on-mesh query.
/// </summary>
public readonly record struct ClosestPointHit(
    int TriangleIndex,
    Vector3 Point,
    float DistanceSquared);

/// <summary>
/// Flat-array Bounding Volume Hierarchy (BVH) built with Binned SAH.
/// Zero heap allocations per node; supports fast ray casting and closest-point queries.
/// </summary>
public sealed class Bvh
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BvhNode
    {
        public Vector3 Min;
        public int LeftFirst; // If Count > 0: start index in _triIndices; if Count == 0: left child node index (right child = LeftFirst + 1)
        public Vector3 Max;
        public int Count;     // > 0 for leaf, 0 for internal node
    }

    private const int MaxLeafTriangles = 4;
    private const int SahBins = 8;

    private readonly IndexedMesh _mesh;
    private readonly BvhNode[] _nodes;
    private readonly int[] _triIndices;
    private int _nodesUsed;

    public int NodeCount => _nodesUsed;

    public Bvh(IndexedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _mesh = mesh;

        int triCount = mesh.TriangleCount;
        if (triCount == 0)
        {
            _nodes = [];
            _triIndices = [];
            _nodesUsed = 0;
            return;
        }

        _triIndices = new int[triCount];
        for (int i = 0; i < triCount; i++)
        {
            _triIndices[i] = i;
        }

        _nodes = new BvhNode[triCount * 2];

        Vector3[] triMin = ArrayPool<Vector3>.Shared.Rent(triCount);
        Vector3[] triMax = ArrayPool<Vector3>.Shared.Rent(triCount);
        Vector3[] centroids = ArrayPool<Vector3>.Shared.Rent(triCount);

        try
        {
            ReadOnlySpan<Vector3> pos = mesh.Positions.Span;
            ReadOnlySpan<int> indices = mesh.Indices.Span;

            for (int i = 0; i < triCount; i++)
            {
                Vector3 a = pos[indices[i * 3]];
                Vector3 b = pos[indices[i * 3 + 1]];
                Vector3 c = pos[indices[i * 3 + 2]];

                triMin[i] = Vector3.Min(a, Vector3.Min(b, c));
                triMax[i] = Vector3.Max(a, Vector3.Max(b, c));
                centroids[i] = (a + b + c) * (1.0f / 3.0f);
            }

            _nodesUsed = 1;
            _nodes[0].LeftFirst = 0;
            _nodes[0].Count = triCount;
            UpdateNodeBounds(0, triMin, triMax);
            Subdivide(0, triMin, triMax, centroids);
        }
        finally
        {
            ArrayPool<Vector3>.Shared.Return(triMin);
            ArrayPool<Vector3>.Shared.Return(triMax);
            ArrayPool<Vector3>.Shared.Return(centroids);
        }
    }

    private void UpdateNodeBounds(int nodeIdx, Vector3[] triMin, Vector3[] triMax)
    {
        ref BvhNode node = ref _nodes[nodeIdx];
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        int first = node.LeftFirst;
        int end = first + node.Count;
        for (int i = first; i < end; i++)
        {
            int t = _triIndices[i];
            min = Vector3.Min(min, triMin[t]);
            max = Vector3.Max(max, triMax[t]);
        }

        node.Min = min;
        node.Max = max;
    }

    private void Subdivide(int nodeIdx, Vector3[] triMin, Vector3[] triMax, Vector3[] centroids)
    {
        // Iterative explicit stack to avoid deep recursion on degenerate inputs
        Span<int> workStack = stackalloc int[128];
        int stackPtr = 0;
        workStack[stackPtr++] = nodeIdx;

        Span<Vector3> binMin = stackalloc Vector3[SahBins];
        Span<Vector3> binMax = stackalloc Vector3[SahBins];
        Span<int> binCount = stackalloc int[SahBins];
        Span<float> leftArea = stackalloc float[SahBins - 1];
        Span<int> leftSum = stackalloc int[SahBins - 1];

        while (stackPtr > 0)
        {
            int currIdx = workStack[--stackPtr];
            ref BvhNode node = ref _nodes[currIdx];
            int count = node.Count;
            if (count <= MaxLeafTriangles)
            {
                continue;
            }

            int first = node.LeftFirst;
            int end = first + count;

            Vector3 cMin = new(float.PositiveInfinity);
            Vector3 cMax = new(float.NegativeInfinity);
            for (int i = first; i < end; i++)
            {
                Vector3 c = centroids[_triIndices[i]];
                cMin = Vector3.Min(cMin, c);
                cMax = Vector3.Max(cMax, c);
            }

            Vector3 extent = cMax - cMin;
            int axis = 0;
            if (extent.Y > extent.X) axis = 1;
            if (extent.Z > GetAxis(extent, axis)) axis = 2;

            float axisExtent = GetAxis(extent, axis);
            int splitIdx;

            if (axisExtent <= 1e-6f)
            {
                // Centroids coincide; split in half
                splitIdx = first + count / 2;
            }
            else
            {
                // Binned SAH along dominant centroid axis
                for (int b = 0; b < SahBins; b++)
                {
                    binMin[b] = new Vector3(float.PositiveInfinity);
                    binMax[b] = new Vector3(float.NegativeInfinity);
                    binCount[b] = 0;
                }

                float scale = SahBins / axisExtent;
                float axisMin = GetAxis(cMin, axis);

                for (int i = first; i < end; i++)
                {
                    int t = _triIndices[i];
                    int b = Math.Clamp((int)((GetAxis(centroids[t], axis) - axisMin) * scale), 0, SahBins - 1);
                    binCount[b]++;
                    binMin[b] = Vector3.Min(binMin[b], triMin[t]);
                    binMax[b] = Vector3.Max(binMax[b], triMax[t]);
                }

                Vector3 curLeftMin = new(float.PositiveInfinity);
                Vector3 curLeftMax = new(float.NegativeInfinity);
                int curLeftCount = 0;

                for (int b = 0; b < SahBins - 1; b++)
                {
                    curLeftCount += binCount[b];
                    curLeftMin = Vector3.Min(curLeftMin, binMin[b]);
                    curLeftMax = Vector3.Max(curLeftMax, binMax[b]);
                    leftSum[b] = curLeftCount;
                    leftArea[b] = curLeftCount > 0 ? HalfBoxArea(curLeftMin, curLeftMax) : 0f;
                }

                Vector3 curRightMin = new(float.PositiveInfinity);
                Vector3 curRightMax = new(float.NegativeInfinity);
                int curRightCount = 0;

                float bestCost = float.PositiveInfinity;
                int bestBin = -1;

                for (int b = SahBins - 2; b >= 0; b--)
                {
                    curRightCount += binCount[b + 1];
                    curRightMin = Vector3.Min(curRightMin, binMin[b + 1]);
                    curRightMax = Vector3.Max(curRightMax, binMax[b + 1]);

                    if (leftSum[b] > 0 && curRightCount > 0)
                    {
                        float cost = leftSum[b] * leftArea[b] + curRightCount * HalfBoxArea(curRightMin, curRightMax);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestBin = b;
                        }
                    }
                }

                float parentCost = count * HalfBoxArea(node.Min, node.Max);
                if (bestBin < 0 || bestCost >= parentCost && count <= 8)
                {
                    continue;
                }

                float splitPos = axisMin + (bestBin + 1) * (axisExtent / SahBins);
                int left = first;
                int right = end - 1;
                while (left <= right)
                {
                    if (GetAxis(centroids[_triIndices[left]], axis) < splitPos)
                    {
                        left++;
                    }
                    else
                    {
                        (_triIndices[left], _triIndices[right]) = (_triIndices[right], _triIndices[left]);
                        right--;
                    }
                }

                splitIdx = left;
                if (splitIdx == first || splitIdx == end)
                {
                    splitIdx = first + count / 2;
                }
            }

            int leftCount = splitIdx - first;
            int rightCount = count - leftCount;

            int leftChildIdx = _nodesUsed;
            int rightChildIdx = _nodesUsed + 1;
            _nodesUsed += 2;

            _nodes[leftChildIdx].LeftFirst = first;
            _nodes[leftChildIdx].Count = leftCount;
            UpdateNodeBounds(leftChildIdx, triMin, triMax);

            _nodes[rightChildIdx].LeftFirst = splitIdx;
            _nodes[rightChildIdx].Count = rightCount;
            UpdateNodeBounds(rightChildIdx, triMin, triMax);

            node.LeftFirst = leftChildIdx;
            node.Count = 0;

            if (stackPtr + 2 <= workStack.Length)
            {
                workStack[stackPtr++] = rightChildIdx;
                workStack[stackPtr++] = leftChildIdx;
            }
        }
    }

    public RayHit RayCast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity)
    {
        if (_nodesUsed == 0)
        {
            return default;
        }

        Vector3 invDir = new(
            MathF.Abs(direction.X) > 1e-12f ? 1.0f / direction.X : MathF.CopySign(1e12f, direction.X),
            MathF.Abs(direction.Y) > 1e-12f ? 1.0f / direction.Y : MathF.CopySign(1e12f, direction.Y),
            MathF.Abs(direction.Z) > 1e-12f ? 1.0f / direction.Z : MathF.CopySign(1e12f, direction.Z));

        if (!IntersectRayAabb(origin, invDir, _nodes[0].Min, _nodes[0].Max, maxDistance, out _))
        {
            return default;
        }

        ReadOnlySpan<Vector3> positions = _mesh.Positions.Span;
        ReadOnlySpan<int> indices = _mesh.Indices.Span;

        float bestT = maxDistance;
        int bestTri = -1;
        Vector3 bestBary = default;

        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;

        while (stackPtr > 0)
        {
            int nodeIdx = stack[--stackPtr];
            ref readonly BvhNode node = ref _nodes[nodeIdx];

            if (node.Count > 0)
            {
                int first = node.LeftFirst;
                int end = first + node.Count;
                for (int i = first; i < end; i++)
                {
                    int triIdx = _triIndices[i];
                    Vector3 a = positions[indices[triIdx * 3]];
                    Vector3 b = positions[indices[triIdx * 3 + 1]];
                    Vector3 c = positions[indices[triIdx * 3 + 2]];

                    if (IntersectRayTriangle(origin, direction, a, b, c, out float t, out float u, out float v) && t < bestT)
                    {
                        bestT = t;
                        bestTri = triIdx;
                        bestBary = new Vector3(1.0f - u - v, u, v);
                    }
                }
            }
            else
            {
                int c1 = node.LeftFirst;
                int c2 = c1 + 1;

                bool hit1 = IntersectRayAabb(origin, invDir, _nodes[c1].Min, _nodes[c1].Max, bestT, out float dist1);
                bool hit2 = IntersectRayAabb(origin, invDir, _nodes[c2].Min, _nodes[c2].Max, bestT, out float dist2);

                if (hit1 && hit2)
                {
                    if (dist1 < dist2)
                    {
                        stack[stackPtr++] = c2;
                        stack[stackPtr++] = c1;
                    }
                    else
                    {
                        stack[stackPtr++] = c1;
                        stack[stackPtr++] = c2;
                    }
                }
                else if (hit1)
                {
                    stack[stackPtr++] = c1;
                }
                else if (hit2)
                {
                    stack[stackPtr++] = c2;
                }
            }
        }

        if (bestTri < 0)
        {
            return default;
        }

        return new RayHit(true, bestT, bestTri, origin + direction * bestT, bestBary);
    }

    public ClosestPointHit ClosestPoint(Vector3 point, float maxDistanceSquared = float.PositiveInfinity)
    {
        if (_nodesUsed == 0)
        {
            return new ClosestPointHit(-1, default, float.PositiveInfinity);
        }

        ReadOnlySpan<Vector3> positions = _mesh.Positions.Span;
        ReadOnlySpan<int> indices = _mesh.Indices.Span;

        float bestDistSq = maxDistanceSquared;
        int bestTri = -1;
        Vector3 bestPt = default;

        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;

        while (stackPtr > 0)
        {
            int nodeIdx = stack[--stackPtr];
            ref readonly BvhNode node = ref _nodes[nodeIdx];

            if (PointAabbDistanceSquared(point, node.Min, node.Max) > bestDistSq)
            {
                continue;
            }

            if (node.Count > 0)
            {
                int first = node.LeftFirst;
                int end = first + node.Count;
                for (int i = first; i < end; i++)
                {
                    int triIdx = _triIndices[i];
                    Vector3 a = positions[indices[triIdx * 3]];
                    Vector3 b = positions[indices[triIdx * 3 + 1]];
                    Vector3 c = positions[indices[triIdx * 3 + 2]];

                    Vector3 cp = ClosestPointOnTriangle(point, a, b, c);
                    float dSq = Vector3.DistanceSquared(point, cp);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestTri = triIdx;
                        bestPt = cp;
                    }
                }
            }
            else
            {
                int c1 = node.LeftFirst;
                int c2 = c1 + 1;

                float d1 = PointAabbDistanceSquared(point, _nodes[c1].Min, _nodes[c1].Max);
                float d2 = PointAabbDistanceSquared(point, _nodes[c2].Min, _nodes[c2].Max);

                if (d1 <= bestDistSq && d2 <= bestDistSq)
                {
                    if (d1 < d2)
                    {
                        stack[stackPtr++] = c2;
                        stack[stackPtr++] = c1;
                    }
                    else
                    {
                        stack[stackPtr++] = c1;
                        stack[stackPtr++] = c2;
                    }
                }
                else if (d1 <= bestDistSq)
                {
                    stack[stackPtr++] = c1;
                }
                else if (d2 <= bestDistSq)
                {
                    stack[stackPtr++] = c2;
                }
            }
        }

        return new ClosestPointHit(bestTri, bestPt, bestDistSq);
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection (double-sided).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IntersectRayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float t,
        out float u,
        out float v)
    {
        t = 0f;
        u = 0f;
        v = 0f;

        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 pvec = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, pvec);

        if (MathF.Abs(det) < 1e-10f)
        {
            return false;
        }

        float invDet = 1.0f / det;
        Vector3 tvec = origin - a;
        u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < -1e-6f || u > 1.0f + 1e-6f)
        {
            return false;
        }

        Vector3 qvec = Vector3.Cross(tvec, e1);
        v = Vector3.Dot(direction, qvec) * invDet;
        if (v < -1e-6f || u + v > 1.0f + 1e-6f)
        {
            return false;
        }

        t = Vector3.Dot(e2, qvec) * invDet;
        return t >= 1e-6f;
    }

    /// <summary>
    /// Computes the exact closest point on triangle (a, b, c) to query point p (Ericson, Real-Time Collision Detection).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + v * ab;
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + w * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        float denom = 1.0f / (va + vb + vc);
        float vBary = vb * denom;
        float wBary = vc * denom;
        return a + ab * vBary + ac * wBary;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectRayAabb(
        Vector3 origin,
        Vector3 invDir,
        Vector3 boxMin,
        Vector3 boxMax,
        float maxT,
        out float tNearOut)
    {
        Vector3 t0 = (boxMin - origin) * invDir;
        Vector3 t1 = (boxMax - origin) * invDir;

        Vector3 tMin = Vector3.Min(t0, t1);
        Vector3 tMax = Vector3.Max(t0, t1);

        float tNear = MathF.Max(0f, MathF.Max(tMin.X, MathF.Max(tMin.Y, tMin.Z)));
        float tFar = MathF.Min(maxT, MathF.Min(tMax.X, MathF.Min(tMax.Y, tMax.Z)));

        tNearOut = tNear;
        return tNear <= tFar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float PointAabbDistanceSquared(Vector3 p, Vector3 min, Vector3 max)
    {
        Vector3 clamped = Vector3.Clamp(p, min, max);
        return Vector3.DistanceSquared(p, clamped);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HalfBoxArea(Vector3 min, Vector3 max)
    {
        Vector3 e = max - min;
        return e.X * e.Y + e.Y * e.Z + e.Z * e.X;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetAxis(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z
    };
}

