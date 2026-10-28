using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Mycel.Core.Geometry;

namespace Mycel.App.Diagnostics;

/// <summary>
/// Generates large subdivided stress meshes in a temporary folder for performance and UI responsiveness profiling.
/// These meshes are never committed to git and are cleaned up on demand.
/// </summary>
public static class StressMeshGenerator
{
    public static string TempDir { get; } = Path.Combine(Path.GetTempPath(), "Mycel_Stress");

    static StressMeshGenerator()
    {
        try
        {
            Directory.CreateDirectory(TempDir);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Uniformly subdivides an input mesh so that each triangle is split into 4 smaller triangles
    /// until the target triangle count is reached or exceeded.
    /// </summary>
    public static IndexedMesh Subdivide(IndexedMesh input, int targetTriangleCount)
    {
        IndexedMesh current = input;
        while (current.TriangleCount < targetTriangleCount)
        {
            current = SubdivideOnce(current);
            if (current.TriangleCount > 2_500_000)
            {
                break;
            }
        }
        return current;
    }

    /// <summary>
    /// Performs one uniform 1-to-4 triangle subdivision pass with shared edge midpoint sharing.
    /// </summary>
    public static IndexedMesh SubdivideOnce(IndexedMesh mesh)
    {
        var posSpan = mesh.Positions.Span;
        var idxSpan = mesh.Indices.Span;

        List<Vector3> newVertices = new(posSpan.ToArray());
        Dictionary<(int, int), int> edgeMidpoints = new();

        int GetMidpoint(int i0, int i1)
        {
            int min = Math.Min(i0, i1);
            int max = Math.Max(i0, i1);
            var key = (min, max);

            if (edgeMidpoints.TryGetValue(key, out int midIndex))
            {
                return midIndex;
            }

            Vector3 midPos = (newVertices[min] + newVertices[max]) * 0.5f;
            midIndex = newVertices.Count;
            newVertices.Add(midPos);
            edgeMidpoints.Add(key, midIndex);
            return midIndex;
        }

        int originalTriCount = mesh.TriangleCount;
        int[] newIndices = new int[originalTriCount * 12]; // 4 triangles * 3 indices

        for (int i = 0; i < originalTriCount; i++)
        {
            int i3 = i * 3;
            int a = idxSpan[i3];
            int b = idxSpan[i3 + 1];
            int c = idxSpan[i3 + 2];

            int mab = GetMidpoint(a, b);
            int mbc = GetMidpoint(b, c);
            int mca = GetMidpoint(c, a);

            int outBase = i * 12;
            // Tri 1: a, mab, mca
            newIndices[outBase + 0] = a;
            newIndices[outBase + 1] = mab;
            newIndices[outBase + 2] = mca;

            // Tri 2: b, mbc, mab
            newIndices[outBase + 3] = b;
            newIndices[outBase + 4] = mbc;
            newIndices[outBase + 5] = mab;

            // Tri 3: c, mca, mbc
            newIndices[outBase + 6] = c;
            newIndices[outBase + 7] = mca;
            newIndices[outBase + 8] = mbc;

            // Tri 4: mab, mbc, mca
            newIndices[outBase + 9] = mab;
            newIndices[outBase + 10] = mbc;
            newIndices[outBase + 11] = mca;
        }

        return new IndexedMesh(newVertices.ToArray(), newIndices);
    }

    /// <summary>
    /// Writes an IndexedMesh as binary STL into the temporary stress folder.
    /// </summary>
    public static string WriteToTempStl(IndexedMesh mesh, string filename)
    {
        Directory.CreateDirectory(TempDir);
        string fullPath = Path.Combine(TempDir, filename);

        using (FileStream fs = File.Create(fullPath))
        using (BinaryWriter bw = new(fs))
        {
            byte[] header = new byte[80];
            bw.Write(header);
            bw.Write((uint)mesh.TriangleCount);

            var normals = mesh.TriangleNormals.Span;
            for (int i = 0; i < mesh.TriangleCount; i++)
            {
                mesh.GetTriangle(i, out Vector3 v0, out Vector3 v1, out Vector3 v2);
                Vector3 normal = normals[i];
                if (!float.IsFinite(normal.X)) normal = Vector3.UnitZ;

                bw.Write(normal.X);
                bw.Write(normal.Y);
                bw.Write(normal.Z);

                bw.Write(v0.X);
                bw.Write(v0.Y);
                bw.Write(v0.Z);

                bw.Write(v1.X);
                bw.Write(v1.Y);
                bw.Write(v1.Z);

                bw.Write(v2.X);
                bw.Write(v2.Y);
                bw.Write(v2.Z);

                bw.Write((ushort)0);
            }
        }

        return fullPath;
    }

    /// <summary>
    /// Deletes any temporary stress meshes created in the temp directory.
    /// </summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }
        }
        catch
        {
        }
    }
}

