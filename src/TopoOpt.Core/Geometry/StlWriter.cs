using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace TopoOpt.Core.Geometry;

/// <summary>
/// Exports an <see cref="IndexedMesh"/> to binary STL format.
/// </summary>
public static class StlWriter
{
    public static void WriteBinary(Stream stream, IndexedMesh mesh, string? customHeader = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mesh);

        Span<byte> header = stackalloc byte[80];
        header.Clear();
        if (!string.IsNullOrEmpty(customHeader))
        {
            byte[] customBytes = System.Text.Encoding.ASCII.GetBytes(customHeader);
            int copyLen = Math.Min(customBytes.Length, 80);
            customBytes.AsSpan(0, copyLen).CopyTo(header);
        }
        else
        {
            "TopoOpt Binary STL (mm)"u8.CopyTo(header);
        }
        stream.Write(header);

        int triCount = mesh.TriangleCount;
        Span<byte> countBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(countBytes, (uint)triCount);
        stream.Write(countBytes);

        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<int> indices = mesh.Indices.Span;
        ReadOnlySpan<Vector3> normals = mesh.TriangleNormals.Span;

        Span<byte> record = stackalloc byte[50];
        record.Clear();

        for (int t = 0; t < triCount; t++)
        {
            Vector3 n = normals[t];
            Vector3 a = positions[indices[t * 3]];
            Vector3 b = positions[indices[t * 3 + 1]];
            Vector3 c = positions[indices[t * 3 + 2]];

            WriteVector3(record[0..12], n);
            WriteVector3(record[12..24], a);
            WriteVector3(record[24..36], b);
            WriteVector3(record[36..48], c);
            BinaryPrimitives.WriteUInt16LittleEndian(record[48..50], 0);

            stream.Write(record);
        }
    }

    private static void WriteVector3(Span<byte> dest, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(dest[0..4], v.X);
        BinaryPrimitives.WriteSingleLittleEndian(dest[4..8], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(dest[8..12], v.Z);
    }
}

