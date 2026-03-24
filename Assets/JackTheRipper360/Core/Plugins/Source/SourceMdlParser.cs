using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Source
{
    /// <summary>
    /// Parser for Source engine StudioModel (MDL) files.
    /// Reads header metadata, bone hierarchy, texture names, and body part definitions.
    /// Actual mesh geometry resides in companion .VVD and .VTX files; this parser
    /// performs metadata extraction only and exports a JSON summary.
    /// Supports MDL versions 44 through 49.
    /// </summary>
    public class SourceMdlParser : IAssetParser
    {
        private const int MinMdlFileSize = 408;
        private const int SupportedVersionMin = 44;
        private const int SupportedVersionMax = 49;

        // Struct sizes within the MDL file
        private const int BoneStructSize = 216;
        private const int TextureStructSize = 64;
        private const int BodyPartStructSize = 16;
        private const int HitboxSetStructSize = 12;
        private const int AnimDescStructSize = 100;
        private const int SeqDescStructSize = 212;

        public AssetType Type => AssetType.Model;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < MinMdlFileSize)
                return false;

            string ext = Path.GetExtension(fileName);
            if (!string.Equals(ext, ".mdl", StringComparison.OrdinalIgnoreCase))
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            if (stream.Read(header, 0, 8) < 8)
                return false;

            // Magic: "IDST" (0x54534449 little-endian)
            if (header[0] != 'I' || header[1] != 'D' || header[2] != 'S' || header[3] != 'T')
                return false;

            int version = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
            return version >= SupportedVersionMin && version <= SupportedVersionMax;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                // Magic and version
                string magic = reader.ReadString(4);
                if (magic != "IDST")
                    throw new InvalidDataException($"Invalid MDL signature: '{magic}'");

                int version = reader.ReadInt32();
                if (version < SupportedVersionMin || version > SupportedVersionMax)
                    throw new InvalidDataException($"Unsupported MDL version: {version}");

                // Checksum
                int checksum = reader.ReadInt32();

                // Name (64 bytes, null-terminated)
                string modelName = reader.ReadString(64);

                // Data length
                int dataLength = reader.ReadInt32();

                // Eye position (3 floats)
                float eyePosX = reader.ReadSingle();
                float eyePosY = reader.ReadSingle();
                float eyePosZ = reader.ReadSingle();

                // Illumination position (3 floats)
                float illumPosX = reader.ReadSingle();
                float illumPosY = reader.ReadSingle();
                float illumPosZ = reader.ReadSingle();

                // Hull min/max (6 floats)
                float hullMinX = reader.ReadSingle();
                float hullMinY = reader.ReadSingle();
                float hullMinZ = reader.ReadSingle();
                float hullMaxX = reader.ReadSingle();
                float hullMaxY = reader.ReadSingle();
                float hullMaxZ = reader.ReadSingle();

                // View bounding box min/max (6 floats)
                float viewMinX = reader.ReadSingle();
                float viewMinY = reader.ReadSingle();
                float viewMinZ = reader.ReadSingle();
                float viewMaxX = reader.ReadSingle();
                float viewMaxY = reader.ReadSingle();
                float viewMaxZ = reader.ReadSingle();

                // Flags
                int flags = reader.ReadInt32();

                // Bones
                int numBones = reader.ReadInt32();
                int boneOffset = reader.ReadInt32();

                // Bone controllers
                int numBoneControllers = reader.ReadInt32();
                int boneControllerOffset = reader.ReadInt32();

                // Hitbox sets
                int numHitboxSets = reader.ReadInt32();
                int hitboxSetOffset = reader.ReadInt32();

                // Local animations
                int numLocalAnim = reader.ReadInt32();
                int localAnimOffset = reader.ReadInt32();

                // Local sequences
                int numLocalSeq = reader.ReadInt32();
                int localSeqOffset = reader.ReadInt32();

                // Activity list version and events indexed
                int activityListVersion = reader.ReadInt32();
                int eventsIndexed = reader.ReadInt32();

                // Textures
                int numTextures = reader.ReadInt32();
                int textureOffset = reader.ReadInt32();

                // Texture directories
                int numTextureDirs = reader.ReadInt32();
                int textureDirOffset = reader.ReadInt32();

                // Skin references
                int numSkinRef = reader.ReadInt32();
                int numSkinFamilies = reader.ReadInt32();
                int skinRefOffset = reader.ReadInt32();

                // Body parts
                int numBodyParts = reader.ReadInt32();
                int bodyPartOffset = reader.ReadInt32();

                // Parse texture names
                var textureNames = new List<string>();
                if (numTextures > 0 && numTextures < 4096 &&
                    textureOffset > 0 && textureOffset < reader.Length)
                {
                    textureNames = ReadTextureNames(reader, numTextures, textureOffset);
                }

                // Parse body part names
                var bodyPartNames = new List<string>();
                if (numBodyParts > 0 && numBodyParts < 1024 &&
                    bodyPartOffset > 0 && bodyPartOffset < reader.Length)
                {
                    bodyPartNames = ReadBodyPartNames(reader, numBodyParts, bodyPartOffset);
                }

                // Parse bone names
                var boneNames = new List<string>();
                if (numBones > 0 && numBones < 4096 &&
                    boneOffset > 0 && boneOffset < reader.Length)
                {
                    boneNames = ReadBoneNames(reader, numBones, boneOffset);
                }

                var entry = new AssetEntry(fileName, AssetType.Model)
                {
                    SourcePath = fileName,
                    Offset = 0,
                    Size = stream.Length,
                    FormatName = $"Source MDL v{version}"
                };

                entry.Metadata["Version"] = version;
                entry.Metadata["Checksum"] = checksum;
                entry.Metadata["ModelName"] = modelName;
                entry.Metadata["DataLength"] = dataLength;
                entry.Metadata["EyePosition"] = $"{eyePosX}, {eyePosY}, {eyePosZ}";
                entry.Metadata["HullMin"] = $"{hullMinX}, {hullMinY}, {hullMinZ}";
                entry.Metadata["HullMax"] = $"{hullMaxX}, {hullMaxY}, {hullMaxZ}";
                entry.Metadata["ViewMin"] = $"{viewMinX}, {viewMinY}, {viewMinZ}";
                entry.Metadata["ViewMax"] = $"{viewMaxX}, {viewMaxY}, {viewMaxZ}";
                entry.Metadata["Flags"] = flags;
                entry.Metadata["NumBones"] = numBones;
                entry.Metadata["NumBoneControllers"] = numBoneControllers;
                entry.Metadata["NumHitboxSets"] = numHitboxSets;
                entry.Metadata["NumLocalAnimations"] = numLocalAnim;
                entry.Metadata["NumLocalSequences"] = numLocalSeq;
                entry.Metadata["NumTextures"] = numTextures;
                entry.Metadata["NumTextureDirs"] = numTextureDirs;
                entry.Metadata["NumSkinReferences"] = numSkinRef;
                entry.Metadata["NumSkinFamilies"] = numSkinFamilies;
                entry.Metadata["NumBodyParts"] = numBodyParts;
                entry.Metadata["TextureNames"] = textureNames;
                entry.Metadata["BodyPartNames"] = bodyPartNames;
                entry.Metadata["BoneNames"] = boneNames;

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string jsonPath = Path.ChangeExtension(outputPath, ".json");
            string dir = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            WriteJsonString(sb, "modelName", GetMetadataString(entry, "ModelName"), 1);
            WriteJsonString(sb, "format", entry.FormatName, 1);
            WriteJsonNumber(sb, "version", GetMetadataInt(entry, "Version"), 1);
            WriteJsonNumber(sb, "checksum", GetMetadataInt(entry, "Checksum"), 1);
            WriteJsonNumber(sb, "dataLength", GetMetadataInt(entry, "DataLength"), 1);
            WriteJsonNumber(sb, "fileSize", entry.Size, 1);
            WriteJsonNumber(sb, "flags", GetMetadataInt(entry, "Flags"), 1);

            // Bounding boxes
            sb.AppendLine("  \"boundingBox\": {");
            WriteJsonString(sb, "eyePosition", GetMetadataString(entry, "EyePosition"), 2);
            WriteJsonString(sb, "hullMin", GetMetadataString(entry, "HullMin"), 2);
            WriteJsonString(sb, "hullMax", GetMetadataString(entry, "HullMax"), 2);
            WriteJsonString(sb, "viewMin", GetMetadataString(entry, "ViewMin"), 2);
            WriteJsonStringLast(sb, "viewMax", GetMetadataString(entry, "ViewMax"), 2);
            sb.AppendLine("  },");

            // Counts
            sb.AppendLine("  \"counts\": {");
            WriteJsonNumber(sb, "bones", GetMetadataInt(entry, "NumBones"), 2);
            WriteJsonNumber(sb, "boneControllers", GetMetadataInt(entry, "NumBoneControllers"), 2);
            WriteJsonNumber(sb, "hitboxSets", GetMetadataInt(entry, "NumHitboxSets"), 2);
            WriteJsonNumber(sb, "localAnimations", GetMetadataInt(entry, "NumLocalAnimations"), 2);
            WriteJsonNumber(sb, "localSequences", GetMetadataInt(entry, "NumLocalSequences"), 2);
            WriteJsonNumber(sb, "textures", GetMetadataInt(entry, "NumTextures"), 2);
            WriteJsonNumber(sb, "skinReferences", GetMetadataInt(entry, "NumSkinReferences"), 2);
            WriteJsonNumber(sb, "skinFamilies", GetMetadataInt(entry, "NumSkinFamilies"), 2);
            WriteJsonNumberLast(sb, "bodyParts", GetMetadataInt(entry, "NumBodyParts"), 2);
            sb.AppendLine("  },");

            // Bones
            WriteJsonStringArray(sb, "bones", GetMetadataStringList(entry, "BoneNames"), 1);
            sb.AppendLine(",");

            // Textures
            WriteJsonStringArray(sb, "textures", GetMetadataStringList(entry, "TextureNames"), 1);
            sb.AppendLine(",");

            // Body parts
            WriteJsonStringArray(sb, "bodyParts", GetMetadataStringList(entry, "BodyPartNames"), 1);
            sb.AppendLine();

            sb.AppendLine("}");

            string json = sb.ToString();
            File.WriteAllText(jsonPath, json, Encoding.UTF8);

            return ExportResult.Succeeded(jsonPath, json.Length);
        }

        #region MDL Structure Readers

        /// <summary>
        /// Reads texture names from the MDL texture table.
        /// Each texture entry is 64 bytes; the first 4 bytes are a relative offset to the name string.
        /// </summary>
        private static List<string> ReadTextureNames(EndianBinaryReader reader, int count, int tableOffset)
        {
            var names = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                long entryOffset = tableOffset + (long)i * TextureStructSize;
                if (entryOffset + 4 > reader.Length)
                    break;

                reader.Seek(entryOffset);
                int nameRelativeOffset = reader.ReadInt32();

                long nameAbsoluteOffset = entryOffset + nameRelativeOffset;
                if (nameRelativeOffset != 0 && nameAbsoluteOffset > 0 && nameAbsoluteOffset < reader.Length)
                {
                    reader.Seek(nameAbsoluteOffset);
                    string name = reader.ReadNullTerminatedString();
                    names.Add(name);
                }
                else
                {
                    names.Add($"texture_{i}");
                }
            }

            return names;
        }

        /// <summary>
        /// Reads body part names from the MDL body part table.
        /// Each body part entry is 16 bytes; the first 4 bytes are a relative offset to the name string.
        /// </summary>
        private static List<string> ReadBodyPartNames(EndianBinaryReader reader, int count, int tableOffset)
        {
            var names = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                long entryOffset = tableOffset + (long)i * BodyPartStructSize;
                if (entryOffset + 4 > reader.Length)
                    break;

                reader.Seek(entryOffset);
                int nameRelativeOffset = reader.ReadInt32();

                long nameAbsoluteOffset = entryOffset + nameRelativeOffset;
                if (nameRelativeOffset != 0 && nameAbsoluteOffset > 0 && nameAbsoluteOffset < reader.Length)
                {
                    reader.Seek(nameAbsoluteOffset);
                    string name = reader.ReadNullTerminatedString();
                    names.Add(name);
                }
                else
                {
                    names.Add($"bodypart_{i}");
                }
            }

            return names;
        }

        /// <summary>
        /// Reads bone names from the MDL bone table.
        /// Each bone entry is 216 bytes; the first 4 bytes are a relative offset to the name string.
        /// </summary>
        private static List<string> ReadBoneNames(EndianBinaryReader reader, int count, int boneOffset)
        {
            var names = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                long entryOffset = boneOffset + (long)i * BoneStructSize;
                if (entryOffset + 4 > reader.Length)
                    break;

                reader.Seek(entryOffset);
                int nameRelativeOffset = reader.ReadInt32();

                long nameAbsoluteOffset = entryOffset + nameRelativeOffset;
                if (nameRelativeOffset != 0 && nameAbsoluteOffset > 0 && nameAbsoluteOffset < reader.Length)
                {
                    reader.Seek(nameAbsoluteOffset);
                    string name = reader.ReadNullTerminatedString();
                    names.Add(name);
                }
                else
                {
                    names.Add($"bone_{i}");
                }
            }

            return names;
        }

        #endregion

        #region JSON Helpers

        private static string GetMetadataString(AssetEntry entry, string key)
        {
            if (entry.Metadata.TryGetValue(key, out object value) && value != null)
                return value.ToString();
            return "";
        }

        private static int GetMetadataInt(AssetEntry entry, string key)
        {
            if (entry.Metadata.TryGetValue(key, out object value) && value is int intVal)
                return intVal;
            return 0;
        }

        private static List<string> GetMetadataStringList(AssetEntry entry, string key)
        {
            if (entry.Metadata.TryGetValue(key, out object value) && value is List<string> list)
                return list;
            return new List<string>();
        }

        private static string Indent(int level)
        {
            return new string(' ', level * 2);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static void WriteJsonString(StringBuilder sb, string key, string value, int indent)
        {
            sb.AppendLine($"{Indent(indent)}\"{key}\": \"{EscapeJson(value)}\",");
        }

        private static void WriteJsonStringLast(StringBuilder sb, string key, string value, int indent)
        {
            sb.AppendLine($"{Indent(indent)}\"{key}\": \"{EscapeJson(value)}\"");
        }

        private static void WriteJsonNumber(StringBuilder sb, string key, long value, int indent)
        {
            sb.AppendLine($"{Indent(indent)}\"{key}\": {value},");
        }

        private static void WriteJsonNumberLast(StringBuilder sb, string key, long value, int indent)
        {
            sb.AppendLine($"{Indent(indent)}\"{key}\": {value}");
        }

        private static void WriteJsonStringArray(StringBuilder sb, string key, List<string> values, int indent)
        {
            string pad = Indent(indent);
            string innerPad = Indent(indent + 1);

            if (values.Count == 0)
            {
                sb.Append($"{pad}\"{key}\": []");
                return;
            }

            sb.AppendLine($"{pad}\"{key}\": [");
            for (int i = 0; i < values.Count; i++)
            {
                string comma = i < values.Count - 1 ? "," : "";
                sb.AppendLine($"{innerPad}\"{EscapeJson(values[i])}\"{comma}");
            }
            sb.Append($"{pad}]");
        }

        #endregion
    }
}
