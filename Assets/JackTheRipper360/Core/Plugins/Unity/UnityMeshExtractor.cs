using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unity
{
    /// <summary>
    /// Specialized extractor for Unity Mesh serialized objects from Xbox 360 asset bundles.
    /// Parses vertex data, index buffers, submeshes, bind poses, and bone weights,
    /// then exports to OBJ or FBX ASCII format.
    /// </summary>
    public static class UnityMeshExtractor
    {
        #region Channel Format Constants

        /// <summary>
        /// Vertex channel data formats.
        /// </summary>
        public enum ChannelFormat : byte
        {
            Float = 0,
            Float16 = 1,
            UNorm8 = 2,
            SNorm8 = 3,
            UNorm16 = 4,
            SNorm16 = 5,
            UInt8 = 6,
            SInt8 = 7,
            UInt16 = 8,
            SInt16 = 9,
            UInt32 = 10,
            SInt32 = 11
        }

        /// <summary>
        /// Well-known vertex attribute semantics.
        /// </summary>
        public enum VertexAttribute : byte
        {
            Position = 0,
            Normal = 1,
            Tangent = 2,
            Color = 3,
            TexCoord0 = 4,
            TexCoord1 = 5,
            TexCoord2 = 6,
            TexCoord3 = 7,
            TexCoord4 = 8,
            TexCoord5 = 9,
            TexCoord6 = 10,
            TexCoord7 = 11,
            BlendWeight = 12,
            BlendIndices = 13
        }

        #endregion

        #region Data Structures

        public class ChannelInfo
        {
            public byte Stream;
            public byte Offset;
            public ChannelFormat Format;
            public byte Dimension;
        }

        public class SubMesh
        {
            public uint IndexStart;
            public uint IndexCount;
            public int Topology; // 0 = Triangles, 1 = TriStrip, etc.
            public uint VertexStart;
            public uint VertexCount;
            public float AABBCenterX, AABBCenterY, AABBCenterZ;
            public float AABBExtentX, AABBExtentY, AABBExtentZ;
        }

        public class VertexData
        {
            public int VertexCount;
            public List<ChannelInfo> Channels = new List<ChannelInfo>();
            public byte[] RawVertexBuffer;
            public int[] StreamStrides;
        }

        public class BoneWeight
        {
            public float[] Weights = new float[4];
            public int[] BoneIndices = new int[4];
        }

        public class ParsedMesh
        {
            public string Name = "";
            public List<SubMesh> SubMeshes = new List<SubMesh>();
            public VertexData Vertices;
            public bool IndexFormat32Bit;
            public byte[] RawIndexBuffer;
            public List<float[]> BindPoses = new List<float[]>(); // 4x4 matrices
            public List<BoneWeight> BoneWeights = new List<BoneWeight>();
            public float BoundsCenterX, BoundsCenterY, BoundsCenterZ;
            public float BoundsExtentX, BoundsExtentY, BoundsExtentZ;

            // Decoded data
            public float[] Positions;
            public float[] Normals;
            public float[] Tangents;
            public float[] Colors;
            public float[] UV0;
            public float[] UV1;
            public int[] Indices;

            public bool HasNormals => Normals != null && Normals.Length > 0;
            public bool HasTangents => Tangents != null && Tangents.Length > 0;
            public bool HasColors => Colors != null && Colors.Length > 0;
            public bool HasUVs => UV0 != null && UV0.Length > 0;
            public bool HasSkeleton => BoneWeights.Count > 0 && BindPoses.Count > 0;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Exports a Unity Mesh asset to OBJ or FBX format.
        /// </summary>
        /// <param name="entry">The asset entry describing the mesh.</param>
        /// <param name="source">Stream positioned at or containing the mesh data.</param>
        /// <param name="outputPath">Desired output file path (extension may be changed).</param>
        /// <param name="options">Export options controlling format and output directory.</param>
        /// <returns>Result indicating success or failure with output path.</returns>
        public static ExportResult ExportMesh(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                source.Seek(entry.Offset, SeekOrigin.Begin);

                // Xbox 360 uses big-endian
                using (var reader = new EndianBinaryReader(source, bigEndian: true, leaveOpen: true))
                {
                    reader.Seek(entry.Offset);
                    ParsedMesh mesh = ParseMeshData(reader, entry.Size);

                    if (mesh.Vertices == null || mesh.Vertices.VertexCount == 0)
                        return ExportResult.Failed("Mesh contains no vertex data.");

                    DecodeVertexData(mesh);
                    DecodeIndexBuffer(mesh);

                    // Populate metadata on the entry
                    PopulateMetadata(entry, mesh);

                    // Determine output format
                    string format = options?.PreferredFormat?.ToLowerInvariant() ?? "obj";
                    string dir = options?.OutputDirectory ?? Path.GetDirectoryName(outputPath) ?? ".";
                    string baseName = Path.GetFileNameWithoutExtension(outputPath);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    long bytesWritten;
                    string finalPath;

                    if (format == "fbx")
                    {
                        finalPath = Path.Combine(dir, baseName + ".fbx");
                        bytesWritten = WriteFbxAscii(mesh, finalPath);
                    }
                    else
                    {
                        finalPath = Path.Combine(dir, baseName + ".obj");
                        bytesWritten = WriteObj(mesh, finalPath);
                    }

                    return ExportResult.Succeeded(finalPath, bytesWritten);
                }
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Failed to export mesh: {ex.Message}");
            }
        }

        #endregion

        #region Mesh Parsing

        private static ParsedMesh ParseMeshData(EndianBinaryReader reader, long maxSize)
        {
            long startPos = reader.Position;
            var mesh = new ParsedMesh();

            // Name: length-prefixed string (int32 length + chars + 4-byte alignment)
            int nameLength = reader.ReadInt32();
            if (nameLength > 0 && nameLength < 4096)
            {
                mesh.Name = reader.ReadString(nameLength);
                reader.Align(4);
            }

            // SubMesh array
            int subMeshCount = reader.ReadInt32();
            if (subMeshCount < 0 || subMeshCount > 65536)
                subMeshCount = 0;

            for (int i = 0; i < subMeshCount; i++)
            {
                var sub = new SubMesh();
                sub.IndexStart = reader.ReadUInt32();
                sub.IndexCount = reader.ReadUInt32();
                sub.Topology = reader.ReadInt32();
                reader.Skip(4); // baseVertex (Unity 2017.3+)
                sub.VertexStart = reader.ReadUInt32();
                sub.VertexCount = reader.ReadUInt32();

                // AABB
                sub.AABBCenterX = reader.ReadSingle();
                sub.AABBCenterY = reader.ReadSingle();
                sub.AABBCenterZ = reader.ReadSingle();
                sub.AABBExtentX = reader.ReadSingle();
                sub.AABBExtentY = reader.ReadSingle();
                sub.AABBExtentZ = reader.ReadSingle();

                mesh.SubMeshes.Add(sub);
            }

            // Blend shape data (skip for now)
            // Shape vertices
            int shapeVertCount = reader.ReadInt32();
            if (shapeVertCount > 0)
                reader.Skip(shapeVertCount * 40); // position(12) + normal(12) + tangent(12) + index(4)

            // Shapes
            int shapeCount = reader.ReadInt32();
            if (shapeCount > 0)
            {
                for (int i = 0; i < shapeCount; i++)
                {
                    reader.Skip(8); // firstVertex + vertexCount
                    reader.Skip(1); // hasNormals
                    reader.Skip(1); // hasTangents
                    reader.Align(4);
                }
            }

            // Shape channels
            int channelCount = reader.ReadInt32();
            for (int i = 0; i < channelCount; i++)
            {
                int chNameLen = reader.ReadInt32();
                if (chNameLen > 0 && chNameLen < 4096)
                {
                    reader.Skip(chNameLen);
                    reader.Align(4);
                }
                int frameCount = reader.ReadInt32();
                reader.Skip(frameCount * 8); // weight(4) + full weight(4) per frame
            }

            // Bind poses
            int bindPoseCount = reader.ReadInt32();
            if (bindPoseCount > 0 && bindPoseCount < 65536)
            {
                for (int i = 0; i < bindPoseCount; i++)
                {
                    float[] matrix = new float[16];
                    for (int j = 0; j < 16; j++)
                        matrix[j] = reader.ReadSingle();
                    mesh.BindPoses.Add(matrix);
                }
            }

            // Bone name hashes (skip)
            int boneHashCount = reader.ReadInt32();
            if (boneHashCount > 0)
                reader.Skip(boneHashCount * 4);

            // Root bone name hash
            reader.Skip(4);

            // Mesh compression (byte), stream compression (byte), isReadable, keepVertices, keepIndices
            reader.Skip(5);
            reader.Align(4);

            // Index buffer
            int indexBufferSize = reader.ReadInt32();
            if (indexBufferSize > 0 && indexBufferSize < maxSize)
            {
                mesh.RawIndexBuffer = reader.ReadBytes(indexBufferSize);
                reader.Align(4);
            }

            // Skin data (bone weights)
            int skinCount = reader.ReadInt32();
            if (skinCount > 0 && skinCount < 10000000)
            {
                for (int i = 0; i < skinCount; i++)
                {
                    var bw = new BoneWeight();
                    bw.Weights[0] = reader.ReadSingle();
                    bw.Weights[1] = reader.ReadSingle();
                    bw.Weights[2] = reader.ReadSingle();
                    bw.Weights[3] = reader.ReadSingle();
                    bw.BoneIndices[0] = reader.ReadInt32();
                    bw.BoneIndices[1] = reader.ReadInt32();
                    bw.BoneIndices[2] = reader.ReadInt32();
                    bw.BoneIndices[3] = reader.ReadInt32();
                    mesh.BoneWeights.Add(bw);
                }
            }

            // Index format: 0 = 16-bit, 1 = 32-bit
            mesh.IndexFormat32Bit = reader.ReadInt32() == 1;

            // Vertex data
            mesh.Vertices = new VertexData();
            mesh.Vertices.VertexCount = reader.ReadInt32();

            // Channels
            int vertexChannelCount = reader.ReadInt32();
            for (int i = 0; i < vertexChannelCount; i++)
            {
                var ch = new ChannelInfo();
                ch.Stream = reader.ReadByte();
                ch.Offset = reader.ReadByte();
                ch.Format = (ChannelFormat)reader.ReadByte();
                ch.Dimension = (byte)(reader.ReadByte() & 0x0F);
                mesh.Vertices.Channels.Add(ch);
            }

            // Stream strides
            // Read stream info: offset, size, stride, dividerOp, frequency
            int streamCount = 4; // Unity typically has 4 streams max
            mesh.Vertices.StreamStrides = new int[streamCount];
            for (int i = 0; i < streamCount; i++)
            {
                reader.Skip(4); // channelMask or offset
                reader.Skip(4); // offset or size
                int stride = reader.ReadInt32();
                mesh.Vertices.StreamStrides[i] = stride;
                reader.Skip(4); // dividerOp + frequency or padding
            }

            // Raw vertex buffer
            int vertexDataSize = reader.ReadInt32();
            if (vertexDataSize > 0 && vertexDataSize < maxSize)
            {
                mesh.Vertices.RawVertexBuffer = reader.ReadBytes(vertexDataSize);
            }

            // Bounding box
            mesh.BoundsCenterX = reader.ReadSingle();
            mesh.BoundsCenterY = reader.ReadSingle();
            mesh.BoundsCenterZ = reader.ReadSingle();
            mesh.BoundsExtentX = reader.ReadSingle();
            mesh.BoundsExtentY = reader.ReadSingle();
            mesh.BoundsExtentZ = reader.ReadSingle();

            return mesh;
        }

        #endregion

        #region Vertex Decoding

        private static void DecodeVertexData(ParsedMesh mesh)
        {
            var vd = mesh.Vertices;
            if (vd.RawVertexBuffer == null || vd.VertexCount == 0)
                return;

            int vertexCount = vd.VertexCount;

            for (int chIdx = 0; chIdx < vd.Channels.Count; chIdx++)
            {
                var ch = vd.Channels[chIdx];
                if (ch.Dimension == 0)
                    continue;

                int stride = ch.Stream < vd.StreamStrides.Length ? vd.StreamStrides[ch.Stream] : 0;
                if (stride == 0)
                    continue;

                // Compute stream base offset (sum sizes of prior streams)
                int streamBaseOffset = 0;
                for (int s = 0; s < ch.Stream && s < vd.StreamStrides.Length; s++)
                {
                    streamBaseOffset += vd.StreamStrides[s] * vertexCount;
                }

                int componentCount = ch.Dimension;
                float[] decoded = new float[vertexCount * componentCount];

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = streamBaseOffset + v * stride + ch.Offset;
                    for (int c = 0; c < componentCount; c++)
                    {
                        decoded[v * componentCount + c] = ReadChannelValue(
                            vd.RawVertexBuffer, offset, ch.Format);
                        offset += GetFormatByteSize(ch.Format);
                    }
                }

                var attr = (VertexAttribute)chIdx;
                switch (attr)
                {
                    case VertexAttribute.Position:
                        mesh.Positions = decoded;
                        break;
                    case VertexAttribute.Normal:
                        mesh.Normals = decoded;
                        break;
                    case VertexAttribute.Tangent:
                        mesh.Tangents = decoded;
                        break;
                    case VertexAttribute.Color:
                        mesh.Colors = decoded;
                        break;
                    case VertexAttribute.TexCoord0:
                        mesh.UV0 = decoded;
                        break;
                    case VertexAttribute.TexCoord1:
                        mesh.UV1 = decoded;
                        break;
                }
            }
        }

        private static float ReadChannelValue(byte[] buffer, int offset, ChannelFormat format)
        {
            if (offset + GetFormatByteSize(format) > buffer.Length)
                return 0f;

            switch (format)
            {
                case ChannelFormat.Float:
                {
                    // Big-endian float (Xbox 360)
                    byte[] bytes = new byte[4];
                    bytes[0] = buffer[offset + 3];
                    bytes[1] = buffer[offset + 2];
                    bytes[2] = buffer[offset + 1];
                    bytes[3] = buffer[offset + 0];
                    return BitConverter.ToSingle(bytes, 0);
                }
                case ChannelFormat.Float16:
                {
                    ushort raw = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                    return HalfToFloat(raw);
                }
                case ChannelFormat.UNorm8:
                    return buffer[offset] / 255f;

                case ChannelFormat.SNorm8:
                    return Math.Max((sbyte)buffer[offset] / 127f, -1f);

                case ChannelFormat.UNorm16:
                {
                    ushort raw = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                    return raw / 65535f;
                }
                case ChannelFormat.SNorm16:
                {
                    short raw = (short)((buffer[offset] << 8) | buffer[offset + 1]);
                    return Math.Max(raw / 32767f, -1f);
                }
                case ChannelFormat.UInt8:
                    return buffer[offset];

                case ChannelFormat.SInt8:
                    return (sbyte)buffer[offset];

                case ChannelFormat.UInt16:
                {
                    ushort raw = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                    return raw;
                }
                case ChannelFormat.SInt16:
                {
                    short raw = (short)((buffer[offset] << 8) | buffer[offset + 1]);
                    return raw;
                }
                case ChannelFormat.UInt32:
                {
                    uint raw = (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                                      (buffer[offset + 2] << 8) | buffer[offset + 3]);
                    return raw;
                }
                case ChannelFormat.SInt32:
                {
                    int raw = (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                              (buffer[offset + 2] << 8) | buffer[offset + 3];
                    return raw;
                }
                default:
                    return 0f;
            }
        }

        private static int GetFormatByteSize(ChannelFormat format)
        {
            switch (format)
            {
                case ChannelFormat.Float:
                case ChannelFormat.UInt32:
                case ChannelFormat.SInt32:
                    return 4;
                case ChannelFormat.Float16:
                case ChannelFormat.UNorm16:
                case ChannelFormat.SNorm16:
                case ChannelFormat.UInt16:
                case ChannelFormat.SInt16:
                    return 2;
                case ChannelFormat.UNorm8:
                case ChannelFormat.SNorm8:
                case ChannelFormat.UInt8:
                case ChannelFormat.SInt8:
                    return 1;
                default:
                    return 1;
            }
        }

        private static float HalfToFloat(ushort half)
        {
            int sign = (half >> 15) & 1;
            int exponent = (half >> 10) & 0x1F;
            int mantissa = half & 0x3FF;

            if (exponent == 0)
            {
                if (mantissa == 0)
                    return sign == 1 ? -0f : 0f;
                // Denormalized
                float val = (float)(mantissa / 1024.0 * Math.Pow(2, -14));
                return sign == 1 ? -val : val;
            }
            if (exponent == 31)
            {
                if (mantissa == 0)
                    return sign == 1 ? float.NegativeInfinity : float.PositiveInfinity;
                return float.NaN;
            }

            float result = (float)((1.0 + mantissa / 1024.0) * Math.Pow(2, exponent - 15));
            return sign == 1 ? -result : result;
        }

        #endregion

        #region Index Buffer Decoding

        private static void DecodeIndexBuffer(ParsedMesh mesh)
        {
            if (mesh.RawIndexBuffer == null || mesh.RawIndexBuffer.Length == 0)
                return;

            int indexSize = mesh.IndexFormat32Bit ? 4 : 2;
            int indexCount = mesh.RawIndexBuffer.Length / indexSize;
            mesh.Indices = new int[indexCount];

            for (int i = 0; i < indexCount; i++)
            {
                int offset = i * indexSize;
                if (mesh.IndexFormat32Bit)
                {
                    // Big-endian 32-bit
                    mesh.Indices[i] = (mesh.RawIndexBuffer[offset] << 24) |
                                      (mesh.RawIndexBuffer[offset + 1] << 16) |
                                      (mesh.RawIndexBuffer[offset + 2] << 8) |
                                      mesh.RawIndexBuffer[offset + 3];
                }
                else
                {
                    // Big-endian 16-bit
                    mesh.Indices[i] = (mesh.RawIndexBuffer[offset] << 8) |
                                      mesh.RawIndexBuffer[offset + 1];
                }
            }
        }

        #endregion

        #region Metadata

        private static void PopulateMetadata(AssetEntry entry, ParsedMesh mesh)
        {
            entry.Metadata["MeshName"] = mesh.Name;
            entry.Metadata["VertexCount"] = mesh.Vertices?.VertexCount ?? 0;
            entry.Metadata["IndexCount"] = mesh.Indices?.Length ?? 0;
            entry.Metadata["SubMeshCount"] = mesh.SubMeshes.Count;
            entry.Metadata["HasNormals"] = mesh.HasNormals;
            entry.Metadata["HasUVs"] = mesh.HasUVs;
            entry.Metadata["HasTangents"] = mesh.HasTangents;
            entry.Metadata["HasColors"] = mesh.HasColors;
            entry.Metadata["HasSkeleton"] = mesh.HasSkeleton;
            entry.Metadata["IndexFormat"] = mesh.IndexFormat32Bit ? "32-bit" : "16-bit";
            entry.Metadata["BoundsCenter"] = $"({mesh.BoundsCenterX:F3}, {mesh.BoundsCenterY:F3}, {mesh.BoundsCenterZ:F3})";
            entry.Metadata["BoundsExtent"] = $"({mesh.BoundsExtentX:F3}, {mesh.BoundsExtentY:F3}, {mesh.BoundsExtentZ:F3})";

            if (mesh.BindPoses.Count > 0)
                entry.Metadata["BoneCount"] = mesh.BindPoses.Count;
        }

        #endregion

        #region OBJ Export

        private static long WriteObj(ParsedMesh mesh, string outputPath)
        {
            var ci = CultureInfo.InvariantCulture;

            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# JackTheRipper360 Unity Mesh Extractor");
                writer.WriteLine("# Mesh: {0}", mesh.Name);
                writer.WriteLine("# Vertices: {0}", mesh.Vertices?.VertexCount ?? 0);
                writer.WriteLine("# SubMeshes: {0}", mesh.SubMeshes.Count);
                writer.WriteLine();

                if (mesh.Name.Length > 0)
                    writer.WriteLine("o {0}", mesh.Name);

                // Vertices
                if (mesh.Positions != null)
                {
                    int vertexCount = mesh.Positions.Length / 3;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        writer.WriteLine("v {0} {1} {2}",
                            mesh.Positions[i * 3].ToString("G9", ci),
                            mesh.Positions[i * 3 + 1].ToString("G9", ci),
                            mesh.Positions[i * 3 + 2].ToString("G9", ci));
                    }
                    writer.WriteLine();
                }

                // Normals
                if (mesh.HasNormals)
                {
                    int normalCount = mesh.Normals.Length / 3;
                    for (int i = 0; i < normalCount; i++)
                    {
                        writer.WriteLine("vn {0} {1} {2}",
                            mesh.Normals[i * 3].ToString("G9", ci),
                            mesh.Normals[i * 3 + 1].ToString("G9", ci),
                            mesh.Normals[i * 3 + 2].ToString("G9", ci));
                    }
                    writer.WriteLine();
                }

                // UVs (flip V for OBJ convention)
                if (mesh.HasUVs)
                {
                    int uvCount = mesh.UV0.Length / 2;
                    for (int i = 0; i < uvCount; i++)
                    {
                        float u = mesh.UV0[i * 2];
                        float v = 1.0f - mesh.UV0[i * 2 + 1]; // Flip V
                        writer.WriteLine("vt {0} {1}",
                            u.ToString("G9", ci),
                            v.ToString("G9", ci));
                    }
                    writer.WriteLine();
                }

                // Faces grouped by submesh
                if (mesh.Indices != null)
                {
                    bool hasVn = mesh.HasNormals;
                    bool hasVt = mesh.HasUVs;

                    for (int s = 0; s < mesh.SubMeshes.Count; s++)
                    {
                        var sub = mesh.SubMeshes[s];
                        writer.WriteLine("g submesh_{0}", s);
                        writer.WriteLine("usemtl material_{0}", s);

                        int start = (int)sub.IndexStart;
                        int count = (int)sub.IndexCount;

                        if (sub.Topology == 0) // Triangles
                        {
                            for (int i = start; i + 2 < start + count && i + 2 < mesh.Indices.Length; i += 3)
                            {
                                // OBJ indices are 1-based
                                int a = mesh.Indices[i] + 1;
                                int b = mesh.Indices[i + 1] + 1;
                                int c = mesh.Indices[i + 2] + 1;

                                writer.Write("f ");
                                WriteObjFaceVertex(writer, a, hasVt, hasVn);
                                writer.Write(' ');
                                WriteObjFaceVertex(writer, b, hasVt, hasVn);
                                writer.Write(' ');
                                WriteObjFaceVertex(writer, c, hasVt, hasVn);
                                writer.WriteLine();
                            }
                        }
                        else if (sub.Topology == 1) // Triangle Strip - triangulate
                        {
                            for (int i = start; i + 2 < start + count && i + 2 < mesh.Indices.Length; i++)
                            {
                                int a, b, c;
                                if ((i - start) % 2 == 0)
                                {
                                    a = mesh.Indices[i] + 1;
                                    b = mesh.Indices[i + 1] + 1;
                                    c = mesh.Indices[i + 2] + 1;
                                }
                                else
                                {
                                    a = mesh.Indices[i + 1] + 1;
                                    b = mesh.Indices[i] + 1;
                                    c = mesh.Indices[i + 2] + 1;
                                }

                                // Skip degenerate triangles
                                if (a == b || b == c || a == c)
                                    continue;

                                writer.Write("f ");
                                WriteObjFaceVertex(writer, a, hasVt, hasVn);
                                writer.Write(' ');
                                WriteObjFaceVertex(writer, b, hasVt, hasVn);
                                writer.Write(' ');
                                WriteObjFaceVertex(writer, c, hasVt, hasVn);
                                writer.WriteLine();
                            }
                        }

                        writer.WriteLine();
                    }
                }
            }

            return new FileInfo(outputPath).Length;
        }

        private static void WriteObjFaceVertex(StreamWriter writer, int index, bool hasVt, bool hasVn)
        {
            if (hasVt && hasVn)
                writer.Write("{0}/{1}/{2}", index, index, index);
            else if (hasVt)
                writer.Write("{0}/{1}", index, index);
            else if (hasVn)
                writer.Write("{0}//{1}", index, index);
            else
                writer.Write("{0}", index);
        }

        #endregion

        #region FBX ASCII Export

        private static long WriteFbxAscii(ParsedMesh mesh, string outputPath)
        {
            var ci = CultureInfo.InvariantCulture;
            int vertexCount = mesh.Positions != null ? mesh.Positions.Length / 3 : 0;
            int indexCount = mesh.Indices != null ? mesh.Indices.Length : 0;

            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
            {
                // FBX 7.4 ASCII header
                writer.WriteLine("; FBX 7.4.0 project file");
                writer.WriteLine("; Generated by JackTheRipper360 Unity Mesh Extractor");
                writer.WriteLine("; -------------------------------------------------");
                writer.WriteLine();
                writer.WriteLine("FBXHeaderExtension:  {");
                writer.WriteLine("\tFBXHeaderVersion: 1003");
                writer.WriteLine("\tFBXVersion: 7400");
                writer.WriteLine("\tCreator: \"JackTheRipper360\"");
                writer.WriteLine("}");
                writer.WriteLine();

                // Global settings
                writer.WriteLine("GlobalSettings:  {");
                writer.WriteLine("\tVersion: 1000");
                writer.WriteLine("\tProperties70:  {");
                writer.WriteLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
                writer.WriteLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
                writer.WriteLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
                writer.WriteLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
                writer.WriteLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
                writer.WriteLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
                writer.WriteLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
                writer.WriteLine("\t}");
                writer.WriteLine("}");
                writer.WriteLine();

                // Objects section
                writer.WriteLine("Objects:  {");

                long geometryId = 100000;
                long modelId = 200000;
                string meshName = string.IsNullOrEmpty(mesh.Name) ? "Mesh" : mesh.Name;

                // Geometry node
                writer.WriteLine("\tGeometry: {0}, \"Geometry::{1}\", \"Mesh\" {{", geometryId, meshName);

                // Vertices
                writer.Write("\t\tVertices: *{0} {\n\t\t\ta: ", vertexCount * 3);
                if (mesh.Positions != null)
                {
                    for (int i = 0; i < mesh.Positions.Length; i++)
                    {
                        if (i > 0) writer.Write(',');
                        if (i > 0 && i % 12 == 0) writer.Write("\n\t\t\t");
                        writer.Write(mesh.Positions[i].ToString("G9", ci));
                    }
                }
                writer.WriteLine("\n\t\t}");

                // Polygon vertex indices
                // FBX uses negative (index + 1) for last vertex of each polygon
                writer.Write("\t\tPolygonVertexIndex: *{0} {{\n\t\t\ta: ", indexCount);
                if (mesh.Indices != null)
                {
                    for (int i = 0; i < indexCount; i++)
                    {
                        if (i > 0) writer.Write(',');
                        if (i > 0 && i % 12 == 0) writer.Write("\n\t\t\t");

                        // Every third index is negated and decremented to mark end of triangle
                        if ((i + 1) % 3 == 0)
                            writer.Write((-mesh.Indices[i] - 1).ToString(ci));
                        else
                            writer.Write(mesh.Indices[i].ToString(ci));
                    }
                }
                writer.WriteLine("\n\t\t}");

                // Layer elements
                int layerIndex = 0;

                // Normals
                if (mesh.HasNormals)
                {
                    writer.WriteLine("\t\tLayerElementNormal: 0 {");
                    writer.WriteLine("\t\t\tVersion: 101");
                    writer.WriteLine("\t\t\tName: \"Normals\"");
                    writer.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
                    writer.Write("\t\t\tNormals: *{0} {{\n\t\t\t\ta: ", mesh.Normals.Length);
                    for (int i = 0; i < mesh.Normals.Length; i++)
                    {
                        if (i > 0) writer.Write(',');
                        if (i > 0 && i % 12 == 0) writer.Write("\n\t\t\t\t");
                        writer.Write(mesh.Normals[i].ToString("G9", ci));
                    }
                    writer.WriteLine("\n\t\t\t}");
                    writer.WriteLine("\t\t}");
                }

                // UVs
                if (mesh.HasUVs)
                {
                    writer.WriteLine("\t\tLayerElementUV: 0 {");
                    writer.WriteLine("\t\t\tVersion: 101");
                    writer.WriteLine("\t\t\tName: \"UVMap\"");
                    writer.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
                    writer.Write("\t\t\tUV: *{0} {{\n\t\t\t\ta: ", mesh.UV0.Length);
                    for (int i = 0; i < mesh.UV0.Length; i++)
                    {
                        if (i > 0) writer.Write(',');
                        if (i > 0 && i % 12 == 0) writer.Write("\n\t\t\t\t");
                        writer.Write(mesh.UV0[i].ToString("G9", ci));
                    }
                    writer.WriteLine("\n\t\t\t}");
                    writer.WriteLine("\t\t}");
                }

                // Layer structure
                writer.WriteLine("\t\tLayer: 0 {");
                writer.WriteLine("\t\t\tVersion: 100");
                if (mesh.HasNormals)
                {
                    writer.WriteLine("\t\t\tLayerElement:  {");
                    writer.WriteLine("\t\t\t\tType: \"LayerElementNormal\"");
                    writer.WriteLine("\t\t\t\tTypedIndex: 0");
                    writer.WriteLine("\t\t\t}");
                }
                if (mesh.HasUVs)
                {
                    writer.WriteLine("\t\t\tLayerElement:  {");
                    writer.WriteLine("\t\t\t\tType: \"LayerElementUV\"");
                    writer.WriteLine("\t\t\t\tTypedIndex: 0");
                    writer.WriteLine("\t\t\t}");
                }
                writer.WriteLine("\t\t}");

                writer.WriteLine("\t}"); // End Geometry

                // Model node
                writer.WriteLine("\tModel: {0}, \"Model::{1}\", \"Mesh\" {{", modelId, meshName);
                writer.WriteLine("\t\tVersion: 232");
                writer.WriteLine("\t\tProperties70:  {");
                writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
                writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
                writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
                writer.WriteLine("\t\t}");
                writer.WriteLine("\t}");

                writer.WriteLine("}"); // End Objects
                writer.WriteLine();

                // Connections
                writer.WriteLine("Connections:  {");
                writer.WriteLine("\tC: \"OO\",{0},0", modelId);
                writer.WriteLine("\tC: \"OO\",{0},{1}", geometryId, modelId);
                writer.WriteLine("}");
            }

            return new FileInfo(outputPath).Length;
        }

        #endregion
    }
}
