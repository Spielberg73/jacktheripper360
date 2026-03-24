using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unity
{
    /// <summary>
    /// Parser for Unity Asset Bundles (UnityFS/UnityWeb/UnityRaw) and serialized .assets files.
    /// Detects and catalogs all embedded objects (Texture2D, Mesh, AudioClip, AnimationClip, etc.)
    /// creating child AssetEntry nodes for each discoverable object.
    /// </summary>
    public class UnityAssetParser : IAssetParser
    {
        public AssetType Type => AssetType.Container;

        private const int MIN_HEADER_SIZE = 16;
        private const int MAX_REASONABLE_OBJECTS = 500000;

        #region Unity ClassID to AssetType mapping

        private static readonly Dictionary<int, AssetType> ClassIdToAssetType = new Dictionary<int, AssetType>
        {
            { 1, AssetType.Data },         // GameObject
            { 4, AssetType.Data },         // Transform
            { 21, AssetType.Data },        // Material
            { 28, AssetType.Texture },     // Texture2D
            { 43, AssetType.Model },       // Mesh
            { 48, AssetType.Data },        // Shader
            { 49, AssetType.Data },        // TextAsset
            { 74, AssetType.Animation },   // AnimationClip
            { 83, AssetType.Audio },       // AudioClip
            { 114, AssetType.Data },       // MonoBehaviour
            { 115, AssetType.Data },       // MonoScript
            { 128, AssetType.Data },       // Font
            { 150, AssetType.Data },       // PreloadData
            { 152, AssetType.Video },      // MovieTexture
            { 156, AssetType.Data },       // TerrainData
            { 213, AssetType.Texture },    // Sprite
            { 241, AssetType.Audio },      // AudioMixerController
        };

        private static readonly Dictionary<int, string> ClassIdToName = new Dictionary<int, string>
        {
            { 1, "GameObject" }, { 4, "Transform" }, { 21, "Material" },
            { 28, "Texture2D" }, { 43, "Mesh" }, { 48, "Shader" },
            { 49, "TextAsset" }, { 74, "AnimationClip" }, { 83, "AudioClip" },
            { 114, "MonoBehaviour" }, { 115, "MonoScript" }, { 128, "Font" },
            { 150, "PreloadData" }, { 152, "MovieTexture" }, { 156, "TerrainData" },
            { 213, "Sprite" }, { 241, "AudioMixerController" },
        };

        #endregion

        #region Detection

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream == null || stream.Length < MIN_HEADER_SIZE)
                return false;

            try
            {
                long savedPos = stream.Position;
                stream.Seek(0, SeekOrigin.Begin);

                // Check for asset bundle signatures
                byte[] header = new byte[Math.Min(16, stream.Length)];
                stream.Read(header, 0, header.Length);
                stream.Position = savedPos;

                string headerStr = Encoding.ASCII.GetString(header, 0, Math.Min(header.Length, 10));

                if (headerStr.StartsWith("UnityFS") ||
                    headerStr.StartsWith("UnityWeb") ||
                    headerStr.StartsWith("UnityRaw"))
                    return true;

                // Check by extension for .assets files
                string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                if (ext == ".assets" || ext == ".unity3d" || ext == ".bundle")
                {
                    // Validate serialized file header structure
                    stream.Seek(0, SeekOrigin.Begin);
                    return ValidateSerializedHeader(stream);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateSerializedHeader(Stream stream)
        {
            if (stream.Length < 20)
                return false;

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    // Unity serialized files start with metadata size (big-endian int32)
                    byte[] bytes = reader.ReadBytes(4);
                    int metadataSize = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

                    // Should be positive and less than file size
                    if (metadataSize <= 0 || metadataSize > stream.Length)
                        return false;

                    bytes = reader.ReadBytes(4);
                    int fileSize = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

                    bytes = reader.ReadBytes(4);
                    int version = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

                    // Version should be reasonable (Unity uses versions ~5-22)
                    if (version < 5 || version > 50)
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Parsing

        public AssetEntry Parse(Stream stream, string fileName)
        {
            stream.Seek(0, SeekOrigin.Begin);

            byte[] header = new byte[Math.Min(16, stream.Length)];
            stream.Read(header, 0, header.Length);
            string headerStr = Encoding.ASCII.GetString(header, 0, Math.Min(header.Length, 10));

            if (headerStr.StartsWith("UnityFS") || headerStr.StartsWith("UnityWeb") || headerStr.StartsWith("UnityRaw"))
            {
                return ParseAssetBundle(stream, fileName, headerStr);
            }
            else
            {
                stream.Seek(0, SeekOrigin.Begin);
                return ParseSerializedFile(stream, fileName);
            }
        }

        private AssetEntry ParseAssetBundle(Stream stream, string fileName, string signature)
        {
            stream.Seek(0, SeekOrigin.Begin);

            var entry = new AssetEntry(Path.GetFileName(fileName), AssetType.Container)
            {
                SourcePath = fileName,
                Size = stream.Length,
                FormatName = $"Unity AssetBundle ({signature.TrimEnd('\0')})"
            };

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    // Read signature string (null-terminated)
                    string sig = ReadNullTerminated(reader);

                    // Format version (big-endian int32)
                    int formatVersion = ReadInt32BE(reader);

                    // Unity version string
                    string unityVersion = ReadNullTerminated(reader);

                    // Generator version string
                    string generatorVersion = ReadNullTerminated(reader);

                    entry.Metadata["Signature"] = sig;
                    entry.Metadata["FormatVersion"] = formatVersion;
                    entry.Metadata["UnityVersion"] = unityVersion;
                    entry.Metadata["GeneratorVersion"] = generatorVersion;

                    if (sig == "UnityFS")
                    {
                        ParseUnityFSBundle(reader, entry, formatVersion);
                    }
                    else
                    {
                        // UnityRaw / UnityWeb - older format
                        ParseLegacyBundle(reader, entry, formatVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                entry.Metadata["ParseError"] = ex.Message;
            }

            return entry;
        }

        private void ParseUnityFSBundle(BinaryReader reader, AssetEntry entry, int formatVersion)
        {
            // File size (int64 big-endian)
            long totalFileSize = ReadInt64BE(reader);

            // Compressed block info size
            int compressedBlocksInfoSize = ReadInt32BE(reader);

            // Uncompressed block info size
            int uncompressedBlocksInfoSize = ReadInt32BE(reader);

            // Flags
            int flags = ReadInt32BE(reader);

            int compressionType = flags & 0x3F;
            bool hasDirectoryInfo = (flags & 0x40) != 0;
            bool blockInfoAtEnd = (flags & 0x80) != 0;

            entry.Metadata["TotalFileSize"] = totalFileSize;
            entry.Metadata["CompressedBlockInfoSize"] = compressedBlocksInfoSize;
            entry.Metadata["UncompressedBlockInfoSize"] = uncompressedBlocksInfoSize;
            entry.Metadata["CompressionType"] = GetCompressionName(compressionType);
            entry.Metadata["HasDirectoryInfo"] = hasDirectoryInfo;
            entry.Metadata["BlockInfoAtEnd"] = blockInfoAtEnd;

            // Try to read block info (uncompressed case)
            if (compressionType == 0) // None
            {
                try
                {
                    long blockInfoPos = blockInfoAtEnd
                        ? reader.BaseStream.Length - compressedBlocksInfoSize
                        : reader.BaseStream.Position;

                    reader.BaseStream.Seek(blockInfoPos, SeekOrigin.Begin);

                    // Read uncompressed block info
                    byte[] blockInfoHash = reader.ReadBytes(16); // hash

                    int blockCount = ReadInt32BE(reader);
                    entry.Metadata["BlockCount"] = blockCount;

                    if (blockCount > 0 && blockCount < 10000)
                    {
                        var blocks = new List<BlockInfo>();
                        for (int i = 0; i < blockCount; i++)
                        {
                            int uncompSize = ReadInt32BE(reader);
                            int compSize = ReadInt32BE(reader);
                            short blockFlags = ReadInt16BE(reader);
                            blocks.Add(new BlockInfo
                            {
                                UncompressedSize = uncompSize,
                                CompressedSize = compSize,
                                Flags = blockFlags
                            });
                        }

                        // Read directory info
                        int nodeCount = ReadInt32BE(reader);
                        entry.Metadata["NodeCount"] = nodeCount;

                        if (nodeCount > 0 && nodeCount < MAX_REASONABLE_OBJECTS)
                        {
                            for (int i = 0; i < nodeCount; i++)
                            {
                                long offset = ReadInt64BE(reader);
                                long size = ReadInt64BE(reader);
                                int nodeFlags = ReadInt32BE(reader);
                                string nodeName = ReadNullTerminated(reader);

                                var childEntry = new AssetEntry(
                                    string.IsNullOrEmpty(nodeName) ? $"node_{i}" : nodeName,
                                    AssetType.Data)
                                {
                                    SourcePath = entry.SourcePath,
                                    Offset = offset,
                                    Size = size,
                                    FormatName = "Unity Serialized File"
                                };
                                childEntry.Metadata["NodeFlags"] = nodeFlags;
                                entry.Children.Add(childEntry);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    entry.Metadata["BlockInfoError"] = ex.Message;
                }
            }
            else
            {
                entry.Metadata["Note"] = $"Block info is {GetCompressionName(compressionType)} compressed - enumeration requires decompression";
            }
        }

        private void ParseLegacyBundle(BinaryReader reader, AssetEntry entry, int formatVersion)
        {
            // Legacy bundle: file size, header size, count fields
            try
            {
                int minimumStreamedBytes = ReadInt32BE(reader);
                int headerSize = ReadInt32BE(reader);
                int numberOfLevelsToDownloadBeforeStreaming = ReadInt32BE(reader);
                int levelCount = ReadInt32BE(reader);

                entry.Metadata["HeaderSize"] = headerSize;
                entry.Metadata["LevelCount"] = levelCount;

                if (levelCount > 0 && levelCount < 100)
                {
                    for (int i = 0; i < levelCount; i++)
                    {
                        int compSize = ReadInt32BE(reader);
                        int uncompSize = ReadInt32BE(reader);
                        entry.Metadata[$"Level{i}_CompressedSize"] = compSize;
                        entry.Metadata[$"Level{i}_UncompressedSize"] = uncompSize;
                    }
                }

                // Complete file size
                if (formatVersion >= 2)
                {
                    int completeFileSize = ReadInt32BE(reader);
                    entry.Metadata["CompleteFileSize"] = completeFileSize;
                }

                // Data header size
                if (formatVersion >= 3)
                {
                    int dataHeaderSize = ReadInt32BE(reader);
                    entry.Metadata["DataHeaderSize"] = dataHeaderSize;
                }
            }
            catch (Exception ex)
            {
                entry.Metadata["LegacyParseError"] = ex.Message;
            }
        }

        private AssetEntry ParseSerializedFile(Stream stream, string fileName)
        {
            var entry = new AssetEntry(Path.GetFileName(fileName), AssetType.Container)
            {
                SourcePath = fileName,
                Size = stream.Length,
                FormatName = "Unity Serialized File"
            };

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    // Header (big-endian)
                    int metadataSize = ReadInt32BE(reader);
                    int fileSize = ReadInt32BE(reader);
                    int version = ReadInt32BE(reader);
                    int dataOffset = ReadInt32BE(reader);

                    entry.Metadata["MetadataSize"] = metadataSize;
                    entry.Metadata["SerializedVersion"] = version;
                    entry.Metadata["DataOffset"] = $"0x{dataOffset:X}";

                    bool bigEndian;
                    if (version >= 9)
                    {
                        // Endianness byte
                        byte endian = reader.ReadByte();
                        reader.ReadBytes(3); // reserved
                        bigEndian = endian != 0;
                    }
                    else
                    {
                        bigEndian = true;
                    }

                    entry.Metadata["BigEndian"] = bigEndian;

                    // Unity version string (for version >= 7)
                    if (version >= 7)
                    {
                        string unityVersion = ReadNullTerminated(reader);
                        entry.Metadata["UnityVersion"] = unityVersion;
                    }

                    // Target platform (for version >= 8)
                    if (version >= 8)
                    {
                        int platform = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
                        entry.Metadata["Platform"] = GetPlatformName(platform);
                    }

                    // Type tree (version >= 13)
                    bool enableTypeTree = true;
                    if (version >= 13)
                    {
                        enableTypeTree = reader.ReadByte() != 0;
                        entry.Metadata["HasTypeTree"] = enableTypeTree;
                    }

                    // Read types
                    int typeCount = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
                    entry.Metadata["TypeCount"] = typeCount;

                    if (typeCount > 0 && typeCount < 10000)
                    {
                        var types = new List<SerializedType>();
                        for (int i = 0; i < typeCount; i++)
                        {
                            var type = ReadSerializedType(reader, version, enableTypeTree, bigEndian);
                            types.Add(type);
                        }

                        // Read object info
                        int objectCount = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
                        entry.Metadata["ObjectCount"] = objectCount;

                        if (objectCount > 0 && objectCount < MAX_REASONABLE_OBJECTS)
                        {
                            for (int i = 0; i < objectCount; i++)
                            {
                                try
                                {
                                    var objInfo = ReadObjectInfo(reader, version, bigEndian);

                                    int classId = objInfo.ClassId;
                                    if (objInfo.TypeIndex >= 0 && objInfo.TypeIndex < types.Count)
                                        classId = types[objInfo.TypeIndex].ClassId;

                                    string className = ClassIdToName.ContainsKey(classId)
                                        ? ClassIdToName[classId]
                                        : $"ClassID_{classId}";

                                    AssetType assetType = ClassIdToAssetType.ContainsKey(classId)
                                        ? ClassIdToAssetType[classId]
                                        : AssetType.Data;

                                    var child = new AssetEntry($"{className}_{i}", assetType)
                                    {
                                        SourcePath = fileName,
                                        Offset = objInfo.DataOffset + dataOffset,
                                        Size = objInfo.Size,
                                        FormatName = className
                                    };
                                    child.Metadata["ClassID"] = classId;
                                    child.Metadata["ClassName"] = className;
                                    child.Metadata["FileID"] = objInfo.FileId;

                                    entry.Children.Add(child);
                                }
                                catch
                                {
                                    break; // Corrupt data, stop parsing
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                entry.Metadata["ParseError"] = ex.Message;
            }

            return entry;
        }

        #endregion

        #region Export

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // For container types, extract children
                if (entry.Children.Count > 0)
                {
                    string baseName = Path.GetFileNameWithoutExtension(outputPath);
                    string baseDir = Path.Combine(dir ?? ".", baseName + "_extracted");
                    Directory.CreateDirectory(baseDir);

                    int exported = 0;
                    foreach (var child in entry.Children)
                    {
                        try
                        {
                            string childPath = Path.Combine(baseDir, SanitizeFileName(child.Name));

                            // Extract raw data
                            if (child.Offset >= 0 && child.Size > 0 && child.Offset + child.Size <= source.Length)
                            {
                                source.Seek(child.Offset, SeekOrigin.Begin);
                                byte[] data = new byte[child.Size];
                                int read = source.Read(data, 0, (int)child.Size);

                                // Route to specialized extractor based on type
                                switch (child.FormatName)
                                {
                                    case "Texture2D":
                                        using (var ms = new MemoryStream(data, 0, read))
                                        {
                                            var texResult = UnityTextureExtractor.ExportTexture2D(child, ms, childPath + ".dds", options);
                                            if (texResult.Success) exported++;
                                        }
                                        break;

                                    case "Mesh":
                                        using (var ms = new MemoryStream(data, 0, read))
                                        {
                                            var meshResult = UnityMeshExtractor.ExportMesh(child, ms, childPath + ".obj", options);
                                            if (meshResult.Success) exported++;
                                        }
                                        break;

                                    default:
                                        string ext = GetExtensionForClass(child.FormatName);
                                        File.WriteAllBytes(childPath + ext, data);
                                        exported++;
                                        break;
                                }
                            }
                        }
                        catch { /* Skip failed children */ }
                    }

                    return ExportResult.Succeeded(baseDir, exported);
                }

                // Single file export
                source.Seek(entry.Offset, SeekOrigin.Begin);
                long size = entry.Size > 0 ? entry.Size : source.Length - entry.Offset;
                byte[] buffer = new byte[size];
                int bytesRead = source.Read(buffer, 0, (int)size);
                File.WriteAllBytes(outputPath, buffer);

                return ExportResult.Succeeded(outputPath, bytesRead);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed(ex.Message);
            }
        }

        #endregion

        #region Helpers

        private struct BlockInfo
        {
            public int UncompressedSize;
            public int CompressedSize;
            public short Flags;
        }

        private struct SerializedType
        {
            public int ClassId;
            public bool IsStrippedType;
            public short ScriptTypeIndex;
        }

        private struct ObjectInfo
        {
            public long FileId;
            public long DataOffset;
            public int Size;
            public int TypeIndex;
            public int ClassId;
        }

        private SerializedType ReadSerializedType(BinaryReader reader, int version, bool enableTypeTree, bool bigEndian)
        {
            var type = new SerializedType();

            type.ClassId = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();

            if (version >= 16)
                type.IsStrippedType = reader.ReadByte() != 0;

            if (version >= 17)
                type.ScriptTypeIndex = bigEndian ? ReadInt16BE(reader) : reader.ReadInt16();

            // Type tree hash
            if (version >= 13)
            {
                if ((version < 16 && type.ClassId < 0) ||
                    (version >= 16 && type.ClassId == 114) ||
                    (version >= 17 && type.IsStrippedType && type.ScriptTypeIndex >= 0))
                {
                    reader.ReadBytes(32); // script hash + type hash
                }
                else
                {
                    reader.ReadBytes(16); // type hash only
                }
            }

            // Skip type tree if present
            if (enableTypeTree)
            {
                SkipTypeTree(reader, version, bigEndian);
            }

            return type;
        }

        private void SkipTypeTree(BinaryReader reader, int version, bool bigEndian)
        {
            if (version >= 12) // blob format
            {
                int nodeCount = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
                int stringBufferSize = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();

                // Each node: version(2) + depth(1) + typeFlags(1) + typeStrOffset(4) + nameStrOffset(4) + size(4) + index(4) + metaFlag(4) = 24 bytes
                // version >= 19 adds refTypeHash(8) = 32 bytes
                int nodeSize = version >= 19 ? 32 : 24;
                reader.ReadBytes(nodeCount * nodeSize);
                reader.ReadBytes(stringBufferSize);
            }
            else
            {
                // Recursive type tree - read until done
                SkipTypeTreeNode(reader, version, bigEndian);
            }
        }

        private void SkipTypeTreeNode(BinaryReader reader, int version, bool bigEndian)
        {
            ReadNullTerminated(reader); // type
            ReadNullTerminated(reader); // name

            int size = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            int index = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            int isArray = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            int version2 = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            int metaFlag = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();

            int childCount = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            for (int i = 0; i < childCount; i++)
                SkipTypeTreeNode(reader, version, bigEndian);
        }

        private ObjectInfo ReadObjectInfo(BinaryReader reader, int version, bool bigEndian)
        {
            var info = new ObjectInfo();

            if (version < 14)
            {
                info.FileId = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            }
            else
            {
                Align(reader, 4);
                info.FileId = bigEndian ? ReadInt64BE(reader) : reader.ReadInt64();
            }

            if (version >= 22)
            {
                info.DataOffset = bigEndian ? ReadInt64BE(reader) : reader.ReadInt64();
            }
            else
            {
                info.DataOffset = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            }

            info.Size = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();
            info.TypeIndex = bigEndian ? ReadInt32BE(reader) : reader.ReadInt32();

            if (version < 16)
            {
                info.ClassId = bigEndian ? ReadInt16BE(reader) : reader.ReadInt16();
            }
            else
            {
                info.ClassId = info.TypeIndex;
            }

            if (version < 11)
            {
                reader.ReadInt16(); // isDestroyed
            }

            if (version >= 11 && version < 17)
            {
                reader.ReadInt16(); // scriptTypeIndex
            }

            if (version == 15 || version == 16)
            {
                reader.ReadByte(); // stripped
            }

            return info;
        }

        private void Align(BinaryReader reader, int alignment)
        {
            long pos = reader.BaseStream.Position;
            long mod = pos % alignment;
            if (mod != 0)
                reader.ReadBytes((int)(alignment - mod));
        }

        private string ReadNullTerminated(BinaryReader reader)
        {
            var sb = new StringBuilder();
            byte b;
            int maxLen = 1024;
            while (maxLen-- > 0 && (b = reader.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        private int ReadInt32BE(BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(4);
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        private long ReadInt64BE(BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(8);
            return ((long)b[0] << 56) | ((long)b[1] << 48) | ((long)b[2] << 40) | ((long)b[3] << 32) |
                   ((long)b[4] << 24) | ((long)b[5] << 16) | ((long)b[6] << 8) | b[7];
        }

        private short ReadInt16BE(BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(2);
            return (short)((b[0] << 8) | b[1]);
        }

        private string GetCompressionName(int type)
        {
            switch (type)
            {
                case 0: return "None";
                case 1: return "LZMA";
                case 2: return "LZ4";
                case 3: return "LZ4HC";
                default: return $"Unknown ({type})";
            }
        }

        private string GetPlatformName(int platform)
        {
            switch (platform)
            {
                case 0: return "StandaloneOSX";
                case 5: return "StandaloneWindows";
                case 9: return "iOS";
                case 11: return "Android";
                case 13: return "StandaloneLinux";
                case 19: return "StandaloneWindows64";
                case 21: return "WebGL";
                case 24: return "WSAPlayer";
                case 25: return "StandaloneLinux64";
                case 29: return "Switch";
                case 31: return "PS5";
                case 38: return "Xbox Series";
                default: return $"Unknown ({platform})";
            }
        }

        private string GetExtensionForClass(string className)
        {
            switch (className)
            {
                case "Texture2D": return ".tex";
                case "Mesh": return ".mesh";
                case "AudioClip": return ".audio";
                case "AnimationClip": return ".anim";
                case "Shader": return ".shader";
                case "TextAsset": return ".txt";
                case "Font": return ".font";
                default: return ".bin";
            }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        #endregion
    }
}
