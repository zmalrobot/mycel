using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Mycel.Core.Tests.Fixtures;

/// <summary>
/// Represents a single 3D triangle with counter-clockwise outward vertex winding.
/// </summary>
public readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C);

/// <summary>
/// Deterministic procedural triangle-mesh generators and STL writers for unit tests.
/// All generated solids are closed, 2-manifold, and have outward counter-clockwise normals.
/// </summary>
public static class StlFixtures
{
    /// <summary>
    /// Generates a closed, axis-aligned box centred at the origin with 12 triangles.
    /// Exact analytical volume: <c>V = size.X * size.Y * size.Z</c>.
    /// </summary>
    public static List<Tri> Box(Vector3 size)
    {
        float hx = size.X * 0.5f;
        float hy = size.Y * 0.5f;
        float hz = size.Z * 0.5f;

        Vector3 v000 = new(-hx, -hy, -hz);
        Vector3 v100 = new(+hx, -hy, -hz);
        Vector3 v110 = new(+hx, +hy, -hz);
        Vector3 v010 = new(-hx, +hy, -hz);
        Vector3 v001 = new(-hx, -hy, +hz);
        Vector3 v101 = new(+hx, -hy, +hz);
        Vector3 v111 = new(+hx, +hy, +hz);
        Vector3 v011 = new(-hx, +hy, +hz);

        return new List<Tri>(12)
        {
            // Bottom (-Z)
            new(v000, v010, v110),
            new(v000, v110, v100),
            // Top (+Z)
            new(v001, v101, v111),
            new(v001, v111, v011),
            // Front (-Y)
            new(v000, v100, v101),
            new(v000, v101, v001),
            // Back (+Y)
            new(v110, v010, v011),
            new(v110, v011, v111),
            // Left (-X)
            new(v010, v000, v001),
            new(v010, v001, v011),
            // Right (+X)
            new(v100, v110, v111),
            new(v100, v111, v101)
        };
    }

    /// <summary>
    /// Generates a closed polygonal cylinder centred at the origin along the Z axis.
    /// When <paramref name="holeRadius"/> &gt; 0, generates a coaxial through-hole with annular caps and an inner wall.
    /// Exact analytical volume (solid): <c>V = 0.5 * n * r^2 * sin(2 * pi / n) * h</c>.
    /// Exact analytical volume (with hole): <c>V = 0.5 * n * (r^2 - r_hole^2) * sin(2 * pi / n) * h</c>.
    /// </summary>
    public static List<Tri> Cylinder(float radius, float height, int segments, float holeRadius = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (holeRadius >= radius)
        {
            throw new ArgumentOutOfRangeException(nameof(holeRadius), "Hole radius must be smaller than outer radius.");
        }

        float zBot = -height * 0.5f;
        float zTop = +height * 0.5f;

        Vector3[] oBot = new Vector3[segments];
        Vector3[] oTop = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            double angle = 2.0 * Math.PI * i / segments;
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);
            oBot[i] = new Vector3(radius * c, radius * s, zBot);
            oTop[i] = new Vector3(radius * c, radius * s, zTop);
        }

        if (holeRadius <= 0f)
        {
            List<Tri> tris = new(segments * 4);
            Vector3 cBot = new(0f, 0f, zBot);
            Vector3 cTop = new(0f, 0f, zTop);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                // Top cap (+Z)
                tris.Add(new Tri(cTop, oTop[i], oTop[next]));
                // Bottom cap (-Z)
                tris.Add(new Tri(cBot, oBot[next], oBot[i]));
                // Outer wall
                tris.Add(new Tri(oBot[i], oBot[next], oTop[next]));
                tris.Add(new Tri(oBot[i], oTop[next], oTop[i]));
            }

            return tris;
        }
        else
        {
            Vector3[] iBot = new Vector3[segments];
            Vector3[] iTop = new Vector3[segments];

            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                float c = (float)Math.Cos(angle);
                float s = (float)Math.Sin(angle);
                iBot[i] = new Vector3(holeRadius * c, holeRadius * s, zBot);
                iTop[i] = new Vector3(holeRadius * c, holeRadius * s, zTop);
            }

            List<Tri> tris = new(segments * 8);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                // Top annular cap (+Z)
                tris.Add(new Tri(iTop[i], oTop[i], oTop[next]));
                tris.Add(new Tri(iTop[i], oTop[next], iTop[next]));

                // Bottom annular cap (-Z)
                tris.Add(new Tri(iBot[i], oBot[next], oBot[i]));
                tris.Add(new Tri(iBot[i], iBot[next], oBot[next]));

                // Outer wall (outward)
                tris.Add(new Tri(oBot[i], oBot[next], oTop[next]));
                tris.Add(new Tri(oBot[i], oTop[next], oTop[i]));

                // Inner wall (inward towards Z axis)
                tris.Add(new Tri(iBot[next], iBot[i], iTop[i]));
                tris.Add(new Tri(iBot[next], iTop[i], iTop[next]));
            }

            return tris;
        }
    }

    /// <summary>
    /// Generates a closed UV sphere centred at the origin.
    /// Exact polyhedral analytical volume:
    /// <c>V = (1/6) * lon * r^3 * sin(2*pi/lon) * sum_{i=0}^{lat-1} (sin(phi_i) + sin(phi_{i+1})) * (cos(phi_i) - cos(phi_{i+1}))</c>
    /// where <c>phi_i = i * pi / lat</c>, which approaches <c>(4/3) * pi * r^3</c> from below as segments increase.
    /// </summary>
    public static List<Tri> Sphere(float radius, int latSegments, int lonSegments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latSegments, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(lonSegments, 3);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        Vector3 northPole = new(0f, 0f, +radius);
        Vector3 southPole = new(0f, 0f, -radius);

        Vector3[,] rings = new Vector3[latSegments - 1, lonSegments];
        for (int i = 1; i < latSegments; i++)
        {
            double phi = Math.PI * i / latSegments;
            float z = (float)(radius * Math.Cos(phi));
            float rRing = (float)(radius * Math.Sin(phi));

            for (int j = 0; j < lonSegments; j++)
            {
                double theta = 2.0 * Math.PI * j / lonSegments;
                float x = (float)(rRing * Math.Cos(theta));
                float y = (float)(rRing * Math.Sin(theta));
                rings[i - 1, j] = new Vector3(x, y, z);
            }
        }

        List<Tri> tris = new(2 * lonSegments * (latSegments - 1));

        // North polar cap
        for (int j = 0; j < lonSegments; j++)
        {
            int next = (j + 1) % lonSegments;
            tris.Add(new Tri(northPole, rings[0, j], rings[0, next]));
        }

        // Intermediate latitude bands
        for (int i = 0; i < latSegments - 2; i++)
        {
            for (int j = 0; j < lonSegments; j++)
            {
                int next = (j + 1) % lonSegments;
                Vector3 v00 = rings[i, j];
                Vector3 v01 = rings[i, next];
                Vector3 v10 = rings[i + 1, j];
                Vector3 v11 = rings[i + 1, next];

                tris.Add(new Tri(v00, v10, v11));
                tris.Add(new Tri(v00, v11, v01));
            }
        }

        // South polar cap
        int lastRing = latSegments - 2;
        for (int j = 0; j < lonSegments; j++)
        {
            int next = (j + 1) % lonSegments;
            tris.Add(new Tri(southPole, rings[lastRing, next], rings[lastRing, j]));
        }

        return tris;
    }

    /// <summary>
    /// Generates an L-shaped prism extruded along the Z axis, centred at Z = 0.
    /// Both arms have outer length <paramref name="armLength"/> and width <paramref name="armWidth"/>.
    /// Exact analytical volume: <c>V = thickness * armWidth * (2 * armLength - armWidth)</c>.
    /// </summary>
    public static List<Tri> LBracket(float armLength, float armWidth, float thickness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(armLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(armWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);
        if (armWidth >= armLength)
        {
            throw new ArgumentOutOfRangeException(nameof(armWidth), "Arm width must be strictly less than arm length.");
        }

        float zBot = -thickness * 0.5f;
        float zTop = +thickness * 0.5f;

        Vector3[] bot =
        [
            new(0f, 0f, zBot),
            new(armLength, 0f, zBot),
            new(armLength, armWidth, zBot),
            new(armWidth, armWidth, zBot),
            new(armWidth, armLength, zBot),
            new(0f, armLength, zBot)
        ];

        Vector3[] top =
        [
            new(0f, 0f, zTop),
            new(armLength, 0f, zTop),
            new(armLength, armWidth, zTop),
            new(armWidth, armWidth, zTop),
            new(armWidth, armLength, zTop),
            new(0f, armLength, zTop)
        ];

        List<Tri> tris = new(20)
        {
            // Top cap (+Z)
            new(top[0], top[1], top[3]),
            new(top[1], top[2], top[3]),
            new(top[0], top[3], top[5]),
            new(top[3], top[4], top[5]),

            // Bottom cap (-Z)
            new(bot[0], bot[3], bot[1]),
            new(bot[1], bot[3], bot[2]),
            new(bot[0], bot[5], bot[3]),
            new(bot[3], bot[5], bot[4])
        };

        // 6 perimeter walls
        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            tris.Add(new Tri(bot[i], bot[next], top[next]));
            tris.Add(new Tri(bot[i], top[next], top[i]));
        }

        return tris;
    }

    /// <summary>
    /// Transforms all triangle vertices by the given 4x4 matrix.
    /// </summary>
    public static List<Tri> Transform(IEnumerable<Tri> tris, Matrix4x4 m)
    {
        ArgumentNullException.ThrowIfNull(tris);
        List<Tri> result = new();
        foreach (Tri t in tris)
        {
            result.Add(new Tri(
                Vector3.Transform(t.A, m),
                Vector3.Transform(t.B, m),
                Vector3.Transform(t.C, m)));
        }
        return result;
    }

    /// <summary>
    /// Returns a copy of the triangle list without the triangle at <paramref name="index"/>.
    /// </summary>
    public static List<Tri> RemoveTriangle(List<Tri> tris, int index)
    {
        ArgumentNullException.ThrowIfNull(tris);
        List<Tri> copy = new(tris);
        copy.RemoveAt(index);
        return copy;
    }

    /// <summary>
    /// Returns a copy of the triangle list with the winding of the triangle at <paramref name="index"/> reversed.
    /// </summary>
    public static List<Tri> FlipTriangle(List<Tri> tris, int index)
    {
        ArgumentNullException.ThrowIfNull(tris);
        List<Tri> copy = new(tris);
        Tri t = copy[index];
        copy[index] = new Tri(t.A, t.C, t.B);
        return copy;
    }

    /// <summary>
    /// Computes the exact signed volume of the mesh as the sum of signed tetrahedron volumes in double precision.
    /// </summary>
    public static double SignedVolume(IEnumerable<Tri> tris)
    {
        ArgumentNullException.ThrowIfNull(tris);
        double sum = 0.0;
        foreach (Tri t in tris)
        {
            double ax = t.A.X, ay = t.A.Y, az = t.A.Z;
            double bx = t.B.X, by = t.B.Y, bz = t.B.Z;
            double cx = t.C.X, cy = t.C.Y, cz = t.C.Z;

            double crossX = by * cz - bz * cy;
            double crossY = bz * cx - bx * cz;
            double crossZ = bx * cy - by * cx;

            sum += ax * crossX + ay * crossY + az * crossZ;
        }
        return sum / 6.0;
    }

    /// <summary>
    /// Writes triangles to a binary STL stream (80-byte header, uint32 count, 50 bytes per triangle, little-endian).
    /// </summary>
    public static void WriteBinary(Stream s, IEnumerable<Tri> tris, bool headerStartsWithSolid = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(tris);

        IReadOnlyList<Tri> list = tris as IReadOnlyList<Tri> ?? tris.ToList();

        Span<byte> header = stackalloc byte[80];
        header.Clear();
        ReadOnlySpan<byte> prefix = headerStartsWithSolid
            ? "solid binary_stl"u8
            : "Mycel Binary STL"u8;
        prefix.CopyTo(header);
        s.Write(header);

        Span<byte> countBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(countBytes, (uint)list.Count);
        s.Write(countBytes);

        Span<byte> record = stackalloc byte[50];
        record.Clear();

        foreach (Tri t in list)
        {
            Vector3 cross = Vector3.Cross(t.B - t.A, t.C - t.A);
            float len = cross.Length();
            Vector3 normal = len > 0f ? cross / len : Vector3.Zero;

            WriteVector3LittleEndian(record[0..12], normal);
            WriteVector3LittleEndian(record[12..24], t.A);
            WriteVector3LittleEndian(record[24..36], t.B);
            WriteVector3LittleEndian(record[36..48], t.C);
            BinaryPrimitives.WriteUInt16LittleEndian(record[48..50], 0);

            s.Write(record);
        }
    }

    /// <summary>
    /// Writes triangles to an ASCII STL stream using <see cref="CultureInfo.InvariantCulture"/> and "R" float formatting.
    /// </summary>
    public static void WriteAscii(Stream s, IEnumerable<Tri> tris)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(tris);

        using StreamWriter writer = new(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        writer.NewLine = "\n";
        writer.WriteLine("solid mesh");

        foreach (Tri t in tris)
        {
            Vector3 cross = Vector3.Cross(t.B - t.A, t.C - t.A);
            float len = cross.Length();
            Vector3 n = len > 0f ? cross / len : Vector3.Zero;

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  facet normal {n.X:R} {n.Y:R} {n.Z:R}"));
            writer.WriteLine("    outer loop");
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"      vertex {t.A.X:R} {t.A.Y:R} {t.A.Z:R}"));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"      vertex {t.B.X:R} {t.B.Y:R} {t.B.Z:R}"));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"      vertex {t.C.X:R} {t.C.Y:R} {t.C.Z:R}"));
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }

        writer.WriteLine("endsolid mesh");
        writer.Flush();
    }

    private static void WriteVector3LittleEndian(Span<byte> dest, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(dest[0..4], v.X);
        BinaryPrimitives.WriteSingleLittleEndian(dest[4..8], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(dest[8..12], v.Z);
    }
}
