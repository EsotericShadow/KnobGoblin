using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using KnobForge.Core;
using KnobForge.Core.Scene;


namespace KnobForge.Rendering.GPU;

public static partial class ImportedStlCollarMeshBuilder
{
    private static bool TryReadBinaryGlb(string path, out List<Vector3> positions, out List<uint> indices)
    {
        positions = new List<Vector3>();
        indices = new List<uint>();

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        if (fileBytes.Length < 20)
        {
            return false;
        }

        using var stream = new MemoryStream(fileBytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != GlbMagic)
        {
            return false;
        }

        if (reader.ReadUInt32() != 2u)
        {
            return false;
        }

        uint declaredLength = reader.ReadUInt32();
        if (declaredLength < 20u || declaredLength > fileBytes.Length)
        {
            return false;
        }

        string? jsonChunkText = null;
        byte[]? binaryChunk = null;
        while ((stream.Position + 8) <= declaredLength)
        {
            uint chunkLength = reader.ReadUInt32();
            uint chunkType = reader.ReadUInt32();
            if (chunkLength > int.MaxValue || (stream.Position + chunkLength) > declaredLength)
            {
                return false;
            }

            byte[] chunkData = reader.ReadBytes((int)chunkLength);
            if (chunkData.Length != (int)chunkLength)
            {
                return false;
            }

            if (chunkType == GlbJsonChunkType)
            {
                jsonChunkText = Encoding.UTF8.GetString(chunkData)
                    .TrimEnd('\0', '\t', '\r', '\n', ' ');
            }
            else if (chunkType == GlbBinChunkType && binaryChunk is null)
            {
                binaryChunk = chunkData;
            }
        }

        if (string.IsNullOrWhiteSpace(jsonChunkText) || binaryChunk is null)
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(jsonChunkText);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("meshes", out JsonElement meshesElement) ||
            meshesElement.ValueKind != JsonValueKind.Array ||
            meshesElement.GetArrayLength() == 0)
        {
            return false;
        }

        if (!root.TryGetProperty("accessors", out JsonElement accessorsElement) ||
            accessorsElement.ValueKind != JsonValueKind.Array ||
            accessorsElement.GetArrayLength() == 0)
        {
            return false;
        }

        if (!root.TryGetProperty("bufferViews", out JsonElement bufferViewsElement) ||
            bufferViewsElement.ValueKind != JsonValueKind.Array ||
            bufferViewsElement.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (JsonElement mesh in meshesElement.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitivesElement) ||
                primitivesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement primitive in primitivesElement.EnumerateArray())
            {
                int mode = 4; // TRIANGLES
                if (primitive.TryGetProperty("mode", out JsonElement modeElement) &&
                    modeElement.ValueKind == JsonValueKind.Number &&
                    modeElement.TryGetInt32(out int parsedMode))
                {
                    mode = parsedMode;
                }

                if (mode != 4)
                {
                    continue;
                }

                if (!primitive.TryGetProperty("attributes", out JsonElement attributesElement) ||
                    attributesElement.ValueKind != JsonValueKind.Object ||
                    !attributesElement.TryGetProperty("POSITION", out JsonElement positionAccessorElement) ||
                    !positionAccessorElement.TryGetInt32(out int positionAccessorIndex))
                {
                    continue;
                }

                if (!TryReadAccessorVector3(
                        accessorsElement,
                        bufferViewsElement,
                        binaryChunk,
                        positionAccessorIndex,
                        out Vector3[] primitivePositions) ||
                    primitivePositions.Length == 0)
                {
                    continue;
                }

                int baseVertex = positions.Count;
                for (int i = 0; i < primitivePositions.Length; i++)
                {
                    positions.Add(primitivePositions[i]);
                }

                if (primitive.TryGetProperty("indices", out JsonElement indicesAccessorElement) &&
                    indicesAccessorElement.TryGetInt32(out int indicesAccessorIndex) &&
                    TryReadAccessorIndices(
                        accessorsElement,
                        bufferViewsElement,
                        binaryChunk,
                        indicesAccessorIndex,
                        out uint[] primitiveIndices) &&
                    primitiveIndices.Length >= 3)
                {
                    for (int i = 0; i + 2 < primitiveIndices.Length; i += 3)
                    {
                        uint i0 = primitiveIndices[i + 0];
                        uint i1 = primitiveIndices[i + 1];
                        uint i2 = primitiveIndices[i + 2];
                        if (i0 >= primitivePositions.Length ||
                            i1 >= primitivePositions.Length ||
                            i2 >= primitivePositions.Length ||
                            i0 == i1 || i1 == i2 || i2 == i0)
                        {
                            continue;
                        }

                        indices.Add((uint)(baseVertex + (int)i0));
                        indices.Add((uint)(baseVertex + (int)i1));
                        indices.Add((uint)(baseVertex + (int)i2));
                    }
                }
                else
                {
                    for (int i = 0; i + 2 < primitivePositions.Length; i += 3)
                    {
                        indices.Add((uint)(baseVertex + i + 0));
                        indices.Add((uint)(baseVertex + i + 1));
                        indices.Add((uint)(baseVertex + i + 2));
                    }
                }
            }
        }

        return positions.Count > 0 && indices.Count >= 3;
    }

    private static bool TryReadAccessorVector3(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        byte[] bufferBytes,
        int accessorIndex,
        out Vector3[] vectors)
    {
        vectors = Array.Empty<Vector3>();
        if (!TryResolveAccessorView(accessorsElement, bufferViewsElement, bufferBytes.Length, accessorIndex, out AccessorView view))
        {
            return false;
        }

        if (!string.Equals(view.Type, "VEC3", StringComparison.Ordinal) ||
            view.ComponentType != 5126)
        {
            return false;
        }

        int stride = view.ByteStride > 0 ? view.ByteStride : 12;
        if (stride < 12)
        {
            return false;
        }

        long lastVectorStart = view.DataOffset + ((long)(view.Count - 1) * stride);
        long accessorEnd = view.DataOffset + view.ByteLength;
        if (lastVectorStart < 0 ||
            (lastVectorStart + 12) > bufferBytes.Length ||
            (lastVectorStart + 12) > accessorEnd)
        {
            return false;
        }

        vectors = new Vector3[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int offset = view.DataOffset + (i * stride);
            float x = BitConverter.ToSingle(bufferBytes, offset + 0);
            float y = BitConverter.ToSingle(bufferBytes, offset + 4);
            float z = BitConverter.ToSingle(bufferBytes, offset + 8);
            vectors[i] = new Vector3(x, y, z);
        }

        return true;
    }

    private static bool TryReadAccessorIndices(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        byte[] bufferBytes,
        int accessorIndex,
        out uint[] values)
    {
        values = Array.Empty<uint>();
        if (!TryResolveAccessorView(accessorsElement, bufferViewsElement, bufferBytes.Length, accessorIndex, out AccessorView view))
        {
            return false;
        }

        if (!string.Equals(view.Type, "SCALAR", StringComparison.Ordinal))
        {
            return false;
        }

        int componentSize = view.ComponentType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => 0
        };
        if (componentSize == 0)
        {
            return false;
        }

        int stride = view.ByteStride > 0 ? view.ByteStride : componentSize;
        if (stride < componentSize)
        {
            return false;
        }

        long lastValueStart = view.DataOffset + ((long)(view.Count - 1) * stride);
        long accessorEnd = view.DataOffset + view.ByteLength;
        if (lastValueStart < 0 ||
            (lastValueStart + componentSize) > bufferBytes.Length ||
            (lastValueStart + componentSize) > accessorEnd)
        {
            return false;
        }

        values = new uint[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int offset = view.DataOffset + (i * stride);
            values[i] = view.ComponentType switch
            {
                5121 => bufferBytes[offset],
                5123 => BitConverter.ToUInt16(bufferBytes, offset),
                5125 => BitConverter.ToUInt32(bufferBytes, offset),
                _ => 0u
            };
        }

        return true;
    }

    private static bool TryResolveAccessorView(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        int bufferLength,
        int accessorIndex,
        out AccessorView view)
    {
        view = default;
        if (!TryGetArrayElement(accessorsElement, accessorIndex, out JsonElement accessorElement) ||
            accessorElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Sparse accessors are uncommon for static meshes; keep importer strict.
        if (accessorElement.TryGetProperty("sparse", out _))
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("bufferView", out JsonElement accessorBufferViewElement) ||
            !accessorBufferViewElement.TryGetInt32(out int bufferViewIndex) ||
            !TryGetArrayElement(bufferViewsElement, bufferViewIndex, out JsonElement bufferViewElement) ||
            bufferViewElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("count", out JsonElement countElement) ||
            !countElement.TryGetInt32(out int count) ||
            count <= 0)
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("componentType", out JsonElement componentTypeElement) ||
            !componentTypeElement.TryGetInt32(out int componentType))
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("type", out JsonElement typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? type = typeElement.GetString();
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        if (bufferViewElement.TryGetProperty("buffer", out JsonElement bufferIndexElement) &&
            bufferIndexElement.TryGetInt32(out int bufferIndex) &&
            bufferIndex != 0)
        {
            // GLB uses the first (and usually only) binary buffer chunk.
            return false;
        }

        int bufferViewOffset = 0;
        if (bufferViewElement.TryGetProperty("byteOffset", out JsonElement bufferViewOffsetElement) &&
            bufferViewOffsetElement.TryGetInt32(out int parsedBufferViewOffset))
        {
            bufferViewOffset = parsedBufferViewOffset;
        }

        if (!bufferViewElement.TryGetProperty("byteLength", out JsonElement bufferViewLengthElement) ||
            !bufferViewLengthElement.TryGetInt32(out int bufferViewLength) ||
            bufferViewLength <= 0)
        {
            return false;
        }

        int accessorOffset = 0;
        if (accessorElement.TryGetProperty("byteOffset", out JsonElement accessorOffsetElement) &&
            accessorOffsetElement.TryGetInt32(out int parsedAccessorOffset))
        {
            accessorOffset = parsedAccessorOffset;
        }

        int byteStride = 0;
        if (bufferViewElement.TryGetProperty("byteStride", out JsonElement byteStrideElement) &&
            byteStrideElement.TryGetInt32(out int parsedByteStride))
        {
            byteStride = parsedByteStride;
        }

        int dataOffset = bufferViewOffset + accessorOffset;
        if (dataOffset < 0 || dataOffset >= bufferLength)
        {
            return false;
        }

        if (dataOffset > (bufferViewOffset + bufferViewLength))
        {
            return false;
        }

        view = new AccessorView(
            DataOffset: dataOffset,
            Count: count,
            ComponentType: componentType,
            Type: type,
            ByteStride: byteStride,
            ByteLength: bufferViewLength - accessorOffset);
        return view.ByteLength > 0;
    }

    private static bool TryGetArrayElement(JsonElement arrayElement, int index, out JsonElement value)
    {
        value = default;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        value = arrayElement[index];
        return true;
    }
}
