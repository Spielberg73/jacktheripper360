using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Models
{
    /// <summary>
    /// Exports mesh data to glTF 2.0 format (.gltf + .bin).
    /// glTF is a modern 3D interchange format that supports meshes,
    /// materials, textures, and animations in a single package.
    /// </summary>
    public static class GltfExporter
    {
        public static ExportResult ExportToGltf(MeshData mesh, string outputPath)
        {
            if (mesh == null || mesh.Vertices.Count == 0)
                return ExportResult.Failed("No mesh data to export.");

            string basePath = Path.ChangeExtension(outputPath, null);
            string gltfPath = basePath + ".gltf";
            string binPath = basePath + ".bin";

            // Build binary buffer
            byte[] binData = BuildBinaryBuffer(mesh);
            File.WriteAllBytes(binPath, binData);

            // Build glTF JSON
            string gltfJson = BuildGltfJson(mesh, Path.GetFileName(binPath), binData.Length);
            File.WriteAllText(gltfPath, gltfJson);

            return ExportResult.Succeeded(gltfPath, gltfJson.Length + binData.Length);
        }

        private static byte[] BuildBinaryBuffer(MeshData mesh)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Write positions (vec3 float)
                foreach (var v in mesh.Vertices)
                {
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    writer.Write(v.Z);
                }

                // Pad to 4-byte boundary
                PadToAlignment(writer, 4);

                // Write normals (vec3 float)
                foreach (var n in mesh.Normals)
                {
                    writer.Write(n.X);
                    writer.Write(n.Y);
                    writer.Write(n.Z);
                }
                PadToAlignment(writer, 4);

                // Write UVs (vec2 float)
                foreach (var uv in mesh.UVs)
                {
                    writer.Write(uv.X);
                    writer.Write(1f - uv.Y); // Flip V
                }
                PadToAlignment(writer, 4);

                // Write indices (uint16 or uint32)
                bool use32bit = mesh.Vertices.Count > 65535;
                foreach (var idx in mesh.Indices)
                {
                    if (use32bit)
                        writer.Write((uint)idx);
                    else
                        writer.Write((ushort)idx);
                }
                PadToAlignment(writer, 4);

                return ms.ToArray();
            }
        }

        private static void PadToAlignment(BinaryWriter writer, int alignment)
        {
            long pos = writer.BaseStream.Position;
            long remainder = pos % alignment;
            if (remainder != 0)
            {
                for (int i = 0; i < alignment - remainder; i++)
                    writer.Write((byte)0);
            }
        }

        private static string BuildGltfJson(MeshData mesh, string binFileName, int bufferLength)
        {
            var sb = new StringBuilder();
            string meshName = mesh.Name ?? "mesh";
            bool hasNormals = mesh.Normals.Count == mesh.Vertices.Count;
            bool hasUVs = mesh.UVs.Count == mesh.Vertices.Count;
            bool use32bit = mesh.Vertices.Count > 65535;

            // Calculate buffer view offsets
            int positionSize = mesh.Vertices.Count * 12;
            int positionPadded = AlignUp(positionSize, 4);
            int normalSize = hasNormals ? mesh.Normals.Count * 12 : 0;
            int normalPadded = AlignUp(normalSize, 4);
            int uvSize = hasUVs ? mesh.UVs.Count * 8 : 0;
            int uvPadded = AlignUp(uvSize, 4);
            int indexSize = mesh.Indices.Count * (use32bit ? 4 : 2);

            int normalOffset = positionPadded;
            int uvOffset = normalOffset + normalPadded;
            int indexOffset = uvOffset + uvPadded;

            // Compute bounds
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var v in mesh.Vertices)
            {
                minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
                minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
                minZ = Math.Min(minZ, v.Z); maxZ = Math.Max(maxZ, v.Z);
            }

            sb.AppendLine("{");
            sb.AppendLine("  \"asset\": { \"version\": \"2.0\", \"generator\": \"JackTheRipper360\" },");
            sb.AppendLine("  \"scene\": 0,");
            sb.AppendLine("  \"scenes\": [{ \"nodes\": [0] }],");
            sb.AppendLine($"  \"nodes\": [{{ \"mesh\": 0, \"name\": \"{meshName}\" }}],");

            // Meshes
            sb.AppendLine("  \"meshes\": [{");
            sb.Append("    \"primitives\": [{ \"attributes\": { \"POSITION\": 0");
            int accessorIdx = 1;
            if (hasNormals) { sb.Append($", \"NORMAL\": {accessorIdx}"); accessorIdx++; }
            if (hasUVs) { sb.Append($", \"TEXCOORD_0\": {accessorIdx}"); accessorIdx++; }
            sb.AppendLine($" }}, \"indices\": {accessorIdx} }}]");
            sb.AppendLine("  }],");

            // Accessors
            sb.AppendLine("  \"accessors\": [");
            // Position accessor
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    {{ \"bufferView\": 0, \"componentType\": 5126, \"count\": {0}, \"type\": \"VEC3\", " +
                "\"min\": [{1:F6},{2:F6},{3:F6}], \"max\": [{4:F6},{5:F6},{6:F6}] }},",
                mesh.Vertices.Count, minX, minY, minZ, maxX, maxY, maxZ));

            int bufferViewIdx = 1;
            if (hasNormals)
            {
                sb.AppendLine($"    {{ \"bufferView\": {bufferViewIdx}, \"componentType\": 5126, \"count\": {mesh.Normals.Count}, \"type\": \"VEC3\" }},");
                bufferViewIdx++;
            }
            if (hasUVs)
            {
                sb.AppendLine($"    {{ \"bufferView\": {bufferViewIdx}, \"componentType\": 5126, \"count\": {mesh.UVs.Count}, \"type\": \"VEC2\" }},");
                bufferViewIdx++;
            }
            // Index accessor
            int indexComponentType = use32bit ? 5125 : 5123;
            sb.AppendLine($"    {{ \"bufferView\": {bufferViewIdx}, \"componentType\": {indexComponentType}, \"count\": {mesh.Indices.Count}, \"type\": \"SCALAR\" }}");
            sb.AppendLine("  ],");

            // Buffer views
            sb.AppendLine("  \"bufferViews\": [");
            sb.AppendLine($"    {{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": {positionSize}, \"target\": 34962 }},");
            if (hasNormals)
                sb.AppendLine($"    {{ \"buffer\": 0, \"byteOffset\": {normalOffset}, \"byteLength\": {normalSize}, \"target\": 34962 }},");
            if (hasUVs)
                sb.AppendLine($"    {{ \"buffer\": 0, \"byteOffset\": {uvOffset}, \"byteLength\": {uvSize}, \"target\": 34962 }},");
            sb.AppendLine($"    {{ \"buffer\": 0, \"byteOffset\": {indexOffset}, \"byteLength\": {indexSize}, \"target\": 34963 }}");
            sb.AppendLine("  ],");

            // Buffers
            sb.AppendLine($"  \"buffers\": [{{ \"uri\": \"{binFileName}\", \"byteLength\": {bufferLength} }}]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static int AlignUp(int value, int alignment)
        {
            int remainder = value % alignment;
            return remainder == 0 ? value : value + alignment - remainder;
        }
    }
}
