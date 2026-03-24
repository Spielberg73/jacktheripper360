#if UNITY_EDITOR || UNITY_STANDALONE
using System.Collections.Generic;
using UnityEngine;
using JackTheRipper360.Core.Models;

namespace JackTheRipper360.Runtime.Preview
{
    /// <summary>
    /// Converts MeshData into Unity Mesh objects for 3D preview.
    /// </summary>
    public static class ModelPreviewRenderer
    {
        /// <summary>
        /// Create a Unity Mesh from extracted MeshData.
        /// </summary>
        public static Mesh CreateMesh(MeshData meshData)
        {
            if (meshData == null || meshData.Vertices.Count == 0)
                return null;

            var mesh = new Mesh();
            mesh.name = meshData.Name ?? "Xbox360Mesh";

            // Set vertices
            var vertices = new Vector3[meshData.Vertices.Count];
            for (int i = 0; i < meshData.Vertices.Count; i++)
            {
                var v = meshData.Vertices[i];
                vertices[i] = new Vector3(v.X, v.Y, v.Z);
            }
            mesh.vertices = vertices;

            // Set normals
            if (meshData.Normals.Count == meshData.Vertices.Count)
            {
                var normals = new Vector3[meshData.Normals.Count];
                for (int i = 0; i < meshData.Normals.Count; i++)
                {
                    var n = meshData.Normals[i];
                    normals[i] = new Vector3(n.X, n.Y, n.Z);
                }
                mesh.normals = normals;
            }

            // Set UVs
            if (meshData.UVs.Count == meshData.Vertices.Count)
            {
                var uvs = new Vector2[meshData.UVs.Count];
                for (int i = 0; i < meshData.UVs.Count; i++)
                {
                    var uv = meshData.UVs[i];
                    uvs[i] = new Vector2(uv.X, uv.Y);
                }
                mesh.uv = uvs;
            }

            // Set triangles (submeshes)
            if (meshData.SubMeshes.Count > 0)
            {
                mesh.subMeshCount = meshData.SubMeshes.Count;
                for (int s = 0; s < meshData.SubMeshes.Count; s++)
                {
                    var sub = meshData.SubMeshes[s];
                    int[] triangles = new int[sub.IndexCount];
                    for (int i = 0; i < sub.IndexCount && sub.IndexStart + i < meshData.Indices.Count; i++)
                    {
                        triangles[i] = meshData.Indices[sub.IndexStart + i];
                    }
                    mesh.SetTriangles(triangles, s);
                }
            }
            else if (meshData.Indices.Count > 0)
            {
                mesh.triangles = meshData.Indices.ToArray();
            }

            if (mesh.normals == null || mesh.normals.Length == 0)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Create a preview GameObject with the mesh and a default material.
        /// </summary>
        public static GameObject CreatePreviewObject(MeshData meshData)
        {
            Mesh mesh = CreateMesh(meshData);
            if (mesh == null) return null;

            var go = new GameObject(meshData.Name ?? "MeshPreview");
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            filter.mesh = mesh;

            // Create materials for submeshes
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = new Material(Shader.Find("Standard"));
                if (i < meshData.Materials.Count)
                {
                    var matData = meshData.Materials[i];
                    materials[i].name = matData.Name ?? $"Material_{i}";
                    materials[i].color = new Color(
                        matData.DiffuseColor[0],
                        matData.DiffuseColor[1],
                        matData.DiffuseColor[2],
                        matData.DiffuseColor[3]
                    );
                }
            }
            renderer.materials = materials;

            return go;
        }
    }
}
#endif
