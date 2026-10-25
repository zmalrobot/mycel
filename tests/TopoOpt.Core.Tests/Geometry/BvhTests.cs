using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using Xunit;

namespace TopoOpt.Core.Tests.Geometry;

public sealed class BvhTests
{
    [Fact]
    public void RayCast_10kDeterministicRays_MatchesBruteForceExactly()
    {
        List<Tri> tris = StlFixtures.Cylinder(12f, 24f, 48, 5f);
        IndexedMesh mesh = LoadMeshFromTris(tris);
        Bvh bvh = new(mesh);

        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<int> indices = mesh.Indices.Span;
        int triCount = mesh.TriangleCount;

        // Deterministic LCG / Weyl sequence for 10,000 rays
        uint state = 0x12345678u;
        float NextFloat(float min, float max)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float t = (state & 0x00FFFFFFu) / (float)0x01000000u;
            return min + t * (max - min);
        }

        int hits = 0;
        for (int r = 0; r < 10_000; r++)
        {
            Vector3 origin = new(NextFloat(-25f, 25f), NextFloat(-25f, 25f), NextFloat(-25f, 25f));
            Vector3 target = new(NextFloat(-15f, 15f), NextFloat(-15f, 15f), NextFloat(-15f, 15f));
            Vector3 dir = Vector3.Normalize(target - origin);

            RayHit bvhHit = bvh.RayCast(origin, dir);
            RayHit bfHit = BruteForceRayCast(positions, indices, triCount, origin, dir);

            Assert.Equal(bfHit.IsHit, bvhHit.IsHit);
            if (bfHit.IsHit)
            {
                hits++;
                Assert.True(MathF.Abs(bfHit.T - bvhHit.T) < 1e-4f,
                    $"Ray {r}: BVH T={bvhHit.T} vs BF T={bfHit.T}");
                // If distances are not tied on a shared triangle edge, triangle index must match
                if (MathF.Abs(bfHit.T - bvhHit.T) < 1e-6f)
                {
                    Assert.Equal(bfHit.TriangleIndex, bvhHit.TriangleIndex);
                }
            }
        }

        Assert.True(hits > 2_000);
    }

    [Fact]
    public void ClosestPoint_MatchesBruteForceOnLBracket()
    {
        List<Tri> tris = StlFixtures.LBracket(30f, 8f, 6f);
        IndexedMesh mesh = LoadMeshFromTris(tris);
        Bvh bvh = new(mesh);

        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<int> indices = mesh.Indices.Span;
        int triCount = mesh.TriangleCount;

        uint state = 0xCAFEBABEu;
        float NextFloat(float min, float max)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float t = (state & 0x00FFFFFFu) / (float)0x01000000u;
            return min + t * (max - min);
        }

        for (int i = 0; i < 500; i++)
        {
            Vector3 query = new(NextFloat(-10f, 40f), NextFloat(-10f, 40f), NextFloat(-15f, 15f));
            ClosestPointHit bvhClosest = bvh.ClosestPoint(query);
            ClosestPointHit bfClosest = BruteForceClosestPoint(positions, indices, triCount, query);

            Assert.True(MathF.Abs(bvhClosest.DistanceSquared - bfClosest.DistanceSquared) <= 1e-4f * MathF.Max(1f, bfClosest.DistanceSquared),
                $"Query {i}: BVH distSq={bvhClosest.DistanceSquared} vs BF distSq={bfClosest.DistanceSquared}");
        }
    }

    private static IndexedMesh LoadMeshFromTris(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }

    private static RayHit BruteForceRayCast(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, int triCount, Vector3 origin, Vector3 dir)
    {
        float bestT = float.PositiveInfinity;
        int bestTri = -1;
        Vector3 bestBary = default;

        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = positions[indices[i * 3]];
            Vector3 b = positions[indices[i * 3 + 1]];
            Vector3 c = positions[indices[i * 3 + 2]];

            if (Bvh.IntersectRayTriangle(origin, dir, a, b, c, out float t, out float u, out float v) && t < bestT)
            {
                bestT = t;
                bestTri = i;
                bestBary = new Vector3(1f - u - v, u, v);
            }
        }

        if (bestTri < 0)
        {
            return default;
        }

        return new RayHit(true, bestT, bestTri, origin + dir * bestT, bestBary);
    }

    private static ClosestPointHit BruteForceClosestPoint(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, int triCount, Vector3 p)
    {
        float bestDistSq = float.PositiveInfinity;
        int bestTri = -1;
        Vector3 bestPt = default;

        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = positions[indices[i * 3]];
            Vector3 b = positions[indices[i * 3 + 1]];
            Vector3 c = positions[indices[i * 3 + 2]];

            Vector3 cp = Bvh.ClosestPointOnTriangle(p, a, b, c);
            float dSq = Vector3.DistanceSquared(p, cp);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestTri = i;
                bestPt = cp;
            }
        }

        return new ClosestPointHit(bestTri, bestPt, bestDistSq);
    }
}

