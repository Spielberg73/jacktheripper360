using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins
{
    /// <summary>
    /// Plugin for parsing Unreal Engine 3 package files (.upk, .u, .umap, .uasset).
    /// Many Xbox 360 games used UE3 (Gears of War, Mass Effect, Batman Arkham, etc.)
    /// </summary>
    public class UnrealEngine3Plugin : IGamePlugin
    {
        public string Id => "unreal_engine_3";
        public string GameName => "Unreal Engine 3 Games";
        public IReadOnlyList<string> SupportedTitleIds => new string[0]; // Generic, not game-specific
        public IReadOnlyList<string> SupportedExtensions => new[] { ".upk", ".u", ".umap", ".uasset", ".xxx" };

        public IReadOnlyList<IAssetParser> GetParsers() => new IAssetParser[] { new UpkParser() };
        public IReadOnlyList<IContainerReader> GetContainerReaders() => new IContainerReader[] { new UpkContainerReader() };

        public bool CanHandle(string filePath, byte[] headerBytes)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext == ".upk" || ext == ".u" || ext == ".umap" || ext == ".uasset")
                return true;

            // Check for UE3 package magic (0x9E2A83C1)
            if (headerBytes != null && headerBytes.Length >= 4)
            {
                uint magic = (uint)(headerBytes[0] | (headerBytes[1] << 8) | (headerBytes[2] << 16) | (headerBytes[3] << 24));
                return magic == 0x9E2A83C1;
            }
            return false;
        }
    }

    /// <summary>
    /// Parser for Unreal Engine 3 package files.
    /// </summary>
    public class UpkParser : IAssetParser
    {
        public AssetType Type => AssetType.Archive;
        private const uint UPK_MAGIC = 0x9E2A83C1;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 4) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);
            uint m = (uint)(magic[0] | (magic[1] << 8) | (magic[2] << 16) | (magic[3] << 24));
            return m == UPK_MAGIC;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                uint magic = reader.ReadUInt32();
                ushort fileVersion = reader.ReadUInt16();
                ushort licenseeVersion = reader.ReadUInt16();
                int headerSize = reader.ReadInt32();

                // Read package group (folder) name
                int groupNameLength = reader.ReadInt32();
                string groupName = "";
                if (groupNameLength > 0 && groupNameLength < 1024)
                {
                    groupName = reader.ReadString(groupNameLength).TrimEnd('\0');
                }
                else if (groupNameLength < 0)
                {
                    // Unicode string
                    int uLen = -groupNameLength;
                    if (uLen < 1024)
                    {
                        byte[] uBytes = reader.ReadBytes(uLen * 2);
                        groupName = System.Text.Encoding.Unicode.GetString(uBytes).TrimEnd('\0');
                    }
                }

                uint packageFlags = reader.ReadUInt32();

                // Read tables info
                int nameCount = reader.ReadInt32();
                int nameOffset = reader.ReadInt32();
                int exportCount = reader.ReadInt32();
                int exportOffset = reader.ReadInt32();
                int importCount = reader.ReadInt32();
                int importOffset = reader.ReadInt32();

                var entry = new AssetEntry(fileName, AssetType.Archive)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"UE3 Package (v{fileVersion})"
                };

                entry.Metadata["FileVersion"] = (int)fileVersion;
                entry.Metadata["LicenseeVersion"] = (int)licenseeVersion;
                entry.Metadata["GroupName"] = groupName;
                entry.Metadata["NameCount"] = nameCount;
                entry.Metadata["ExportCount"] = exportCount;
                entry.Metadata["ImportCount"] = importCount;

                // Parse name table
                if (nameOffset > 0 && nameOffset < stream.Length)
                {
                    reader.Seek(nameOffset);
                    var names = new List<string>();
                    for (int i = 0; i < Math.Min(nameCount, 10000); i++)
                    {
                        if (reader.Position >= stream.Length - 4) break;

                        int nameLen = reader.ReadInt32();
                        if (nameLen <= 0 || nameLen > 1024) break;

                        string name = reader.ReadString(nameLen).TrimEnd('\0');
                        names.Add(name);

                        // Skip object flags (UE3 version dependent)
                        if (fileVersion >= 141)
                            reader.Skip(8);
                        else
                            reader.Skip(4);
                    }

                    // Parse export table to find individual assets
                    if (exportOffset > 0 && exportOffset < stream.Length)
                    {
                        reader.Seek(exportOffset);
                        for (int i = 0; i < Math.Min(exportCount, 10000); i++)
                        {
                            if (reader.Position >= stream.Length - 28) break;

                            int classIndex = ReadCompactIndex(reader);
                            int superIndex = ReadCompactIndex(reader);
                            int packageIndex = reader.ReadInt32();
                            int objectNameIndex = ReadCompactIndex(reader);
                            reader.Skip(4); // archetype
                            reader.Skip(8); // object flags (64-bit in UE3)
                            int serialSize = reader.ReadInt32();
                            int serialOffset = reader.ReadInt32();

                            string objName = (objectNameIndex >= 0 && objectNameIndex < names.Count)
                                ? names[objectNameIndex] : $"export_{i}";

                            string className = "";
                            if (classIndex != 0)
                            {
                                int absClass = classIndex > 0 ? classIndex - 1 : -classIndex - 1;
                                // Would need import table to resolve class names fully
                            }

                            if (serialSize > 0)
                            {
                                AssetType childType = GuessTypeFromName(objName);
                                var child = new AssetEntry(objName, childType)
                                {
                                    SourcePath = $"{fileName}:{objName}",
                                    Offset = serialOffset,
                                    Size = serialSize,
                                    FormatName = $"UE3 Export"
                                };
                                entry.Children.Add(child);
                            }

                            // Skip remaining fields (version dependent)
                            if (fileVersion >= 220)
                                reader.Skip(4); // export flags
                            if (fileVersion >= 247)
                                reader.Skip(12); // net objects + GUID
                        }
                    }
                }

                return entry;
            }
        }

        private static int ReadCompactIndex(EndianBinaryReader reader)
        {
            int result = 0;
            bool negative = false;

            byte b = reader.ReadByte();
            negative = (b & 0x80) != 0;
            result = b & 0x3F;

            if ((b & 0x40) != 0)
            {
                b = reader.ReadByte();
                result |= (b & 0x7F) << 6;

                if ((b & 0x80) != 0)
                {
                    b = reader.ReadByte();
                    result |= (b & 0x7F) << 13;

                    if ((b & 0x80) != 0)
                    {
                        b = reader.ReadByte();
                        result |= (b & 0x7F) << 20;

                        if ((b & 0x80) != 0)
                        {
                            b = reader.ReadByte();
                            result |= (b & 0x3F) << 27;
                        }
                    }
                }
            }

            return negative ? -result : result;
        }

        private static AssetType GuessTypeFromName(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("texture") || lower.Contains("tex_") || lower.Contains("_tex"))
                return AssetType.Texture;
            if (lower.Contains("mesh") || lower.Contains("skeletalmesh") || lower.Contains("staticmesh"))
                return AssetType.Model;
            if (lower.Contains("sound") || lower.Contains("audio") || lower.Contains("music"))
                return AssetType.Audio;
            if (lower.Contains("anim") || lower.Contains("sequence"))
                return AssetType.Animation;
            if (lower.Contains("movie") || lower.Contains("video") || lower.Contains("bink"))
                return AssetType.Video;
            return AssetType.Data;
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            long offset = entry.Offset;
            int size = (int)entry.Size;

            if (size <= 0)
                return ExportResult.Failed("Invalid export size.");

            source.Seek(offset, SeekOrigin.Begin);
            byte[] data = new byte[size];
            source.Read(data, 0, size);

            string ext = GetExtensionForType(entry.Type);
            string finalPath = Path.ChangeExtension(outputPath, ext);
            File.WriteAllBytes(finalPath, data);

            return ExportResult.Succeeded(finalPath, data.Length);
        }

        private string GetExtensionForType(AssetType type)
        {
            switch (type)
            {
                case AssetType.Texture: return ".dds";
                case AssetType.Audio: return ".xma";
                case AssetType.Model: return ".mesh.bin";
                default: return ".bin";
            }
        }
    }

    /// <summary>
    /// Container reader that treats UPK files as archives containing sub-assets.
    /// </summary>
    public class UpkContainerReader : IContainerReader
    {
        private Stream _stream;
        private UpkParser _parser;
        private AssetEntry _rootEntry;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();

        public bool CanRead(Stream stream)
        {
            if (stream.Length < 4) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);
            uint m = (uint)(magic[0] | (magic[1] << 8) | (magic[2] << 16) | (magic[3] << 24));
            return m == 0x9E2A83C1;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _parser = new UpkParser();
            _rootEntry = _parser.Parse(stream, "package.upk");
            _entries.Clear();

            foreach (var child in _rootEntry.Children)
            {
                _entries.Add(new ContainerEntry
                {
                    Name = child.Name,
                    Path = child.Name,
                    Offset = child.Offset,
                    Size = child.Size,
                    IsDirectory = false
                });
            }

            return new ContainerInfo
            {
                Format = _rootEntry.FormatName,
                Title = _rootEntry.Metadata.ContainsKey("GroupName") ? _rootEntry.Metadata["GroupName"]?.ToString() : "",
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            byte[] data = new byte[entry.Size];
            _stream.Seek(entry.Offset, SeekOrigin.Begin);
            _stream.Read(data, 0, (int)entry.Size);
            return new MemoryStream(data);
        }

        public void Dispose() { }
    }
}
