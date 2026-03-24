using System.Globalization;
using System.Text;

namespace JackTheRipper360.Core.Models
{
    /// <summary>
    /// Exports MeshData to common 3D model formats.
    /// OBJ export is implemented in pure C# (text-based format).
    /// </summary>
    public static class ModelExporter
    {
        /// <summary>
        /// Export mesh data to Wavefront OBJ format.
        /// </summary>
        public static string ExportToOBJ(MeshData mesh)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# JackTheRipper360 - Xbox 360 Asset Extractor");
            sb.AppendLine($"# Mesh: {mesh.Name ?? "unnamed"}");
            sb.AppendLine($"# Vertices: {mesh.Vertices.Count}");
            sb.AppendLine($"# Faces: {mesh.Indices.Count / 3}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(mesh.Name))
                sb.AppendLine($"o {mesh.Name}");

            // Vertices
            foreach (var v in mesh.Vertices)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "v {0:F6} {1:F6} {2:F6}", v.X, v.Y, v.Z));
            }
            sb.AppendLine();

            // Texture coordinates
            foreach (var uv in mesh.UVs)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "vt {0:F6} {1:F6}", uv.X, 1.0f - uv.Y)); // Flip V for OBJ
            }
            sb.AppendLine();

            // Normals
            foreach (var n in mesh.Normals)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "vn {0:F6} {1:F6} {2:F6}", n.X, n.Y, n.Z));
            }
            sb.AppendLine();

            // Faces (per submesh)
            bool hasUVs = mesh.UVs.Count > 0;
            bool hasNormals = mesh.Normals.Count > 0;

            if (mesh.SubMeshes.Count > 0)
            {
                foreach (var subMesh in mesh.SubMeshes)
                {
                    if (!string.IsNullOrEmpty(subMesh.Name))
                        sb.AppendLine($"g {subMesh.Name}");

                    if (subMesh.MaterialIndex >= 0 && subMesh.MaterialIndex < mesh.Materials.Count)
                        sb.AppendLine($"usemtl {mesh.Materials[subMesh.MaterialIndex].Name}");

                    WriteFaces(sb, mesh.Indices, subMesh.IndexStart, subMesh.IndexCount, hasUVs, hasNormals);
                }
            }
            else
            {
                WriteFaces(sb, mesh.Indices, 0, mesh.Indices.Count, hasUVs, hasNormals);
            }

            return sb.ToString();
        }

        private static void WriteFaces(StringBuilder sb, System.Collections.Generic.List<int> indices,
            int start, int count, bool hasUVs, bool hasNormals)
        {
            for (int i = start; i < start + count - 2; i += 3)
            {
                if (i + 2 >= indices.Count) break;

                // OBJ indices are 1-based
                int i0 = indices[i] + 1;
                int i1 = indices[i + 1] + 1;
                int i2 = indices[i + 2] + 1;

                if (hasUVs && hasNormals)
                    sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
                else if (hasUVs)
                    sb.AppendLine($"f {i0}/{i0} {i1}/{i1} {i2}/{i2}");
                else if (hasNormals)
                    sb.AppendLine($"f {i0}//{i0} {i1}//{i1} {i2}//{i2}");
                else
                    sb.AppendLine($"f {i0} {i1} {i2}");
            }
        }

        /// <summary>
        /// Export material data as MTL file (companion to OBJ).
        /// </summary>
        public static string ExportToMTL(MeshData mesh)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# JackTheRipper360 - Material Library");
            sb.AppendLine();

            foreach (var mat in mesh.Materials)
            {
                sb.AppendLine($"newmtl {mat.Name ?? "default"}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Kd {0:F4} {1:F4} {2:F4}", mat.DiffuseColor[0], mat.DiffuseColor[1], mat.DiffuseColor[2]));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Ks {0:F4} {1:F4} {2:F4}", mat.SpecularColor[0], mat.SpecularColor[1], mat.SpecularColor[2]));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Ns {0:F2}", mat.Shininess));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "d {0:F4}", mat.Opacity));

                if (!string.IsNullOrEmpty(mat.DiffuseTexture))
                    sb.AppendLine($"map_Kd {mat.DiffuseTexture}");
                if (!string.IsNullOrEmpty(mat.NormalTexture))
                    sb.AppendLine($"bump {mat.NormalTexture}");
                if (!string.IsNullOrEmpty(mat.SpecularTexture))
                    sb.AppendLine($"map_Ks {mat.SpecularTexture}");

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
