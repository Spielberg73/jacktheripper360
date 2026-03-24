using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.IdTech
{
    /// <summary>
    /// Parser for id Tech BSP (Binary Space Partitioning) map files.
    /// Supports Quake (v29), Quake 2 / IBSP v38, Quake 3 / IBSP v46-47,
    /// Doom 3 / IBSP v4, Half-Life (v30), and Source Engine / VBSP v19-21.
    /// </summary>
    public class IdTechBspParser : IAssetParser
    {
        private const int MinBspFileSize = 16;
        private const int MaxLumpCount = 64;

        // Magic identifiers
        private const uint MagicIBSP = 0x49425350; // "IBSP"
        private const uint MagicVBSP = 0x56425350; // "VBSP"
        private const uint MagicRBSP = 0x52425350; // "RBSP"

        // Quake 1 / Half-Life have no magic; version is at offset 0
        private const int VersionQuake1 = 29;
        private const int VersionHalfLife = 30;

        // IBSP versions
        private const int VersionQuake2 = 38;
        private const int VersionQuake3 = 46;
        private const int VersionCod = 47;    // Call of Duty variant
        private const int VersionDoom3 = 4;

        // VBSP versions
        private const int VersionSource19 = 19;
        private const int VersionSource20 = 20;
        private const int VersionSource21 = 21;

        // id Tech 2 (Quake 1) lump indices and count
        private const int Q1_LumpCount = 15;
        private const int Q1_Entities = 0;
        private const int Q1_Textures = 2;
        private const int Q1_Vertices = 3;
        private const int Q1_Lighting = 7;
        private const int Q1_Faces = 6;

        // id Tech 3 (Quake 3) lump indices and count
        private const int Q3_LumpCount = 17;
        private const int Q3_Entities = 0;
        private const int Q3_Textures = 1;
        private const int Q3_Planes = 2;
        private const int Q3_Nodes = 3;
        private const int Q3_Leaves = 4;
        private const int Q3_Vertices = 10;
        private const int Q3_Faces = 13;
        private const int Q3_Lightmaps = 14;
        private const int Q3_Visdata = 16;

        // id Tech 2 (Quake 2) lump indices and count
        private const int Q2_LumpCount = 19;
        private const int Q2_Entities = 0;
        private const int Q2_Texinfo = 5;
        private const int Q2_Faces = 6;
        private const int Q2_Lighting = 7;
        private const int Q2_Vertices = 2;

        /// <summary>
        /// Lump names for Quake 1 BSP (version 29).
        /// </summary>
        private static readonly string[] Q1LumpNames =
        {
            "Entities", "Planes", "Textures", "Vertices", "Visibility",
            "Nodes", "Faces", "Lighting", "Leaves", "LeafFaces",
            "LeafBrushes", "Edges", "SurfEdges", "Models", "Brushes"
        };

        /// <summary>
        /// Lump names for Quake 3 / id Tech 3 BSP (version 46/47).
        /// </summary>
        private static readonly string[] Q3LumpNames =
        {
            "Entities", "Textures", "Planes", "Nodes", "Leaves",
            "LeafFaces", "LeafBrushes", "Models", "Brushes", "BrushSides",
            "Vertices", "MeshVerts", "Effects", "Faces", "Lightmaps",
            "LightVols", "VisData"
        };

        /// <summary>
        /// Lump names for Quake 2 BSP (version 38).
        /// </summary>
        private static readonly string[] Q2LumpNames =
        {
            "Entities", "Planes", "Vertices", "Visibility", "Nodes",
            "TexInfo", "Faces", "Lighting", "Leaves", "LeafFaces",
            "LeafBrushes", "Edges", "SurfEdges", "Models", "Brushes",
            "BrushSides", "Pop", "Areas", "AreaPortals"
        };

        public AssetType Type => AssetType.Container;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < MinBspFileSize)
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            if (stream.Read(header, 0, 8) < 8)
                return false;

            uint first = BitConverter.ToUInt32(header, 0);
            uint second = BitConverter.ToUInt32(header, 4);

            // IBSP or VBSP or RBSP with a valid version
            uint magic = first;
            int version = (int)second;

            if (magic == MagicIBSP)
                return version == VersionQuake2 || version == VersionQuake3 ||
                       version == VersionCod || version == VersionDoom3;

            if (magic == MagicVBSP)
                return version >= VersionSource19 && version <= VersionSource21;

            if (magic == MagicRBSP)
                return true;

            // Quake 1 / Half-Life: no magic, version is at offset 0 as int32 LE
            int rawVersion = (int)first;
            return rawVersion == VersionQuake1 || rawVersion == VersionHalfLife;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                BspVariant variant = DetectVariant(reader);
                string variantLabel = GetVariantLabel(variant);

                var entry = new AssetEntry(fileName, AssetType.Container)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"id Tech BSP ({variantLabel})"
                };

                entry.Metadata["BspVersion"] = GetVersionNumber(variant, reader);
                entry.Metadata["EngineVariant"] = variantLabel;
                entry.Metadata["MapName"] = Path.GetFileNameWithoutExtension(fileName);

                // Read the lump directory
                LumpEntry[] lumps = ReadLumpDirectory(reader, variant);
                string[] lumpNames = GetLumpNames(variant);

                // Record lump sizes
                var lumpSizes = new Dictionary<string, long>();
                for (int i = 0; i < lumps.Length && i < lumpNames.Length; i++)
                {
                    lumpSizes[lumpNames[i]] = lumps[i].Length;
                }
                entry.Metadata["LumpSizes"] = lumpSizes;

                // Create children for significant lumps
                AddSignificantLumps(entry, lumps, lumpNames, variant, reader);

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            if (entry.Type == AssetType.Container && entry.Children.Count > 0)
            {
                return ExportAllLumps(entry, source, outputPath);
            }

            return ExportSingleLump(entry, source, outputPath);
        }

        /// <summary>
        /// Detects the BSP variant from the stream header.
        /// </summary>
        private BspVariant DetectVariant(EndianBinaryReader reader)
        {
            reader.Seek(0);
            uint first = reader.ReadUInt32();
            uint second = reader.ReadUInt32();

            if (first == MagicIBSP)
            {
                int version = (int)second;
                switch (version)
                {
                    case VersionQuake2: return BspVariant.Quake2;
                    case VersionQuake3: return BspVariant.Quake3;
                    case VersionCod:    return BspVariant.CallOfDuty;
                    case VersionDoom3:  return BspVariant.Doom3;
                }
            }

            if (first == MagicVBSP)
                return BspVariant.Source;

            if (first == MagicRBSP)
                return BspVariant.Doom3Rbsp;

            int rawVersion = (int)first;
            if (rawVersion == VersionHalfLife)
                return BspVariant.HalfLife;

            return BspVariant.Quake1;
        }

        /// <summary>
        /// Returns the numeric BSP version from the header.
        /// </summary>
        private int GetVersionNumber(BspVariant variant, EndianBinaryReader reader)
        {
            reader.Seek(0);
            uint first = reader.ReadUInt32();

            if (first == MagicIBSP || first == MagicVBSP || first == MagicRBSP)
            {
                return reader.ReadInt32();
            }

            // Quake 1 / Half-Life: version is the first int32
            return (int)first;
        }

        /// <summary>
        /// Reads the lump directory from the BSP header.
        /// </summary>
        private LumpEntry[] ReadLumpDirectory(EndianBinaryReader reader, BspVariant variant)
        {
            int lumpCount;
            int headerOffset;

            switch (variant)
            {
                case BspVariant.Quake1:
                case BspVariant.HalfLife:
                    lumpCount = Q1_LumpCount;
                    headerOffset = 4; // version only, no magic
                    break;
                case BspVariant.Quake2:
                    lumpCount = Q2_LumpCount;
                    headerOffset = 8; // magic + version
                    break;
                case BspVariant.Quake3:
                case BspVariant.CallOfDuty:
                    lumpCount = Q3_LumpCount;
                    headerOffset = 8;
                    break;
                case BspVariant.Doom3:
                case BspVariant.Doom3Rbsp:
                    lumpCount = Q3_LumpCount;
                    headerOffset = 8;
                    break;
                case BspVariant.Source:
                    lumpCount = 64; // Source BSP has up to 64 lumps
                    headerOffset = 8;
                    break;
                default:
                    lumpCount = Q1_LumpCount;
                    headerOffset = 4;
                    break;
            }

            reader.Seek(headerOffset);

            var lumps = new LumpEntry[lumpCount];
            for (int i = 0; i < lumpCount; i++)
            {
                if (reader.Position + 8 > reader.Length)
                    break;

                lumps[i].Offset = reader.ReadInt32();
                lumps[i].Length = reader.ReadInt32();

                // Source BSP has extra version and fourCC fields per lump entry
                if (variant == BspVariant.Source && reader.Position + 8 <= reader.Length)
                {
                    reader.Skip(4); // lump version
                    reader.Skip(4); // fourCC
                }
            }

            return lumps;
        }

        /// <summary>
        /// Returns the lump name table for the given BSP variant.
        /// </summary>
        private string[] GetLumpNames(BspVariant variant)
        {
            switch (variant)
            {
                case BspVariant.Quake1:
                case BspVariant.HalfLife:
                    return Q1LumpNames;
                case BspVariant.Quake2:
                    return Q2LumpNames;
                case BspVariant.Quake3:
                case BspVariant.CallOfDuty:
                case BspVariant.Doom3:
                case BspVariant.Doom3Rbsp:
                    return Q3LumpNames;
                default:
                    return Q3LumpNames;
            }
        }

        /// <summary>
        /// Creates child AssetEntry nodes for the most interesting lumps in the BSP:
        /// entities, textures, lightmaps, and vertices.
        /// </summary>
        private void AddSignificantLumps(AssetEntry parent, LumpEntry[] lumps, string[] lumpNames,
            BspVariant variant, EndianBinaryReader reader)
        {
            for (int i = 0; i < lumps.Length && i < lumpNames.Length; i++)
            {
                if (lumps[i].Length <= 0)
                    continue;

                AssetType lumpType = ClassifyLump(lumpNames[i]);
                bool isSignificant = IsSignificantLump(lumpNames[i]);

                if (!isSignificant)
                    continue;

                var child = new AssetEntry(lumpNames[i], lumpType)
                {
                    SourcePath = parent.SourcePath,
                    Offset = lumps[i].Offset,
                    Size = lumps[i].Length,
                    FormatName = $"BSP Lump ({lumpNames[i]})"
                };

                child.Metadata["LumpIndex"] = i;
                child.Metadata["EngineVariant"] = parent.Metadata["EngineVariant"];

                parent.Children.Add(child);
            }

            // Parse the entities lump for additional metadata
            int entitiesIndex = GetEntitiesLumpIndex(variant);
            if (entitiesIndex >= 0 && entitiesIndex < lumps.Length && lumps[entitiesIndex].Length > 0)
            {
                ParseEntitiesMetadata(reader, lumps[entitiesIndex], parent);
            }
        }

        /// <summary>
        /// Parses the entities lump to extract entity count and worldspawn properties.
        /// </summary>
        private void ParseEntitiesMetadata(EndianBinaryReader reader, LumpEntry entLump, AssetEntry parent)
        {
            if (entLump.Offset < 0 || entLump.Offset + entLump.Length > reader.Length)
                return;

            reader.Seek(entLump.Offset);
            int readLength = (int)Math.Min(entLump.Length, reader.Length - reader.Position);
            string entitiesText = reader.ReadString(readLength);

            // Count entities by counting opening braces at the start of a line or after whitespace
            int entityCount = 0;
            int braceDepth = 0;
            for (int i = 0; i < entitiesText.Length; i++)
            {
                if (entitiesText[i] == '{')
                {
                    if (braceDepth == 0) entityCount++;
                    braceDepth++;
                }
                else if (entitiesText[i] == '}')
                {
                    braceDepth--;
                }
            }

            parent.Metadata["EntityCount"] = entityCount;

            // Extract worldspawn properties (first entity)
            var worldspawn = ExtractWorldspawnProperties(entitiesText);
            if (worldspawn.Count > 0)
            {
                parent.Metadata["Worldspawn"] = worldspawn;

                if (worldspawn.TryGetValue("message", out string message))
                    parent.Metadata["MapTitle"] = message;

                // Some engines use "mapname" in worldspawn
                if (worldspawn.TryGetValue("mapname", out string mapName))
                    parent.Metadata["MapName"] = mapName;
            }
        }

        /// <summary>
        /// Extracts key-value properties from the worldspawn entity (the first entity in the lump).
        /// </summary>
        private Dictionary<string, string> ExtractWorldspawnProperties(string entitiesText)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int firstOpen = entitiesText.IndexOf('{');
            if (firstOpen < 0) return props;

            // Find the matching close brace for the first entity
            int braceDepth = 0;
            int entityEnd = -1;
            for (int i = firstOpen; i < entitiesText.Length; i++)
            {
                if (entitiesText[i] == '{') braceDepth++;
                else if (entitiesText[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0) { entityEnd = i; break; }
                }
            }

            if (entityEnd < 0) entityEnd = entitiesText.Length;

            string worldspawnBlock = entitiesText.Substring(firstOpen + 1, entityEnd - firstOpen - 1);

            // Parse "key" "value" pairs line by line
            using (var lineReader = new StringReader(worldspawnBlock))
            {
                string line;
                while ((line = lineReader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length < 5 || line[0] != '"') continue;

                    int keyEnd = line.IndexOf('"', 1);
                    if (keyEnd < 0) continue;

                    int valueStart = line.IndexOf('"', keyEnd + 1);
                    if (valueStart < 0) continue;

                    int valueEnd = line.IndexOf('"', valueStart + 1);
                    if (valueEnd < 0) continue;

                    string key = line.Substring(1, keyEnd - 1);
                    string value = line.Substring(valueStart + 1, valueEnd - valueStart - 1);

                    props[key] = value;
                }
            }

            return props;
        }

        /// <summary>
        /// Classifies a lump by name into an appropriate AssetType.
        /// </summary>
        private static AssetType ClassifyLump(string lumpName)
        {
            switch (lumpName)
            {
                case "Entities":
                    return AssetType.Data;
                case "Textures":
                case "TexInfo":
                case "Lightmaps":
                    return AssetType.Texture;
                case "Vertices":
                case "Faces":
                case "MeshVerts":
                    return AssetType.Model;
                case "Lighting":
                case "LightVols":
                    return AssetType.Texture;
                default:
                    return AssetType.Data;
            }
        }

        /// <summary>
        /// Determines whether a lump is considered significant enough to expose as a child asset.
        /// </summary>
        private static bool IsSignificantLump(string lumpName)
        {
            switch (lumpName)
            {
                case "Entities":
                case "Textures":
                case "TexInfo":
                case "Lightmaps":
                case "Lighting":
                case "Vertices":
                case "Faces":
                case "VisData":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the entities lump index for the given variant.
        /// </summary>
        private static int GetEntitiesLumpIndex(BspVariant variant)
        {
            // Entities are always lump 0 in all supported BSP variants
            return 0;
        }

        /// <summary>
        /// Exports all significant child lumps of a BSP container.
        /// </summary>
        private ExportResult ExportAllLumps(AssetEntry entry, Stream source, string outputPath)
        {
            string outDir = Path.GetDirectoryName(outputPath);
            string mapName = Path.GetFileNameWithoutExtension(entry.Name);
            string targetDir = Path.Combine(outDir, mapName + "_bsp");
            Directory.CreateDirectory(targetDir);

            long totalBytes = 0;

            foreach (var child in entry.Children)
            {
                string extension = child.Name == "Entities" ? ".ent" : ".lmp";
                string childPath = Path.Combine(targetDir, child.Name + extension);
                var result = ExportSingleLump(child, source, childPath);
                if (result.Success)
                    totalBytes += result.BytesWritten;
            }

            return ExportResult.Succeeded(targetDir, totalBytes);
        }

        /// <summary>
        /// Exports a single BSP lump as raw data. Entity lumps are written as plain text.
        /// </summary>
        private ExportResult ExportSingleLump(AssetEntry entry, Stream source, string outputPath)
        {
            if (entry.Size <= 0)
                return ExportResult.Failed($"Lump '{entry.Name}' is empty.");

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
        /// Returns a human-readable label for a BSP variant.
        /// </summary>
        private static string GetVariantLabel(BspVariant variant)
        {
            switch (variant)
            {
                case BspVariant.Quake1:     return "Quake (v29)";
                case BspVariant.HalfLife:   return "Half-Life (v30)";
                case BspVariant.Quake2:     return "Quake 2 / IBSP v38";
                case BspVariant.Quake3:     return "Quake 3 / IBSP v46";
                case BspVariant.CallOfDuty: return "Call of Duty / IBSP v47";
                case BspVariant.Doom3:      return "Doom 3 / IBSP v4";
                case BspVariant.Doom3Rbsp:  return "Doom 3 (RBSP)";
                case BspVariant.Source:     return "Source Engine (VBSP)";
                default:                    return "Unknown";
            }
        }

        private struct LumpEntry
        {
            public int Offset;
            public int Length;
        }

        private enum BspVariant
        {
            Quake1,
            HalfLife,
            Quake2,
            Quake3,
            CallOfDuty,
            Doom3,
            Doom3Rbsp,
            Source
        }
    }
}
