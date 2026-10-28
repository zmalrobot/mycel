using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Mycel.Core.Geometry;

namespace Mycel.Core.PostProcessing;

/// <summary>
/// Implements volume-preserving Taubin (λ|μ) surface mesh smoothing (Taubin 1995).
/// Eliminates voxel staircase artifacts without shrinking the enclosed volume.
/// </summary>
public static class TaubinSmoother
{
    public static IndexedMesh Smooth(
        IndexedMesh mesh,
        int iterations = 3,
        float lambda = 0.50f,
        float mu = -0.53f,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (iterations <= 0 || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
        {
            return mesh;
        }

        int vCount = mesh.VertexCount;
        ReadOnlySpan<int> indices = mesh.Indices.Span;

        // Build CSR vertex adjacency graph
        int[] degree = new int[vCount];
        for (int i = 0; i < indices.Length; i++)
        {
            degree[indices[i]] += 2;
        }

        int[] rowOffsets = new int[vCount + 1];
        for (int v = 0; v < vCount; v++)
        {
            rowOffsets[v + 1] = rowOffsets[v] + degree[v];
        }

        int[] rawNeighbors = new int[rowOffsets[vCount]];
        int[] writeCursor = new int[vCount];
        Array.Copy(rowOffsets, writeCursor, vCount);

        int triCount = mesh.TriangleCount;
        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            rawNeighbors[writeCursor[i0]++] = i1;
            rawNeighbors[writeCursor[i0]++] = i2;

            rawNeighbors[writeCursor[i1]++] = i2;
            rawNeighbors[writeCursor[i1]++] = i0;

            rawNeighbors[writeCursor[i2]++] = i0;
            rawNeighbors[writeCursor[i2]++] = i1;
        }

        // Deduplicate neighbors in place per vertex
        int[] uniqueCount = new int[vCount];
        for (int v = 0; v < vCount; v++)
        {
            int start = rowOffsets[v];
            int end = rowOffsets[v + 1];
            int len = end - start;
            if (len == 0) continue;

            Array.Sort(rawNeighbors, start, len);
            int dst = start;
            rawNeighbors[dst++] = rawNeighbors[start];
            for (int k = start + 1; k < end; k++)
            {
                if (rawNeighbors[k] != rawNeighbors[dst - 1] && rawNeighbors[k] != v)
                {
                    rawNeighbors[dst++] = rawNeighbors[k];
                }
            }
            uniqueCount[v] = dst - start;
        }

        Vector3[] current = mesh.Positions.ToArray();
        Vector3[] scratch = new Vector3[vCount];

        for (int iter = 0; iter < iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // Step 1: Forward pass with +lambda
            ApplyLaplacianStep(current, scratch, rowOffsets, uniqueCount, rawNeighbors, lambda);

            // Step 2: Backward pass with -mu (inflate)
            ApplyLaplacianStep(scratch, current, rowOffsets, uniqueCount, rawNeighbors, mu);
        }

        return new IndexedMesh(current, mesh.Indices.ToArray());
    }

    private static void ApplyLaplacianStep(
        Vector3[] src,
        Vector3[] dst,
        int[] rowOffsets,
        int[] uniqueCount,
        int[] neighbors,
        float factor)
    {
        int vCount = src.Length;
        for (int v = 0; v < vCount; v++)
        {
            int count = uniqueCount[v];
            if (count == 0)
            {
                dst[v] = src[v];
                continue;
            }

            int start = rowOffsets[v];
            Vector3 p = src[v];
            Vector3 sumDiff = Vector3.Zero;

            for (int k = 0; k < count; k++)
            {
                sumDiff += src[neighbors[start + k]] - p;
            }

            dst[v] = p + (factor / count) * sumDiff;
        }
    }
}

