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

    public Vector3 Centroid { get; set; } = Vector3.Zero;
    public Vector3 MeanNormal { get; set; } = -Vector3.UnitZ;

    public Vector3 ComputeUnitDirection()
    {
        return DirectionMode switch
        {
            LoadDirectionMode.SurfaceNormalInward => MeanNormal != Vector3.Zero ? -Vector3.Normalize(MeanNormal) : -Vector3.UnitZ,
            LoadDirectionMode.SurfaceNormalOutward => MeanNormal != Vector3.Zero ? Vector3.Normalize(MeanNormal) : Vector3.UnitZ,
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

    public void UpdateGeometryMetrics(IndexedMesh mesh)
    {
        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
            MeanNormal = -Vector3.UnitZ;
            return;
        }

        Vector3 sumCentroid = Vector3.Zero;
        Vector3 sumNormal = Vector3.Zero;
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
            Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
            float area = 0.5f * cross.Length();

            if (area > 1e-12f)
            {
                Vector3 normal = Vector3.Normalize(cross);
                sumCentroid += triCentroid * area;
                sumNormal += normal * area;
                totalArea += area;
            }
        }

        if (totalArea > 1e-12)
        {
            Centroid = sumCentroid / (float)totalArea;
            MeanNormal = sumNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(sumNormal) : -Vector3.UnitZ;
        }
        else
        {
            Centroid = Vector3.Zero;
            MeanNormal = -Vector3.UnitZ;
        }
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
    public Vector3 Centroid { get; set; } = Vector3.Zero;

    public void UpdateGeometryMetrics(IndexedMesh mesh)
    {
        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
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

        Centroid = totalArea > 1e-12 ? sumCentroid / (float)totalArea : Vector3.Zero;
    }
}

public sealed class PreserveRegion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Preserve";
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public Vector3 Centroid { get; set; } = Vector3.Zero;

    public void UpdateGeometryMetrics(IndexedMesh mesh)
    {
        if (mesh == null || Triangles.Length == 0)
        {
            Centroid = Vector3.Zero;
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

        Centroid = totalArea > 1e-12 ? sumCentroid / (float)totalArea : Vector3.Zero;
    }
}
