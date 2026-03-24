using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unity
{
    /// <summary>
    /// Parser for Unity asset bundles (.unity3d) and serialized asset files (.assets).
    /// Supports UnityFS, UnityWeb, and UnityRaw bundle formats as well as standalone
    /// serialized files. Extracts embedded objects (textures, meshes, audio, etc.)
    /// with full metadata including Unity ClassID mapping.
    /// </summary>
    public class UnityAssetParser : IAssetParser
    {
        public AssetType Type => AssetType.Container;

        private const int MIN_HEADER_SIZE = 16;
        private const int MAX_REASONABLE_OBJECTS = 500000;

        // Unity asset bundle magic signatures
        private const string MAGIC_UNITY_FS = "UnityFS";
        private const string MAGIC_UNITY_WEB = "UnityWeb";
        private const string MAGIC_UNITY_RAW = "UnityRaw";

        // Compression flags in UnityFS block info
        private const int COMPRESSION_NONE = 0;
        private const int COMPRESSION_LZMA = 1;
        private const int COMPRESSION_LZ4 = 2;
        private const int COMPRESSION_LZ4HC = 3;
        private const int COMPRESSION_MASK = 0x3F;

        // Block info flags
        private const int BLOCK_INFO_FLAG_COMBINED = 0x40;
        private const int BLOCK_INFO_FLAG_AT_END = 0x80;

        #region Unity ClassID Mapping

        private static readonly Dictionary<int, (string ClassName, AssetType Type)> ClassIdMap =
            new Dictionary<int, (string, AssetType)>
            {
                { 1,   ("GameObject",           AssetType.Data) },
                { 4,   ("Transform",            AssetType.Data) },
                { 20,  ("Camera",               AssetType.Data) },
                { 21,  ("Material",             AssetType.Data) },
                { 23,  ("MeshRenderer",         AssetType.Data) },
                { 28,  ("Texture2D",            AssetType.Texture) },
                { 33,  ("MeshCollider",         AssetType.Data) },
                { 43,  ("Mesh",                 AssetType.Model) },
                { 48,  ("Shader",               AssetType.Data) },
                { 49,  ("TextAsset",            AssetType.Data) },
                { 74,  ("AnimationClip",        AssetType.Animation) },
                { 82,  ("AudioSource",          AssetType.Data) },
                { 83,  ("AudioClip",            AssetType.Audio) },
                { 108, ("Light",                AssetType.Data) },
                { 114, ("MonoBehaviour",         AssetType.Data) },
                { 115, ("MonoScript",           AssetType.Data) },
                { 128, ("Font",                 AssetType.Data) },
                { 137, ("Cubemap",              AssetType.Texture) },
                { 142, ("AssetBundle",          AssetType.Container) },
                { 150, ("PreloadData",          AssetType.Data) },
                { 152, ("MovieTexture",         AssetType.Video) },
                { 156, ("TerrainData",          AssetType.Data) },
                { 187, ("MeshFilter",           AssetType.Data) },
                { 213, ("Sprite",               AssetType.Texture) },
                { 241, ("AudioMixerController", AssetType.Audio) },
            };

        #endregion

        #region Internal Data Structures

        private struct BlockInfo
        {
            public uint UncompressedSize;
            public uint CompressedSize;
            public ushort Flags;
        }

        private struct DirectoryNode
        {
            public long Offset;
            public long Size;
            public uint Flags;
            public string Path;
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
            public uint Size;
            public int TypeIndex;
            public int ClassId;
        }

        #endregion

        #region Detection

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream == null || stream.Length < MIN_HEADER_SIZE)
                return false;

            long savedPosition = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);

                // Check for asset bundle signatures
                byte[] header = new byte[Math.Min(16, (int)stream.Length)];
                stream.Read(header, 0, header.Length);

                string headerStr = Encoding.ASCII.GetString(header, 0, Math.Min(header.Length, 10));
                if (headerStr.StartsWith(MAGIC_UNITY_FS) ||
                    headerStr.StartsWith(MAGIC_UNITY_WEB) ||
                    headerStr.StartsWith(MAGIC_UNITY_RAW))
                {
                    return true;
                }

                // Check by extension for standalone .assets files
                string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                if (ext == ".assets" || ext == ".unity3d" || ext == ".bundle")
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    return ValidateSerializedHeader(stream);
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                stream.Seek(savedPosition, SeekOrigin.Begin);
            }
        }

        private bool ValidateSerializedHeader(Stream stream)
        {
            if (stream.Length < 20)
                return false;

            try
            {
                using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
                {
                    uint metadataSize = reader.ReadUInt32();
                    uint fileSize = reader.ReadUInt32();
                    uint version = reader.ReadUInt32();

                    // Metadata size should be positive and less than file size
                    if (metadataSize == 0 || metadataSize > stream.Length)
                        return false;

                    // Version should be reasonable (Unity uses versions ~5-30)
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

            byte[] header = new byte[Math.Min(16, (int)stream.Length)];
            stream.Read(header, 0, header.Length);
            string headerStr = Encoding.ASCII.GetString(header, 0, Math.Min(header.Length, 10));

            if (headerStr.StartsWith(MAGIC_UNITY_FS) ||
                headerStr.StartsWith(MAGIC_UNITY_WEB) ||
                headerStr.StartsWith(MAGIC_UNITY_RAW))
            {
                stream.Seek(0, SeekOrigin.Begin);
                return ParseAssetBundle(stream, fileName);
            }

            stream.Seek(0, SeekOrigin.Begin);
            return ParseSerializedFile(stream, fileName);
        }

        #endregion

        #region Asset Bundle Parsing

        private AssetEntry ParseAssetBundle(Stream stream, string fileName)
        {
            var entry = new AssetEntry(Path.GetFileName(fileName), AssetType.Container)
            {
                SourcePath = fileName,
                Size = stream.Length
            };

            try
            {
                // Read null-terminated signature
                string signature = ReadNullTerminatedString(stream);
                entry.FormatName = $"Unity Asset Bundle ({signature})";
                entry.Metadata["Signature"] = signature;

                using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
                {
                    uint formatVersion = reader.ReadUInt32();
                    entry.Metadata["FormatVersion"] = (int)formatVersion;

                    // Unity version and generator version are null-terminated strings
                    string unityVersion = reader.ReadNullTerminatedString();
                    string generatorVersion = reader.ReadNullTerminatedString();
                    entry.Metadata["UnityVersion"] = unityVersion;
                    entry.Metadata["GeneratorVersion"] = generatorVersion;

                    if (signature == MAGIC_UNITY_FS)
                    {
                        ParseUnityFSBundle(reader, entry);
                    }
                    else
                    {
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

        private void ParseUnityFSBundle(EndianBinaryReader reader, AssetEntry entry)
        {
            long totalFileSize = reader.ReadInt64();
            uint compressedBlockInfoSize = reader.ReadUInt32();
            uint uncompressedBlockInfoSize = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();

            int compressionType = (int)(flags & COMPRESSION_MASK);
            bool blockInfoAtEnd = (flags & BLOCK_INFO_FLAG_AT_END) != 0;

            entry.Metadata["TotalFileSize"] = totalFileSize;
            entry.Metadata["CompressedBlockInfoSize"] = (int)compressedBlockInfoSize;
            entry.Metadata["UncompressedBlockInfoSize"] = (int)uncompressedBlockInfoSize;
            entry.Metadata["CompressionType"] = GetCompressionName(compressionType);
            entry.Metadata["BlockInfoAtEnd"] = blockInfoAtEnd;

            long headerEndPosition = reader.Position;

            // Only parse block info when uncompressed
            if (compressionType != COMPRESSION_NONE)
            {
                entry.Metadata["Note"] =
                    $"Block info is {GetCompressionName(compressionType)} compressed; " +
                    "full enumeration requires decompression";
                return;
            }

            // Read block info
            long blockInfoPosition = blockInfoAtEnd
                ? reader.Length - compressedBlockInfoSize
                : reader.Position;

            reader.Seek(blockInfoPosition);

            // 16-byte uncompressed data hash
            reader.Skip(16);

            int blockCount = reader.ReadInt32();
            entry.Metadata["BlockCount"] = blockCount;

            if (blockCount <= 0 || blockCount > 10000)
                return;

            var blocks = new List<BlockInfo>();
            for (int i = 0; i < blockCount; i++)
            {
                blocks.Add(new BlockInfo
                {
                    UncompressedSize = reader.ReadUInt32(),
                    CompressedSize = reader.ReadUInt32(),
                    Flags = reader.ReadUInt16()
                });
            }

            // Read directory entries
            int nodeCount = reader.ReadInt32();
            entry.Metadata["NodeCount"] = nodeCount;

            if (nodeCount <= 0 || nodeCount > MAX_REASONABLE_OBJECTS)
                return;

            // Calculate absolute data start: after header, after block info (if not at end)
            long dataStart = blockInfoAtEnd
                ? headerEndPosition
                : headerEndPosition + compressedBlockInfoSize;

            for (int i = 0; i < nodeCount; i++)
            {
                long nodeOffset = reader.ReadInt64();
                long nodeSize = reader.ReadInt64();
                uint nodeFlags = reader.ReadUInt32();
                string nodeName = reader.ReadNullTerminatedString();

                if (string.IsNullOrEmpty(nodeName))
                    nodeName = $"node_{i}";

                long absoluteOffset = dataStart + nodeOffset;

                // Try to parse embedded serialized files from uncompressed blocks
                AssetEntry childEntry;
                if (absoluteOffset >= 0 && absoluteOffset + nodeSize <= reader.Length)
                {
                    try
                    {
                        childEntry = ParseEmbeddedSerializedFile(
                            reader.BaseStream, nodeName, absoluteOffset, nodeSize);
                    }
                    catch
                    {
                        childEntry = new AssetEntry(nodeName, AssetType.Data)
                        {
                            SourcePath = entry.SourcePath,
                            Offset = absoluteOffset,
                            Size = nodeSize,
                            FormatName = "Unity Raw Data"
                        };
                    }
                }
                else
                {
                    childEntry = new AssetEntry(nodeName, AssetType.Data)
                    {
                        SourcePath = entry.SourcePath,
                        Offset = nodeOffset,
                        Size = nodeSize,
                        FormatName = "Unity Serialized File"
                    };
                }

                childEntry.Metadata["NodeFlags"] = (int)nodeFlags;
                childEntry.Metadata["BundleEntryOffset"] = nodeOffset;
                childEntry.Metadata["BundleEntrySize"] = nodeSize;
                entry.Children.Add(childEntry);
            }
        }

        private void ParseLegacyBundle(EndianBinaryReader reader, AssetEntry entry, uint formatVersion)
        {
            try
            {
                uint minimumStreamedBytes = reader.ReadUInt32();
                uint headerSize = reader.ReadUInt32();
                uint numberOfLevelsToDownload = reader.ReadUInt32();
                uint levelCount = reader.ReadUInt32();

                entry.Metadata["HeaderSize"] = (int)headerSize;
                entry.Metadata["LevelCount"] = (int)levelCount;

                if (levelCount > 0 && levelCount < 100)
                {
                    for (int i = 0; i < levelCount; i++)
                    {
                        uint compressedSize = reader.ReadUInt32();
                        uint uncompressedSize = reader.ReadUInt32();
                        entry.Metadata[$"Level{i}_CompressedSize"] = (int)compressedSize;
                        entry.Metadata[$"Level{i}_UncompressedSize"] = (int)uncompressedSize;
                    }
                }

                if (formatVersion >= 2)
                {
                    uint completeFileSize = reader.ReadUInt32();
                    entry.Metadata["CompleteFileSize"] = (int)completeFileSize;
                }

                if (formatVersion >= 3)
                {
                    uint dataHeaderSize = reader.ReadUInt32();
                    entry.Metadata["DataHeaderSize"] = (int)dataHeaderSize;
                }
            }
            catch (Exception ex)
            {
                entry.Metadata["LegacyParseError"] = ex.Message;
            }
        }

        #endregion

        #region Serialized File Parsing

        private AssetEntry ParseSerializedFile(Stream stream, string fileName)
        {
            return ParseEmbeddedSerializedFile(stream, Path.GetFileName(fileName), 0, stream.Length);
        }

        private AssetEntry ParseEmbeddedSerializedFile(Stream stream, string name, long baseOffset, long fileSize)
        {
            var entry = new AssetEntry(name, AssetType.Container)
            {
                SourcePath = name,
                Offset = baseOffset,
                Size = fileSize,
                FormatName = "Unity Serialized File"
            };

            try
            {
                // Read the fixed header (always big-endian)
                using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
                {
                    reader.Seek(baseOffset);

                    uint metadataSize = reader.ReadUInt32();
                    uint fileSizeSmall = reader.ReadUInt32();
                    uint version = reader.ReadUInt32();
                    uint dataOffsetSmall = reader.ReadUInt32();

                    entry.Metadata["MetadataSize"] = (int)metadataSize;
                    entry.Metadata["SerializedVersion"] = (int)version;
                    entry.FormatName = $"Unity Serialized File (v{version})";

                    bool bigEndian;
                    if (version >= 9)
                    {
                        bigEndian = reader.ReadByte() != 0;
                        reader.Skip(3); // reserved padding
                    }
                    else
                    {
                        bigEndian = true;
                    }

                    entry.Metadata["BigEndian"] = bigEndian;

                    long headerFileSize;
                    long dataOffset;

                    if (version >= 22)
                    {
                        // Large file support: re-read extended header fields
                        metadataSize = reader.ReadUInt32();
                        headerFileSize = reader.ReadInt64();
                        dataOffset = reader.ReadInt64();
                        reader.ReadInt64(); // unknown
                        entry.Metadata["MetadataSize"] = (int)metadataSize;
                    }
                    else
                    {
                        headerFileSize = fileSizeSmall;
                        dataOffset = dataOffsetSmall;
                    }

                    entry.Metadata["DataOffset"] = dataOffset;

                    // Switch to correct endianness for the rest of the metadata
                    long metadataStart = reader.Position;
                    ParseSerializedMetadata(stream, entry, baseOffset, (int)version,
                        bigEndian, dataOffset, metadataStart);
                }
            }
            catch (Exception ex)
            {
                entry.Metadata["ParseError"] = ex.Message;
            }

            return entry;
        }

        private void ParseSerializedMetadata(Stream stream, AssetEntry entry, long baseOffset,
            int version, bool bigEndian, long dataOffset, long metadataStart)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: bigEndian, leaveOpen: true))
            {
                reader.Seek(metadataStart);

                // Unity version string (null-terminated)
                if (version >= 7)
                {
                    string unityVersion = reader.ReadNullTerminatedString();
                    entry.Metadata["UnityVersion"] = unityVersion;
                }

                // Target platform
                if (version >= 8)
                {
                    uint platform = reader.ReadUInt32();
                    entry.Metadata["Platform"] = GetPlatformName((int)platform);
                }

                // Type tree enabled flag
                bool hasTypeTree = true;
                if (version >= 13)
                {
                    hasTypeTree = reader.ReadByte() != 0;
                    entry.Metadata["HasTypeTree"] = hasTypeTree;
                }

                // Read type entries
                int typeCount = reader.ReadInt32();
                entry.Metadata["TypeCount"] = typeCount;

                if (typeCount <= 0 || typeCount > 10000)
                    return;

                var types = new List<SerializedType>();
                for (int i = 0; i < typeCount; i++)
                {
                    var type = ReadSerializedType(reader, version, hasTypeTree);
                    types.Add(type);
                }

                // Read object info table
                int objectCount = reader.ReadInt32();
                entry.Metadata["ObjectCount"] = objectCount;

                if (objectCount <= 0 || objectCount > MAX_REASONABLE_OBJECTS)
                    return;

                for (int i = 0; i < objectCount; i++)
                {
                    try
                    {
                        var objInfo = ReadObjectInfo(reader, version);

                        // Resolve class ID from the type table
                        int classId = objInfo.ClassId;
                        if (objInfo.TypeIndex >= 0 && objInfo.TypeIndex < types.Count)
                            classId = types[objInfo.TypeIndex].ClassId;

                        string className;
                        AssetType assetType;
                        if (ClassIdMap.TryGetValue(classId, out var mapping))
                        {
                            className = mapping.ClassName;
                            assetType = mapping.Type;
                        }
                        else
                        {
                            className = $"ClassID_{classId}";
                            assetType = AssetType.Data;
                        }

                        long absoluteObjectOffset = baseOffset + dataOffset + objInfo.DataOffset;

                        // Attempt to read the object name from serialized data
                        string objectName = TryReadObjectName(stream, absoluteObjectOffset, objInfo.Size, bigEndian);
                        if (string.IsNullOrEmpty(objectName))
                            objectName = $"{className}_{i}";

                        var child = new AssetEntry(objectName, assetType)
                        {
                            SourcePath = entry.SourcePath,
                            Offset = absoluteObjectOffset,
                            Size = objInfo.Size,
                            FormatName = $"Unity {className}"
                        };

                        child.Metadata["ClassId"] = classId;
                        child.Metadata["ClassName"] = className;
                        child.Metadata["FileId"] = objInfo.FileId;
                        child.Metadata["ObjectDataOffset"] = absoluteObjectOffset;
                        child.Metadata["ObjectDataSize"] = (int)objInfo.Size;
                        child.Metadata["LocalOffset"] = objInfo.DataOffset;
                        child.Metadata["TypeIndex"] = objInfo.TypeIndex;

                        entry.Children.Add(child);
                    }
                    catch
                    {
                        // Corrupt object entry; stop parsing remaining objects
                        break;
                    }
                }
            }
        }

        private SerializedType ReadSerializedType(EndianBinaryReader reader, int version, bool hasTypeTree)
        {
            var type = new SerializedType();

            type.ClassId = reader.ReadInt32();

            if (version >= 16)
                type.IsStrippedType = reader.ReadByte() != 0;

            if (version >= 17)
                type.ScriptTypeIndex = reader.ReadInt16();

            // Type hashes
            if (version >= 13)
            {
                bool hasScriptHash =
                    (version < 16 && type.ClassId < 0) ||
                    (version >= 16 && type.ClassId == 114) ||
                    (version >= 17 && type.ScriptTypeIndex >= 0);

                if (hasScriptHash)
                    reader.Skip(32); // 16-byte script hash + 16-byte type hash
                else
                    reader.Skip(16); // 16-byte type hash only
            }

            // Skip the type tree blob if present
            if (hasTypeTree)
                SkipTypeTree(reader, version);

            return type;
        }

        private void SkipTypeTree(EndianBinaryReader reader, int version)
        {
            if (version >= 12)
            {
                // Blob format: node count + string buffer size + raw data
                int nodeCount = reader.ReadInt32();
                int stringBufferSize = reader.ReadInt32();

                // Each node: 24 bytes for v12-v18, 32 bytes for v19+
                int nodeSize = version >= 19 ? 32 : 24;
                reader.Skip((long)nodeCount * nodeSize);
                reader.Skip(stringBufferSize);
            }
            else
            {
                // Recursive format
                SkipOldTypeTreeNode(reader);
            }
        }

        private void SkipOldTypeTreeNode(EndianBinaryReader reader)
        {
            reader.ReadNullTerminatedString(); // type name
            reader.ReadNullTerminatedString(); // field name
            reader.ReadInt32(); // size
            reader.ReadInt32(); // index
            reader.ReadInt32(); // isArray
            reader.ReadInt32(); // version
            reader.ReadInt32(); // metaFlags

            int childCount = reader.ReadInt32();
            for (int i = 0; i < childCount; i++)
                SkipOldTypeTreeNode(reader);
        }

        private ObjectInfo ReadObjectInfo(EndianBinaryReader reader, int version)
        {
            var info = new ObjectInfo();

            if (version >= 14)
            {
                reader.Align(4);
                info.FileId = reader.ReadInt64();
            }
            else
            {
                info.FileId = reader.ReadInt32();
            }

            if (version >= 22)
                info.DataOffset = reader.ReadInt64();
            else
                info.DataOffset = reader.ReadUInt32();

            info.Size = reader.ReadUInt32();
            info.TypeIndex = reader.ReadInt32();

            if (version < 16)
                info.ClassId = reader.ReadInt16();
            else
                info.ClassId = info.TypeIndex;

            if (version < 11)
                reader.ReadUInt16(); // isDestroyed

            if (version >= 11 && version < 17)
                reader.ReadInt16(); // scriptTypeIndex

            if (version == 15 || version == 16)
                reader.ReadByte(); // stripped

            return info;
        }

        /// <summary>
        /// Attempts to read the name of a Unity object from its serialized data.
        /// Many Unity object types begin with an int32 length-prefixed UTF8 string name.
        /// </summary>
        private string TryReadObjectName(Stream stream, long absoluteOffset, uint size, bool bigEndian)
        {
            if (size < 8 || absoluteOffset < 0 || absoluteOffset >= stream.Length)
                return null;

            long savedPosition = stream.Position;
            try
            {
                stream.Seek(absoluteOffset, SeekOrigin.Begin);

                using (var reader = new EndianBinaryReader(stream, bigEndian: bigEndian, leaveOpen: true))
                {
                    int nameLength = reader.ReadInt32();
                    if (nameLength <= 0 || nameLength > 256 || nameLength > size - 4)
                        return null;

                    string name = reader.ReadString(nameLength);
                    if (string.IsNullOrWhiteSpace(name))
                        return null;

                    // Validate: name should only contain printable ASCII
                    foreach (char c in name)
                    {
                        if (c < 0x20 || c > 0x7E)
                            return null;
                    }

                    return name;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                stream.Seek(savedPosition, SeekOrigin.Begin);
            }
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

                // Container entries: extract all children into a subdirectory
                if (entry.Children.Count > 0)
                {
                    string baseName = Path.GetFileNameWithoutExtension(outputPath);
                    string extractDir = Path.Combine(dir ?? ".", baseName + "_extracted");
                    Directory.CreateDirectory(extractDir);

                    int exportedCount = 0;
                    foreach (var child in entry.Children)
                    {
                        try
                        {
                            if (child.Offset < 0 || child.Size <= 0 ||
                                child.Offset + child.Size > source.Length)
                                continue;

                            string safeName = SanitizeFileName(child.Name);
                            string extension = GetExtensionForClass(
                                child.Metadata.ContainsKey("ClassName")
                                    ? (string)child.Metadata["ClassName"]
                                    : "unknown");
                            string childPath = Path.Combine(extractDir, safeName + extension);

                            source.Seek(child.Offset, SeekOrigin.Begin);
                            byte[] data = new byte[child.Size];
                            ReadFully(source, data, 0, (int)child.Size);
                            File.WriteAllBytes(childPath, data);
                            exportedCount++;
                        }
                        catch
                        {
                            // Skip failed children and continue with the rest
                        }
                    }

                    return ExportResult.Succeeded(extractDir, exportedCount);
                }

                // Single entry: extract raw object data
                if (entry.Metadata.ContainsKey("ObjectDataOffset") &&
                    entry.Metadata.ContainsKey("ObjectDataSize"))
                {
                    long dataOffset = Convert.ToInt64(entry.Metadata["ObjectDataOffset"]);
                    int dataSize = Convert.ToInt32(entry.Metadata["ObjectDataSize"]);

                    if (dataOffset < 0 || dataOffset + dataSize > source.Length)
                    {
                        return ExportResult.Failed(
                            $"Object data range (0x{dataOffset:X}..0x{dataOffset + dataSize:X}) " +
                            $"exceeds source stream length ({source.Length} bytes).");
                    }

                    string extension = GetExtensionForClass(
                        entry.Metadata.ContainsKey("ClassName")
                            ? (string)entry.Metadata["ClassName"]
                            : "unknown");
                    string finalPath = Path.ChangeExtension(outputPath, extension);

                    source.Seek(dataOffset, SeekOrigin.Begin);
                    byte[] objectData = new byte[dataSize];
                    ReadFully(source, objectData, 0, dataSize);
                    File.WriteAllBytes(finalPath, objectData);

                    return ExportResult.Succeeded(finalPath, dataSize);
                }

                // Fallback: export the entire stream region
                long offset = entry.Offset >= 0 ? entry.Offset : 0;
                long size = entry.Size > 0 ? entry.Size : source.Length - offset;

                source.Seek(offset, SeekOrigin.Begin);
                byte[] buffer = new byte[size];
                ReadFully(source, buffer, 0, (int)size);
                File.WriteAllBytes(outputPath, buffer);

                return ExportResult.Succeeded(outputPath, (int)size);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Failed to export Unity asset: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        private static string ReadNullTerminatedString(Stream stream)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = stream.ReadByte()) > 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream: expected {count} bytes, read {totalRead}.");
                totalRead += read;
            }
        }

        private static string GetCompressionName(int type)
        {
            switch (type)
            {
                case COMPRESSION_NONE:  return "None";
                case COMPRESSION_LZMA:  return "LZMA";
                case COMPRESSION_LZ4:   return "LZ4";
                case COMPRESSION_LZ4HC: return "LZ4HC";
                default:                return $"Unknown (0x{type:X2})";
            }
        }

        private static string GetPlatformName(int platform)
        {
            switch (platform)
            {
                case 0:  return "StandaloneOSX";
                case 5:  return "StandaloneWindows";
                case 9:  return "iOS";
                case 11: return "Android";
                case 13: return "StandaloneLinux";
                case 19: return "StandaloneWindows64";
                case 21: return "WebGL";
                case 24: return "WSAPlayer";
                case 25: return "StandaloneLinux64";
                case 29: return "Switch";
                case 31: return "PS5";
                case 38: return "Xbox Series";
                default: return $"Platform_{platform}";
            }
        }

        private static string GetExtensionForClass(string className)
        {
            switch (className)
            {
                case "Texture2D":     return ".tex";
                case "Mesh":          return ".mesh";
                case "AudioClip":     return ".audioclip";
                case "AnimationClip": return ".anim";
                case "Shader":        return ".shader";
                case "Material":      return ".mat";
                case "TextAsset":     return ".txt";
                case "Font":          return ".font";
                case "Sprite":        return ".sprite";
                case "GameObject":    return ".go";
                case "MovieTexture":  return ".movie";
                default:              return ".bin";
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        #endregion
    }
}
