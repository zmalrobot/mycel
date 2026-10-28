using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Xunit;

namespace Mycel.Core.Tests.Fixtures;

public sealed class StlFixturesTests
{
    [Fact]
    public void Box_Volume_MatchesAnalytical()
    {
        Vector3 size = new(12.5f, 8.25f, 20.0f);
        List<Tri> box = StlFixtures.Box(size);

        Assert.Equal(12, box.Count);

        double expected = (double)size.X * size.Y * size.Z;
        double actual = StlFixtures.SignedVolume(box);

        Assert.True(actual > 0.0);
        Assert.True(Math.Abs(actual - expected) / expected < 1e-6);
    }

    [Fact]
    public void Cylinder_Solid_Volume_MatchesPolygonalFormula()
    {
        float r = 10.0f;
        float h = 25.0f;
        int n = 32;

        List<Tri> cyl = StlFixtures.Cylinder(r, h, n);

        double expected = 0.5 * n * r * r * Math.Sin(2.0 * Math.PI / n) * h;
        double actual = StlFixtures.SignedVolume(cyl);

        Assert.True(actual > 0.0);
        Assert.True(Math.Abs(actual - expected) / expected < 1e-6);
    }

    [Fact]
    public void Cylinder_WithHole_Volume_MatchesOuterMinusInner()
    {
        float rOut = 15.0f;
        float rIn = 6.0f;
        float h = 30.0f;
        int n = 48;

        List<Tri> hollow = StlFixtures.Cylinder(rOut, h, n, rIn);
        List<Tri> outer = StlFixtures.Cylinder(rOut, h, n, 0f);
        List<Tri> inner = StlFixtures.Cylinder(rIn, h, n, 0f);

        double expectedFormula = 0.5 * n * (rOut * rOut - rIn * rIn) * Math.Sin(2.0 * Math.PI / n) * h;
        double expectedDiff = StlFixtures.SignedVolume(outer) - StlFixtures.SignedVolume(inner);
        double actual = StlFixtures.SignedVolume(hollow);

        Assert.True(actual > 0.0);
        Assert.True(Math.Abs(actual - expectedFormula) / expectedFormula < 1e-6);
        Assert.True(Math.Abs(actual - expectedDiff) / expectedDiff < 1e-6);
    }

    [Fact]
    public void Sphere_Volume_ApproximatesTrueSphereFromBelow()
    {
        float r = 10.0f;
        double trueSphere = (4.0 / 3.0) * Math.PI * r * r * r;

        List<Tri> sphere16 = StlFixtures.Sphere(r, 16, 16);
        List<Tri> sphere64 = StlFixtures.Sphere(r, 64, 64);

        double vol16 = StlFixtures.SignedVolume(sphere16);
        double vol64 = StlFixtures.SignedVolume(sphere64);

        Assert.True(vol16 > 0.0 && vol16 < trueSphere);
        Assert.True(vol64 > vol16 && vol64 < trueSphere);
        Assert.True((trueSphere - vol64) / trueSphere < 0.01);
    }

    [Fact]
    public void LBracket_Volume_MatchesAnalytical()
    {
        float armLength = 40.0f;
        float armWidth = 10.0f;
        float thickness = 8.0f;

        List<Tri> bracket = StlFixtures.LBracket(armLength, armWidth, thickness);

        double expected = (double)thickness * armWidth * (2.0 * armLength - armWidth);
        double actual = StlFixtures.SignedVolume(bracket);

        Assert.True(actual > 0.0);
        Assert.True(Math.Abs(actual - expected) / expected < 1e-6);
    }

    [Fact]
    public void AllSolids_AreClosedAndTwoManifoldWithOppositeEdgeOrientations()
    {
        AssertIsClosedTwoManifold(StlFixtures.Box(new Vector3(10f, 20f, 30f)));
        AssertIsClosedTwoManifold(StlFixtures.Cylinder(8f, 16f, 24, 0f));
        AssertIsClosedTwoManifold(StlFixtures.Cylinder(8f, 16f, 24, 3f));
        AssertIsClosedTwoManifold(StlFixtures.Sphere(5f, 16, 24));
        AssertIsClosedTwoManifold(StlFixtures.LBracket(30f, 8f, 5f));
    }

    [Fact]
    public void RemoveTriangle_OnBox_LeavesExactlyThreeOpenEdges()
    {
        List<Tri> box = StlFixtures.Box(new Vector3(10f, 10f, 10f));
        List<Tri> openBox = StlFixtures.RemoveTriangle(box, 0);

        Assert.Equal(11, openBox.Count);

        var (directedEdges, undirectedCounts) = AnalyzeEdges(openBox);
        int boundaryEdges = undirectedCounts.Values.Count(c => c == 1);
        int interiorEdges = undirectedCounts.Values.Count(c => c == 2);

        Assert.Equal(3, boundaryEdges);
        Assert.Equal(15, interiorEdges);
        Assert.Equal(33, directedEdges.Count);
    }

    [Fact]
    public void WriteBinary_ProducesExpectedSizeAndHeader()
    {
        List<Tri> box = StlFixtures.Box(new Vector3(5f, 5f, 5f));

        using MemoryStream msStandard = new();
        StlFixtures.WriteBinary(msStandard, box, headerStartsWithSolid: false);
        byte[] bytesStandard = msStandard.ToArray();

        Assert.Equal(84 + 50 * box.Count, bytesStandard.Length);
        Assert.NotEqual("solid", Encoding.ASCII.GetString(bytesStandard, 0, 5));

        using MemoryStream msSolid = new();
        StlFixtures.WriteBinary(msSolid, box, headerStartsWithSolid: true);
        byte[] bytesSolid = msSolid.ToArray();

        Assert.Equal(84 + 50 * box.Count, bytesSolid.Length);
        Assert.Equal("solid", Encoding.ASCII.GetString(bytesSolid, 0, 5));
    }

    [Fact]
    public void WriteAscii_ProducesValidStructureAndFacetCount()
    {
        List<Tri> bracket = StlFixtures.LBracket(20f, 5f, 4f);

        using MemoryStream ms = new();
        StlFixtures.WriteAscii(ms, bracket);
        string text = Encoding.UTF8.GetString(ms.ToArray()).Trim();

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.StartsWith("solid", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("endsolid", lines[^1], StringComparison.Ordinal);

        int facetCount = lines.Count(l => l.StartsWith("facet normal ", StringComparison.Ordinal));
        Assert.Equal(bracket.Count, facetCount);
    }

    [Fact]
    public void Transform_WithRotationAndTranslation_PreservesSignedVolume()
    {
        List<Tri> original = StlFixtures.LBracket(25f, 7f, 6f);
        double originalVol = StlFixtures.SignedVolume(original);

        Quaternion rot = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.73f, -1.12f, 0.41f));
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(15.5f, -42.0f, 8.25f);

        List<Tri> transformed = StlFixtures.Transform(original, m);
        double transformedVol = StlFixtures.SignedVolume(transformed);

        Assert.True(Math.Abs(transformedVol - originalVol) / originalVol < 1e-6);

        List<Tri> flipped = StlFixtures.FlipTriangle(original, 0);
        var (_, undirectedCounts) = AnalyzeEdges(flipped);
        Assert.All(undirectedCounts.Values, c => Assert.Equal(2, c));
    }

    private static void AssertIsClosedTwoManifold(IReadOnlyList<Tri> tris)
    {
        var (directedEdges, undirectedCounts) = AnalyzeEdges(tris);

        // Every undirected edge must be shared by exactly 2 triangles.
        Assert.All(undirectedCounts.Values, count => Assert.Equal(2, count));

        // Every directed edge (u -> v) must appear once and have its opposite (v -> u) present.
        foreach (var ((u, v), count) in directedEdges)
        {
            Assert.NotEqual(u, v);
            Assert.Equal(1, count);
            Assert.True(directedEdges.TryGetValue((v, u), out int revCount) && revCount == 1);
        }
    }

    private static (Dictionary<(int U, int V), int> Directed, Dictionary<(int Min, int Max), int> Undirected) AnalyzeEdges(IReadOnlyList<Tri> tris)
    {
        Dictionary<Vector3, int> vertexToId = new();
        int GetVertexId(Vector3 v)
        {
            if (!vertexToId.TryGetValue(v, out int id))
            {
                id = vertexToId.Count;
                vertexToId[v] = id;
            }
            return id;
        }

        Dictionary<(int U, int V), int> directed = new();
        Dictionary<(int Min, int Max), int> undirected = new();

        foreach (Tri t in tris)
        {
            int a = GetVertexId(t.A);
            int b = GetVertexId(t.B);
            int c = GetVertexId(t.C);

            ReadOnlySpan<(int U, int V)> edges = [(a, b), (b, c), (c, a)];
            foreach (var (u, v) in edges)
            {
                directed[(u, v)] = directed.GetValueOrDefault((u, v)) + 1;
                var key = u < v ? (u, v) : (v, u);
                undirected[key] = undirected.GetValueOrDefault(key) + 1;
            }
        }

        return (directed, undirected);
    }
}
