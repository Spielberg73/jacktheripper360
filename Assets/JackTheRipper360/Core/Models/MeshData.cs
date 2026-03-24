using System.Collections.Generic;

namespace JackTheRipper360.Core.Models
{
    /// <summary>
    /// Platform-agnostic mesh data extracted from Xbox 360 model formats.
    /// </summary>
    public class MeshData
    {
        public string Name { get; set; }
        public List<Vector3f> Vertices { get; set; } = new List<Vector3f>();
        public List<Vector3f> Normals { get; set; } = new List<Vector3f>();
        public List<Vector2f> UVs { get; set; } = new List<Vector2f>();
        public List<int> Indices { get; set; } = new List<int>();
        public List<SubMesh> SubMeshes { get; set; } = new List<SubMesh>();
        public List<MaterialData> Materials { get; set; } = new List<MaterialData>();

        // Skeletal data
        public List<Vector4f> BoneWeights { get; set; } = new List<Vector4f>();
        public List<int[]> BoneIndices { get; set; } = new List<int[]>();
        public List<BoneData> Bones { get; set; } = new List<BoneData>();
    }

    public struct Vector2f
    {
        public float X, Y;
        public Vector2f(float x, float y) { X = x; Y = y; }
    }

    public struct Vector3f
    {
        public float X, Y, Z;
        public Vector3f(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    public struct Vector4f
    {
        public float X, Y, Z, W;
        public Vector4f(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    }

    public class SubMesh
    {
        public string Name { get; set; }
        public int IndexStart { get; set; }
        public int IndexCount { get; set; }
        public int MaterialIndex { get; set; }
    }

    public class BoneData
    {
        public string Name { get; set; }
        public int ParentIndex { get; set; } = -1;
        public Vector3f Position { get; set; }
        public Vector4f Rotation { get; set; }
        public Vector3f Scale { get; set; } = new Vector3f(1, 1, 1);
    }
}
