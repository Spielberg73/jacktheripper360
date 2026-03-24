using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.IdTech
{
    /// <summary>
    /// Parser for id Tech WAD archive files.
    /// Supports WAD2/WAD3 (Quake/Half-Life textures) and IWAD/PWAD (Doom/Doom II).
    /// </summary>
    public class IdTechWadParser : IAssetParser
    {
        private const int MinWadFileSize = 12;

        private const string MagicWad2 = "WAD2";
        private const string MagicWad3 = "WAD3";
        private const string MagicIwad = "IWAD";
        private const string MagicPwad = "PWAD";

        // WAD2/WAD3 lump types
        private const byte WadType_Palette = 0x40;       // '@'
        private const byte WadType_StatusBarPic = 0x42;   // 'B'
        private const byte WadType_MipTexture = 0x44;     // 'D'
        private const byte WadType_ConsolePic = 0x45;     // 'E'

        // Doom marker lump names for section detection
        private static readonly HashSet<string> FlatStartMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "F_START", "FF_START" };
        private static readonly HashSet<string> FlatEndMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "F_END", "FF_END" };
        private static readonly HashSet<string> SpriteStartMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "S_START", "SS_START" };
        private static readonly HashSet<string> SpriteEndMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "S_END", "SS_END" };
        private static readonly HashSet<string> PatchStartMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "P_START", "PP_START" };
        private static readonly HashSet<string> PatchEndMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "P_END", "PP_END" };

        // Well-known Doom lumps and their asset types
        private static readonly HashSet<string> AudioLumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "PLAYPAL", "GENMIDI", "DMXGUS" };
        private static readonly HashSet<string> DataLumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "COLORMAP", "ENDOOM", "BLOCKMAP", "REJECT", "NODES", "SEGS",
              "SSECTORS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SECTORS", "THINGS",
              "BEHAVIOR", "SCRIPTS" };

        public AssetType Type => AssetType.Container;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < MinWadFileSize)
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            if (stream.Read(magic, 0, 4) < 4)
                return false;

            string id = Encoding.ASCII.GetString(magic);
            return id == MagicWad2 || id == MagicWad3 ||
                   id == MagicIwad || id == MagicPwad;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                string magic = reader.ReadString(4);

                var entry = new AssetEntry(fileName, AssetType.Container)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"id Tech WAD ({magic})"
                };

                entry.Metadata["WadFormat"] = magic;

                if (magic == MagicWad2 || magic == MagicWad3)
                {
                    ParseQuakeWad(reader, entry, magic);
                }
                else
                {
                    ParseDoomWad(reader, entry, magic);
                }

                entry.Metadata["EntryCount"] = entry.Children.Count;
                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            if (entry.Type == AssetType.Container)
            {
                return ExportAllChildren(entry, source, outputPath, options);
            }

            return ExportLump(entry, source, outputPath);
        }

        /// <summary>
        /// Parses the directory of a WAD2 or WAD3 file (Quake/Half-Life texture archives).
        /// </summary>
        private void ParseQuakeWad(EndianBinaryReader reader, AssetEntry parent, string wadFormat)
        {
            int numEntries = reader.ReadInt32();
            int dirOffset = reader.ReadInt32();

            if (numEntries < 0 || numEntries > 65536 || dirOffset < 0 || dirOffset > reader.Length)
                return;

            reader.Seek(dirOffset);

            for (int i = 0; i < numEntries; i++)
            {
                long entryStart = reader.Position;
                if (entryStart + 32 > reader.Length)
                    break;

                int offset = reader.ReadInt32();
                int diskSize = reader.ReadInt32();
                int fullSize = reader.ReadInt32();
                byte type = reader.ReadByte();
                byte compression = reader.ReadByte();
                reader.Skip(2); // padding
                string name = reader.ReadString(16);

                var child = new AssetEntry(name, ClassifyQuakeWadType(type))
                {
                    SourcePath = parent.SourcePath,
                    Offset = offset,
                    Size = diskSize,
                    FormatName = GetQuakeTypeLabel(type)
                };

                child.Metadata["LumpType"] = type;
                child.Metadata["WadFormat"] = wadFormat;
                child.Metadata["Compression"] = (int)compression;
                child.Metadata["UncompressedSize"] = fullSize;

                // For mip textures, try to read width, height, and mip count from the lump data
                if (type == WadType_MipTexture && offset >= 0 && offset + 40 <= reader.Length)
                {
                    long savedPos = reader.Position;
                    reader.Seek(offset);

                    string texName = reader.ReadString(16);
                    uint width = reader.ReadUInt32();
                    uint height = reader.ReadUInt32();
                    uint mip0Offset = reader.ReadUInt32();
                    uint mip1Offset = reader.ReadUInt32();
                    uint mip2Offset = reader.ReadUInt32();
                    uint mip3Offset = reader.ReadUInt32();

                    child.Metadata["Width"] = (int)width;
                    child.Metadata["Height"] = (int)height;
                    child.Metadata["MipCount"] = CountMipLevels(mip0Offset, mip1Offset, mip2Offset, mip3Offset);
                    child.FormatName = $"Mip Texture ({width}x{height})";

                    reader.Seek(savedPos);
                }

                parent.Children.Add(child);
            }
        }

        /// <summary>
        /// Parses the directory of an IWAD or PWAD file (Doom/Doom II).
        /// </summary>
        private void ParseDoomWad(EndianBinaryReader reader, AssetEntry parent, string wadFormat)
        {
            int numLumps = reader.ReadInt32();
            int infoTableOffset = reader.ReadInt32();

            if (numLumps < 0 || numLumps > 65536 || infoTableOffset < 0 || infoTableOffset > reader.Length)
                return;

            reader.Seek(infoTableOffset);

            // First pass: read all raw entries
            var rawEntries = new List<DoomDirEntry>(numLumps);
            for (int i = 0; i < numLumps; i++)
            {
                if (reader.Position + 16 > reader.Length)
                    break;

                var raw = new DoomDirEntry
                {
                    Offset = reader.ReadInt32(),
                    Size = reader.ReadInt32(),
                    Name = reader.ReadString(8)
                };
                rawEntries.Add(raw);
            }

            // Second pass: classify lumps using section markers
            DoomSection currentSection = DoomSection.None;

            for (int i = 0; i < rawEntries.Count; i++)
            {
                var raw = rawEntries[i];

                // Track section markers
                if (FlatStartMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.Flat; continue; }
                if (FlatEndMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.None; continue; }
                if (SpriteStartMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.Sprite; continue; }
                if (SpriteEndMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.None; continue; }
                if (PatchStartMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.Patch; continue; }
                if (PatchEndMarkers.Contains(raw.Name))
                    { currentSection = DoomSection.None; continue; }

                AssetType lumpType = ClassifyDoomLump(raw.Name, currentSection);

                var child = new AssetEntry(raw.Name, lumpType)
                {
                    SourcePath = parent.SourcePath,
                    Offset = raw.Offset,
                    Size = raw.Size,
                    FormatName = GetDoomLumpLabel(raw.Name, currentSection)
                };

                child.Metadata["LumpType"] = GetDoomLumpCategory(raw.Name, currentSection);
                child.Metadata["WadFormat"] = wadFormat;

                parent.Children.Add(child);
            }
        }

        /// <summary>
        /// Exports all children of a container WAD to the output directory.
        /// </summary>
        private ExportResult ExportAllChildren(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string outDir = Path.GetDirectoryName(outputPath);
            string wadName = Path.GetFileNameWithoutExtension(entry.Name);
            string targetDir = Path.Combine(outDir, wadName);
            Directory.CreateDirectory(targetDir);

            long totalBytes = 0;
            int exported = 0;

            foreach (var child in entry.Children)
            {
                string childPath = Path.Combine(targetDir, SanitizeFileName(child.Name) + ".lmp");
                var result = ExportLump(child, source, childPath);
                if (result.Success)
                {
                    totalBytes += result.BytesWritten;
                    exported++;
                }
            }

            return ExportResult.Succeeded(targetDir, totalBytes);
        }

        /// <summary>
        /// Exports a single lump as raw data.
        /// </summary>
        private ExportResult ExportLump(AssetEntry entry, Stream source, string outputPath)
        {
            if (entry.Size <= 0)
                return ExportResult.Failed($"Lump '{entry.Name}' has no data (marker lump).");

            if (entry.Offset < 0 || entry.Offset + entry.Size > source.Length)
                return ExportResult.Failed($"Lump '{entry.Name}' has invalid offset/size.");

            source.Seek(entry.Offset, SeekOrigin.Begin);
            byte[] data = new byte[entry.Size];
            int bytesRead = 0;
            while (bytesRead < data.Length)
            {
                int read = source.Read(data, bytesRead, data.Length - bytesRead);
                if (read <= 0) break;
                bytesRead += read;
            }

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, data);
            return ExportResult.Succeeded(outputPath, bytesRead);
        }

        /// <summary>
        /// Classifies a WAD2/WAD3 lump type byte into an AssetType.
        /// </summary>
        private static AssetType ClassifyQuakeWadType(byte type)
        {
            switch (type)
            {
                case WadType_Palette:
                    return AssetType.Data;
                case WadType_StatusBarPic:
                case WadType_ConsolePic:
                case WadType_MipTexture:
                    return AssetType.Texture;
                default:
                    return AssetType.Data;
            }
        }

        /// <summary>
        /// Returns a human-readable label for a WAD2/WAD3 lump type.
        /// </summary>
        private static string GetQuakeTypeLabel(byte type)
        {
            switch (type)
            {
                case WadType_Palette: return "Palette";
                case WadType_StatusBarPic: return "Status Bar Pic";
                case WadType_MipTexture: return "Mip Texture";
                case WadType_ConsolePic: return "Console Pic";
                default: return $"Unknown (0x{type:X2})";
            }
        }

        /// <summary>
        /// Classifies a Doom lump name into an AssetType based on its name and the current section.
        /// </summary>
        private static AssetType ClassifyDoomLump(string name, DoomSection section)
        {
            // Section-based classification takes priority
            switch (section)
            {
                case DoomSection.Flat:
                case DoomSection.Sprite:
                case DoomSection.Patch:
                    return AssetType.Texture;
            }

            // Well-known lump names
            if (name == "PLAYPAL")
                return AssetType.Data;
            if (name == "COLORMAP" || name == "ENDOOM")
                return AssetType.Data;
            if (name.StartsWith("DS", StringComparison.Ordinal) ||
                name.StartsWith("DP", StringComparison.Ordinal))
                return AssetType.Audio;
            if (name.StartsWith("D_", StringComparison.Ordinal) ||
                name.StartsWith("MUS", StringComparison.Ordinal))
                return AssetType.Audio;
            if (name.StartsWith("DEMO", StringComparison.Ordinal))
                return AssetType.Data;
            if (IsMapMarker(name))
                return AssetType.Data;
            if (name == "TEXTURE1" || name == "TEXTURE2" || name == "PNAMES")
                return AssetType.Data;

            if (AudioLumps.Contains(name))
                return AssetType.Audio;
            if (DataLumps.Contains(name))
                return AssetType.Data;

            return AssetType.Data;
        }

        /// <summary>
        /// Returns a human-readable label for a Doom lump.
        /// </summary>
        private static string GetDoomLumpLabel(string name, DoomSection section)
        {
            switch (section)
            {
                case DoomSection.Flat: return "Flat Texture";
                case DoomSection.Sprite: return "Sprite";
                case DoomSection.Patch: return "Patch Texture";
            }

            if (IsMapMarker(name)) return "Map Marker";
            if (name.StartsWith("DS", StringComparison.Ordinal)) return "Sound Effect";
            if (name.StartsWith("DP", StringComparison.Ordinal)) return "PC Speaker Sound";
            if (name.StartsWith("D_", StringComparison.Ordinal)) return "Music (MUS)";
            if (name == "PLAYPAL") return "Palette";
            if (name == "COLORMAP") return "Colormap";
            if (name == "ENDOOM") return "Text Screen";
            if (name.StartsWith("DEMO", StringComparison.Ordinal)) return "Demo Recording";
            if (name == "TEXTURE1" || name == "TEXTURE2") return "Texture Directory";
            if (name == "PNAMES") return "Patch Names";

            return "Lump Data";
        }

        /// <summary>
        /// Returns a category string for a Doom lump's metadata.
        /// </summary>
        private static string GetDoomLumpCategory(string name, DoomSection section)
        {
            switch (section)
            {
                case DoomSection.Flat: return "Flat";
                case DoomSection.Sprite: return "Sprite";
                case DoomSection.Patch: return "Patch";
            }

            if (IsMapMarker(name)) return "Map";
            if (name.StartsWith("DS", StringComparison.Ordinal) ||
                name.StartsWith("DP", StringComparison.Ordinal))
                return "Sound";
            if (name.StartsWith("D_", StringComparison.Ordinal) ||
                name.StartsWith("MUS", StringComparison.Ordinal))
                return "Music";

            return "Data";
        }

        /// <summary>
        /// Determines whether a lump name matches the Doom MAPxx or ExMy naming convention.
        /// </summary>
        private static bool IsMapMarker(string name)
        {
            if (name.Length >= 5 && name.StartsWith("MAP", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 3; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i])) return false;
                }
                return true;
            }

            if (name.Length == 4 &&
                (name[0] == 'E' || name[0] == 'e') &&
                char.IsDigit(name[1]) &&
                (name[2] == 'M' || name[2] == 'm') &&
                char.IsDigit(name[3]))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Counts valid mip levels based on the four mip offset fields in a WAD mip texture header.
        /// A zero offset indicates no data for that mip level.
        /// </summary>
        private static int CountMipLevels(uint mip0, uint mip1, uint mip2, uint mip3)
        {
            if (mip0 == 0) return 0;
            if (mip1 == 0) return 1;
            if (mip2 == 0) return 2;
            if (mip3 == 0) return 3;
            return 4;
        }

        /// <summary>
        /// Sanitizes a lump name for use as a file name on disk.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        private struct DoomDirEntry
        {
            public int Offset;
            public int Size;
            public string Name;
        }

        private enum DoomSection
        {
            None,
            Flat,
            Sprite,
            Patch
        }
    }
}
