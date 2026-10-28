using System;
using System.Collections.Generic;
using System.Numerics;
using Mycel.Core.Geometry;

namespace Mycel.Core.Workflow;

public enum LoadDirectionMode
{
    SurfaceNormalInward,
    SurfaceNormalOutward,
    AxisPositiveX,
    AxisNegativeX,
    AxisPositiveY,
    AxisNegativeY,
    AxisPositiveZ,
    AxisNegativeZ,
    CustomVector
}

public sealed class LoadRegion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Load";
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public float MagnitudeNewtons { get; set; } = 1000.0f;
    public LoadDirectionMode DirectionMode { get; set; } = LoadDirectionMode.SurfaceNormalInward;
    public Vector3 CustomDirection { get; set; } = -Vector3.UnitZ;

    public int SeedTriangle { get; set; } = -1;
    public Vector3 Centroid { get; set; } = Vector3.Zero;
    public Vector3 AnchorPoint { get; set; } = Vector3.Zero;
    public Vector3 MeanNormal { get; set; } = -Vector3.UnitZ;
    public Vector3 FallbackSeedNormal { get; set; } = -Vector3.UnitZ;
    public float SurfaceAreaMm2 { get; set; } = 0.0f;
    public float ModelSurfaceCoveragePercent { get; set; } = 0.0f;
    public float NormalConsistencyRatio { get; set; } = 1.0f;
    public int ConnectedComponentCount { get; set; } = 1;

    public float NominalPressureMpa => SurfaceAreaMm2 > 1e-6f ? MagnitudeNewtons / SurfaceAreaMm2 : 0f;
    public bool IsWeakNormal => DirectionMode is LoadDirectionMode.SurfaceNormalInward or LoadDirectionMode.SurfaceNormalOutward
                                && NormalConsistencyRatio < 0.30f;
    public bool IsHighCoverage => ModelSurfaceCoveragePercent > 40.0f;
    public bool IsDisconnected => ConnectedComponentCount > 1;

    public Vector3 ComputeUnitDirection()
    {
        bool useFallback = IsWeakNormal && FallbackSeedNormal != Vector3.Zero;
        Vector3 baseNormal = useFallback ? FallbackSeedNormal : MeanNormal;

        return DirectionMode switch
        {
            LoadDirectionMode.SurfaceNormalInward => baseNormal != Vector3.Zero ? -Vector3.Normalize(baseNormal) : -Vector3.UnitZ,
            LoadDirectionMode.SurfaceNormalOutward => baseNormal != Vector3.Zero ? Vector3.Normalize(baseNormal) : Vector3.UnitZ,
            LoadDirectionMode.AxisPositiveX => Vector3.UnitX,
            LoadDirectionMode.AxisNegativeX => -Vector3.UnitX,
            LoadDirectionMode.AxisPositiveY => Vector3.UnitY,
            LoadDirectionMode.AxisNegativeY => -Vector3.UnitY,
            LoadDirectionMode.AxisPositiveZ => Vector3.UnitZ,
            LoadDirectionMode.AxisNegativeZ => -Vector3.UnitZ,
            LoadDirectionMode.CustomVector => CustomDirection != Vector3.Zero ? Vector3.Normalize(CustomDirection) : -Vector3.UnitZ,
            _ => -Vector3.UnitZ
        };
    }

    public Vector3 ComputeTotalForce()
    {
        return ComputeUnitDirection() * MagnitudeNewtons;
    }

    public void UpdateGeometryMetrics(IndexedMesh mesh, int seedTriangle = -1)
    {
        if (seedTriangle >= 0)
        {
            SeedTriangle = seedTriangle;
        }

        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
            AnchorPoint = Vector3.Zero;
            MeanNormal = -Vector3.UnitZ;
            FallbackSeedNormal = -Vector3.UnitZ;
            SurfaceAreaMm2 = 0f;
            ModelSurfaceCoveragePercent = 0f;
            NormalConsistencyRatio = 1.0f;
            ConnectedComponentCount = 0;
            return;
        }

        Vector3 sumCentroid = Vector3.Zero;
        Vector3 sumNormal = Vector3.Zero;
        double totalArea = 0.0;

        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<Vector3> faceNormals = mesh.TriangleNormals.Span;

        float maxTriArea = -1f;
        Vector3 maxTriNormal = -Vector3.UnitZ;

        for (int i = 0; i < Triangles.Length; i++)
        {
            int triIdx = Triangles[i];
            if (triIdx < 0 || triIdx >= mesh.TriangleCount)
                continue;

            int i0 = indices[triIdx * 3 + 0];
            int i1 = indices[triIdx * 3 + 1];
            int i2 = indices[triIdx * 3 + 2];

            Vector3 v0 = positions[i0];
            Vector3 v1 = positions[i1];
            Vector3 v2 = positions[i2];

            Vector3 triCentroid = (v0 + v1 + v2) / 3.0f;
            Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
            float area = 0.5f * cross.Length();

            if (area > 1e-12f)
            {
                Vector3 normal = Vector3.Normalize(cross);
                sumCentroid += triCentroid * area;
                sumNormal += normal * area;
                totalArea += area;

                if (area > maxTriArea)
                {
                    maxTriArea = area;
                    maxTriNormal = normal;
                }
            }
        }

        SurfaceAreaMm2 = (float)totalArea;
        double meshArea = mesh.SurfaceArea;
        ModelSurfaceCoveragePercent = meshArea > 1e-6 ? (float)(totalArea / meshArea * 100.0) : 0f;

        if (totalArea > 1e-12)
        {
            Centroid = sumCentroid / (float)totalArea;
            float normalLen = sumNormal.Length();
            NormalConsistencyRatio = (float)(normalLen / totalArea);
            MeanNormal = normalLen > 1e-6f ? sumNormal / normalLen : -Vector3.UnitZ;
        }
        else
        {
            Centroid = Vector3.Zero;
            MeanNormal = -Vector3.UnitZ;
            NormalConsistencyRatio = 1.0f;
        }

        // Determine fallback normal (from seed or largest facet)
        if (SeedTriangle >= 0 && SeedTriangle < mesh.TriangleCount)
        {
            FallbackSeedNormal = faceNormals[SeedTriangle];
        }
        else
        {
            FallbackSeedNormal = maxTriNormal;
        }

        // Find anchor point on the physical surface closest to Centroid
        AnchorPoint = ComputeClosestSurfacePoint(mesh, Centroid, Triangles);

        // Count connected components
        ConnectedComponentCount = ComputeConnectedComponents(mesh, Triangles);
    }

    public List<(Vector3 Point, Vector3 Normal)> SampleDistributedPoints(IndexedMesh mesh, int maxSamples = 40)
    {
        var result = new List<(Vector3 Point, Vector3 Normal)>();
        if (mesh == null || Triangles.Length == 0 || maxSamples <= 0)
            return result;

        int count = Math.Min(Triangles.Length, maxSamples);
        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<Vector3> normals = mesh.TriangleNormals.Span;

        if (Triangles.Length <= maxSamples)
        {
            foreach (int tri in Triangles)
            {
                if (tri < 0 || tri >= mesh.TriangleCount) continue;
                Vector3 v0 = positions[indices[tri * 3 + 0]];
                Vector3 v1 = positions[indices[tri * 3 + 1]];
                Vector3 v2 = positions[indices[tri * 3 + 2]];
                result.Add(((v0 + v1 + v2) / 3.0f, normals[tri]));
            }
            return result;
        }

        // Stratified area sampling across triangles
        double step = (double)Triangles.Length / count;
        for (int i = 0; i < count; i++)
        {
            int triIdx = Triangles[(int)(i * step)];
            if (triIdx < 0 || triIdx >= mesh.TriangleCount) continue;
            Vector3 v0 = positions[indices[triIdx * 3 + 0]];
            Vector3 v1 = positions[indices[triIdx * 3 + 1]];
            Vector3 v2 = positions[indices[triIdx * 3 + 2]];
            result.Add(((v0 + v1 + v2) / 3.0f, normals[triIdx]));
        }

        return result;
    }

    internal static Vector3 ComputeClosestSurfacePoint(IndexedMesh mesh, Vector3 query, int[] triangles)
    {
        if (triangles.Length == 0) return query;

        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;

        float bestDistSq = float.MaxValue;
        Vector3 bestPoint = query;

        foreach (int tri in triangles)
        {
            if (tri < 0 || tri >= mesh.TriangleCount) continue;
            Vector3 a = positions[indices[tri * 3 + 0]];
            Vector3 b = positions[indices[tri * 3 + 1]];
            Vector3 c = positions[indices[tri * 3 + 2]];

            Vector3 pt = ClosestPointOnTriangle(query, a, b, c);
            float distSq = Vector3.DistanceSquared(query, pt);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestPoint = pt;
            }
        }

        return bestPoint;
    }

    internal static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
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
        float v_bary = vb * denom;
        float w_bary = vc * denom;
        return a + ab * v_bary + ac * w_bary;
    }

    internal static int ComputeConnectedComponents(IndexedMesh mesh, int[] triangles)
    {
        if (triangles.Length <= 1) return triangles.Length;

        // Build vertex to triangle adjacency for the patch triangles
        var triSet = new HashSet<int>(triangles);
        var vertToTri = new Dictionary<int, List<int>>();
        ReadOnlySpan<int> indices = mesh.Indices.Span;

        foreach (int t in triangles)
        {
            if (t < 0 || t >= mesh.TriangleCount) continue;
            for (int e = 0; e < 3; e++)
            {
                int v = indices[t * 3 + e];
                if (!vertToTri.TryGetValue(v, out var list))
                {
                    list = new List<int>();
                    vertToTri[v] = list;
                }
                list.Add(t);
            }
        }

        var visited = new HashSet<int>();
        int components = 0;

        foreach (int startTri in triangles)
        {
            if (visited.Contains(startTri)) continue;
            components++;
            var queue = new Queue<int>();
            queue.Enqueue(startTri);
            visited.Add(startTri);

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                for (int e = 0; e < 3; e++)
                {
                    int v = indices[curr * 3 + e];
                    if (vertToTri.TryGetValue(v, out var neighbors))
                    {
                        foreach (int nb in neighbors)
                        {
                            if (triSet.Contains(nb) && visited.Add(nb))
                            {
                                queue.Enqueue(nb);
                            }
                        }
                    }
                }
            }
        }

        return components;
    }
}

public sealed class SupportRegion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Support";
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public bool FixX { get; set; } = true;
    public bool FixY { get; set; } = true;
    public bool FixZ { get; set; } = true;

    public int SeedTriangle { get; set; } = -1;
    public Vector3 Centroid { get; set; } = Vector3.Zero;
    public Vector3 AnchorPoint { get; set; } = Vector3.Zero;
    public float SurfaceAreaMm2 { get; set; } = 0.0f;
    public float ModelSurfaceCoveragePercent { get; set; } = 0.0f;
    public int ConnectedComponentCount { get; set; } = 1;

    public bool IsHighCoverage => ModelSurfaceCoveragePercent > 40.0f;
    public bool IsDisconnected => ConnectedComponentCount > 1;

    public void UpdateGeometryMetrics(IndexedMesh mesh, int seedTriangle = -1)
    {
        if (seedTriangle >= 0) SeedTriangle = seedTriangle;

        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
            AnchorPoint = Vector3.Zero;
            SurfaceAreaMm2 = 0f;
            ModelSurfaceCoveragePercent = 0f;
            ConnectedComponentCount = 0;
            return;
        }

        Vector3 sumCentroid = Vector3.Zero;
        double totalArea = 0.0;

        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;

        for (int i = 0; i < Triangles.Length; i++)
        {
            int triIdx = Triangles[i];
            if (triIdx < 0 || triIdx >= mesh.TriangleCount)
                continue;

            int i0 = indices[triIdx * 3 + 0];
            int i1 = indices[triIdx * 3 + 1];
            int i2 = indices[triIdx * 3 + 2];

            Vector3 v0 = positions[i0];
            Vector3 v1 = positions[i1];
            Vector3 v2 = positions[i2];

            Vector3 triCentroid = (v0 + v1 + v2) / 3.0f;
            float area = 0.5f * Vector3.Cross(v1 - v0, v2 - v0).Length();

            if (area > 1e-12f)
            {
                sumCentroid += triCentroid * area;
                totalArea += area;
            }
        }

        SurfaceAreaMm2 = (float)totalArea;
        double meshArea = mesh.SurfaceArea;
        ModelSurfaceCoveragePercent = meshArea > 1e-6 ? (float)(totalArea / meshArea * 100.0) : 0f;
        Centroid = totalArea > 1e-12 ? sumCentroid / (float)totalArea : Vector3.Zero;
        AnchorPoint = LoadRegion.ComputeClosestSurfacePoint(mesh, Centroid, Triangles);
        ConnectedComponentCount = LoadRegion.ComputeConnectedComponents(mesh, Triangles);
    }
}

public sealed class PreserveRegion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Preserve";
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public int SeedTriangle { get; set; } = -1;
    public Vector3 Centroid { get; set; } = Vector3.Zero;
    public Vector3 AnchorPoint { get; set; } = Vector3.Zero;
    public float SurfaceAreaMm2 { get; set; } = 0.0f;
    public float ModelSurfaceCoveragePercent { get; set; } = 0.0f;
    public int ConnectedComponentCount { get; set; } = 1;

    public bool IsHighCoverage => ModelSurfaceCoveragePercent > 40.0f;
    public bool IsDisconnected => ConnectedComponentCount > 1;

    public void UpdateGeometryMetrics(IndexedMesh mesh, int seedTriangle = -1)
    {
        if (seedTriangle >= 0) SeedTriangle = seedTriangle;

        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
            AnchorPoint = Vector3.Zero;
            SurfaceAreaMm2 = 0f;
            ModelSurfaceCoveragePercent = 0f;
            ConnectedComponentCount = 0;
            return;
        }

        Vector3 sumCentroid = Vector3.Zero;
        double totalArea = 0.0;

        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;

        for (int i = 0; i < Triangles.Length; i++)
        {
            int triIdx = Triangles[i];
            if (triIdx < 0 || triIdx >= mesh.TriangleCount)
                continue;

            int i0 = indices[triIdx * 3 + 0];
            int i1 = indices[triIdx * 3 + 1];
            int i2 = indices[triIdx * 3 + 2];

            Vector3 v0 = positions[i0];
            Vector3 v1 = positions[i1];
            Vector3 v2 = positions[i2];

            Vector3 triCentroid = (v0 + v1 + v2) / 3.0f;
            float area = 0.5f * Vector3.Cross(v1 - v0, v2 - v0).Length();

            if (area > 1e-12f)
            {
                sumCentroid += triCentroid * area;
                totalArea += area;
            }
        }

        SurfaceAreaMm2 = (float)totalArea;
        double meshArea = mesh.SurfaceArea;
        ModelSurfaceCoveragePercent = meshArea > 1e-6 ? (float)(totalArea / meshArea * 100.0) : 0f;
        Centroid = totalArea > 1e-12 ? sumCentroid / (float)totalArea : Vector3.Zero;
        AnchorPoint = LoadRegion.ComputeClosestSurfacePoint(mesh, Centroid, Triangles);
        ConnectedComponentCount = LoadRegion.ComputeConnectedComponents(mesh, Triangles);
    }
}
