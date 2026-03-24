using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Models
{
    /// <summary>
    /// Parser for Xbox 360 mesh/vertex buffer data.
    /// Handles common vertex formats and endian-swapped vertex/index buffers.
    /// </summary>
    public class Xbox360MeshParser : IAssetParser
    {
        public AssetType Type => AssetType.Model;

        // Common vertex format element types
        public enum VertexElementType
        {
            Float1, Float2, Float3, Float4,
            Half2, Half4,
            UByte4, UByte4N,
            Short2, Short2N, Short4, Short4N,
            UShort2N, UShort4N,
            Dec3N, UDec4N,
            Color
        }

        public enum VertexElementUsage
        {
            Position, Normal, Tangent, Binormal,
            TexCoord0, TexCoord1, TexCoord2, TexCoord3,
            Color0, Color1,
            BlendWeight, BlendIndex
        }

        public struct VertexElement
        {
            public VertexElementType Type;
            public VertexElementUsage Usage;
            public int Offset;
        }

        public bool CanParse(Stream stream, string fileName)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext == ".mesh" || ext == ".model" || ext == ".geo" || ext == ".xmodel";
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            var entry = new AssetEntry(fileName, AssetType.Model)
            {
                SourcePath = fileName,
                Size = stream.Length,
                FormatName = "Xbox 360 Mesh"
            };

            // Try to detect and parse mesh format
            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);

                // Try common mesh header patterns
                uint magic = reader.ReadUInt32();
                reader.Seek(0);

                MeshData mesh = TryParseMesh(reader, stream.Length);
                if (mesh != null)
                {
                    entry.Metadata["VertexCount"] = mesh.Vertices.Count;
                    entry.Metadata["IndexCount"] = mesh.Indices.Count;
                    entry.Metadata["SubMeshCount"] = mesh.SubMeshes.Count;
                    entry.Metadata["HasNormals"] = mesh.Normals.Count > 0;
                    entry.Metadata["HasUVs"] = mesh.UVs.Count > 0;
                    entry.Metadata["HasSkeleton"] = mesh.Bones.Count > 0;
                }
            }

            return entry;
        }

        /// <summary>
        /// Attempt to parse mesh data from a stream using common Xbox 360 mesh layouts.
        /// </summary>
        public MeshData TryParseMesh(EndianBinaryReader reader, long streamLength)
        {
            var mesh = new MeshData();

            try
            {
                // Common header: vertex count, index count, vertex stride
                uint vertexCount = reader.ReadUInt32();
                uint indexCount = reader.ReadUInt32();
                uint vertexStride = reader.ReadUInt32();

                // Sanity checks
                if (vertexCount == 0 || vertexCount > 10000000) return null;
                if (indexCount == 0 || indexCount > 50000000) return null;
                if (vertexStride == 0 || vertexStride > 256) return null;

                long expectedSize = reader.Position + (vertexCount * vertexStride) + (indexCount * 2);
                if (expectedSize > streamLength * 2) return null;

                // Read vertices
                for (int i = 0; i < vertexCount; i++)
                {
                    long vertStart = reader.Position;

                    if (vertexStride >= 12)
                    {
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        mesh.Vertices.Add(new Vector3f(x, y, z));
                    }

                    if (vertexStride >= 24)
                    {
                        float nx = reader.ReadSingle();
                        float ny = reader.ReadSingle();
                        float nz = reader.ReadSingle();
                        mesh.Normals.Add(new Vector3f(nx, ny, nz));
                    }

                    if (vertexStride >= 32)
                    {
                        float u = reader.ReadSingle();
                        float v = reader.ReadSingle();
                        mesh.UVs.Add(new Vector2f(u, v));
                    }

                    // Skip remaining stride bytes
                    long bytesRead = reader.Position - vertStart;
                    if (bytesRead < vertexStride)
                        reader.Skip(vertexStride - bytesRead);
                }

                // Read indices (16-bit)
                for (int i = 0; i < indexCount; i++)
                {
                    mesh.Indices.Add(reader.ReadUInt16());
                }

                mesh.SubMeshes.Add(new SubMesh
                {
                    Name = "default",
                    IndexStart = 0,
                    IndexCount = (int)indexCount,
                    MaterialIndex = 0
                });

                return mesh;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parse vertex data from a raw vertex buffer with known format.
        /// </summary>
        public static MeshData ParseVertexBuffer(byte[] vertexData, byte[] indexData,
            int vertexStride, List<VertexElement> elements, bool bigEndian = true)
        {
            var mesh = new MeshData();
            int vertexCount = vertexData.Length / vertexStride;

            using (var vbStream = new MemoryStream(vertexData))
            using (var vbReader = new EndianBinaryReader(vbStream, bigEndian, leaveOpen: true))
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    long vertexBase = v * vertexStride;

                    foreach (var element in elements)
                    {
                        vbReader.Seek(vertexBase + element.Offset);

                        switch (element.Usage)
                        {
                            case VertexElementUsage.Position:
                                mesh.Vertices.Add(ReadVector3(vbReader, element.Type));
                                break;
                            case VertexElementUsage.Normal:
                                mesh.Normals.Add(ReadVector3(vbReader, element.Type));
                                break;
                            case VertexElementUsage.TexCoord0:
                                mesh.UVs.Add(ReadVector2(vbReader, element.Type));
                                break;
                        }
                    }
                }
            }

            // Parse index buffer
            if (indexData != null)
            {
                using (var ibStream = new MemoryStream(indexData))
                using (var ibReader = new EndianBinaryReader(ibStream, bigEndian, leaveOpen: true))
                {
                    int indexCount = indexData.Length / 2;
                    for (int i = 0; i < indexCount; i++)
                    {
                        mesh.Indices.Add(ibReader.ReadUInt16());
                    }
                }
            }

            return mesh;
        }

        private static Vector3f ReadVector3(EndianBinaryReader reader, VertexElementType type)
        {
            switch (type)
            {
                case VertexElementType.Float3:
                    return new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case VertexElementType.Half2:
                    return new Vector3f(HalfToFloat(reader.ReadUInt16()), HalfToFloat(reader.ReadUInt16()), 0);
                default:
                    return new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
        }

        private static Vector2f ReadVector2(EndianBinaryReader reader, VertexElementType type)
        {
            switch (type)
            {
                case VertexElementType.Float2:
                    return new Vector2f(reader.ReadSingle(), reader.ReadSingle());
                case VertexElementType.Half2:
                    return new Vector2f(HalfToFloat(reader.ReadUInt16()), HalfToFloat(reader.ReadUInt16()));
                case VertexElementType.Short2N:
                    return new Vector2f(reader.ReadInt16() / 32767f, reader.ReadInt16() / 32767f);
                default:
                    return new Vector2f(reader.ReadSingle(), reader.ReadSingle());
            }
        }

        private static float HalfToFloat(ushort half)
        {
            int sign = (half >> 15) & 1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            if (exponent == 0)
            {
                if (mantissa == 0) return sign == 1 ? -0f : 0f;
                // Denormalized
                float value = mantissa / 1024f * (float)Math.Pow(2, -14);
                return sign == 1 ? -value : value;
            }
            if (exponent == 31)
            {
                return mantissa == 0 ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
            }

            float result = (float)((1 + mantissa / 1024.0) * Math.Pow(2, exponent - 15));
            return sign == 1 ? -result : result;
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            using (var reader = new EndianBinaryReader(source, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);
                MeshData mesh = TryParseMesh(reader, source.Length);

                if (mesh == null || mesh.Vertices.Count == 0)
                    return ExportResult.Failed("Could not parse mesh data.");

                string format = options?.PreferredFormat?.ToLowerInvariant() ?? "obj";

                if (format == "obj")
                {
                    string objPath = Path.ChangeExtension(outputPath, ".obj");
                    string objData = ModelExporter.ExportToOBJ(mesh);
                    File.WriteAllText(objPath, objData);
                    return ExportResult.Succeeded(objPath, objData.Length);
                }

                // Default to OBJ
                string defaultPath = Path.ChangeExtension(outputPath, ".obj");
                string data = ModelExporter.ExportToOBJ(mesh);
                File.WriteAllText(defaultPath, data);
                return ExportResult.Succeeded(defaultPath, data.Length);
            }
        }
    }
}
