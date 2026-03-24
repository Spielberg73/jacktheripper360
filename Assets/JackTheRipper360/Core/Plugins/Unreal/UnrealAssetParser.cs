using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unreal
{
    /// <summary>
    /// Parser for Unreal Engine 4+ .uasset files (FPackageFileSummary format).
    /// Handles the split-file architecture where header/metadata lives in .uasset
    /// and serialized export data lives in companion .uexp and .ubulk files.
    /// </summary>
    public class UnrealAssetParser : IAssetParser
    {
        public AssetType Type => AssetType.Container;

        private const uint UASSET_MAGIC = 0x9E2A83C1;
        private const int MAX_REASONABLE_COUNT = 500000;
        private const int MAX_NAME_LENGTH = 4096;

        private static readonly string[] SupportedFileExtensions = { ".uasset", ".umap" };

        #region Detection

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream == null || stream.Length < 4)
                return false;

            // Check extension first for a quick accept/reject hint
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            bool extensionMatch = ext == ".uasset" || ext == ".umap";

            // Always verify magic
            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            int bytesRead = stream.Read(header, 0, Math.Min(8, (int)stream.Length));
            if (bytesRead < 4)
                return false;

            uint magic = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            if (magic != UASSET_MAGIC)
                return false;

            // Distinguish UE4+ from UE3: UE4 uses a negative LegacyFileVersion at offset 4
            if (bytesRead >= 8)
            {
                int legacyVersion = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
                // UE4 packages have negative legacy version (typically -6 or -7)
                if (legacyVersion < 0)
                    return true;

                // Positive version is UE3 -- only handle if extension explicitly matches .uasset
                return extensionMatch;
            }

            return extensionMatch;
        }

        #endregion

        #region Parsing

        public AssetEntry Parse(Stream stream, string fileName)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                var summary = ReadPackageFileSummary(reader, stream.Length);
                var names = ReadNameTable(reader, summary, stream.Length);
                var imports = ReadImportTable(reader, summary, names, stream.Length);
                var exports = ReadExportTable(reader, summary, names, imports, stream.Length);

                var root = new AssetEntry(Path.GetFileName(fileName), AssetType.Archive)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = FormatVersionString(summary)
                };

                PopulateMetadata(root, summary, names, imports, exports);
                CreateChildEntries(root, fileName, exports, summary);

                return root;
            }
        }

        private static string FormatVersionString(PackageFileSummary summary)
        {
            if (summary.FileVersionUE4 > 0)
                return $"UE4 UAsset (v{summary.FileVersionUE4}, Legacy {summary.LegacyFileVersion})";
            return $"Unreal UAsset (Legacy {summary.LegacyFileVersion})";
        }

        #endregion

        #region FPackageFileSummary

        private PackageFileSummary ReadPackageFileSummary(EndianBinaryReader reader, long streamLength)
        {
            var s = new PackageFileSummary();

            s.Magic = reader.ReadUInt32();
            if (s.Magic != UASSET_MAGIC)
                throw new InvalidDataException($"Invalid UAsset magic: 0x{s.Magic:X8}");

            s.LegacyFileVersion = reader.ReadInt32();

            // UE4 legacy versioning
            if (s.LegacyFileVersion < -3)
            {
                // -4 and below have LegacyUE3Version
                s.LegacyUE3Version = reader.ReadInt32();
            }

            s.FileVersionUE4 = reader.ReadInt32();
            s.LicenseeVersion = reader.ReadInt32();

            // Custom versions array
            s.CustomVersionCount = reader.ReadInt32();
            if (s.CustomVersionCount > 0 && s.CustomVersionCount < MAX_REASONABLE_COUNT)
            {
                s.CustomVersions = new CustomVersion[s.CustomVersionCount];
                for (int i = 0; i < s.CustomVersionCount; i++)
                {
                    var cv = new CustomVersion();
                    cv.Key = ReadGuid(reader);
                    cv.Version = reader.ReadInt32();
                    s.CustomVersions[i] = cv;
                }
            }
            else
            {
                s.CustomVersions = Array.Empty<CustomVersion>();
            }

            s.TotalHeaderSize = reader.ReadInt32();
            s.FolderName = ReadFString(reader);
            s.PackageFlags = reader.ReadUInt32();

            s.NameCount = reader.ReadInt32();
            s.NameOffset = reader.ReadInt32();

            // Gatherable text data (UE4.14+)
            if (s.FileVersionUE4 >= 516)
            {
                s.GatherableTextDataCount = reader.ReadInt32();
                s.GatherableTextDataOffset = reader.ReadInt32();
            }

            s.ExportCount = reader.ReadInt32();
            s.ExportOffset = reader.ReadInt32();
            s.ImportCount = reader.ReadInt32();
            s.ImportOffset = reader.ReadInt32();
            s.DependsOffset = reader.ReadInt32();

            // Soft package references (UE4.14+)
            if (s.FileVersionUE4 >= 384)
            {
                s.SoftPackageRefsCount = reader.ReadInt32();
                s.SoftPackageRefsOffset = reader.ReadInt32();
            }

            // Searchable names (UE4.17+)
            if (s.FileVersionUE4 >= 510)
            {
                s.SearchableNamesOffset = reader.ReadInt32();
            }

            s.ThumbnailTableOffset = reader.ReadInt32();

            // Package GUID
            s.PackageGuid = ReadGuid(reader);

            // Generations
            int generationCount = reader.ReadInt32();
            if (generationCount > 0 && generationCount < 1000)
            {
                s.Generations = new GenerationInfo[generationCount];
                for (int i = 0; i < generationCount; i++)
                {
                    s.Generations[i] = new GenerationInfo
                    {
                        ExportCount = reader.ReadInt32(),
                        NameCount = reader.ReadInt32()
                    };
                }
            }
            else
            {
                s.Generations = Array.Empty<GenerationInfo>();
            }

            // Engine version (UE4.2+)
            if (s.FileVersionUE4 >= 336)
            {
                s.SavedByEngineVersion = ReadEngineVersion(reader);
            }

            // Compatible engine version (UE4.8+)
            if (s.FileVersionUE4 >= 444)
            {
                s.CompatibleWithEngineVersion = ReadEngineVersion(reader);
            }

            s.CompressionFlags = reader.ReadUInt32();

            // Compressed chunks
            int compressedChunkCount = reader.ReadInt32();
            if (compressedChunkCount > 0 && compressedChunkCount < MAX_REASONABLE_COUNT)
            {
                s.CompressedChunks = new CompressedChunk[compressedChunkCount];
                for (int i = 0; i < compressedChunkCount; i++)
                {
                    s.CompressedChunks[i] = new CompressedChunk
                    {
                        UncompressedOffset = reader.ReadInt32(),
                        UncompressedSize = reader.ReadInt32(),
                        CompressedOffset = reader.ReadInt32(),
                        CompressedSize = reader.ReadInt32()
                    };
                }
            }
            else
            {
                s.CompressedChunks = Array.Empty<CompressedChunk>();
            }

            s.PackageSource = reader.ReadUInt32();

            // Additional packages to cook
            int additionalCount = reader.ReadInt32();
            if (additionalCount > 0 && additionalCount < MAX_REASONABLE_COUNT)
            {
                s.AdditionalPackagesToCook = new string[additionalCount];
                for (int i = 0; i < additionalCount; i++)
                {
                    s.AdditionalPackagesToCook[i] = ReadFString(reader);
                }
            }
            else
            {
                s.AdditionalPackagesToCook = Array.Empty<string>();
            }

            // Asset registry data offset (UE4+)
            if (s.LegacyFileVersion > -7)
            {
                // Older format: texture allocations etc. -- skip
            }

            s.AssetRegistryDataOffset = reader.ReadInt32();
            s.BulkDataStartOffset = reader.ReadInt64();

            // World tile info (UE4.9+)
            if (s.FileVersionUE4 >= 456)
            {
                s.WorldTileInfoDataOffset = reader.ReadInt32();
            }

            // Chunk IDs (UE4.11+)
            if (s.FileVersionUE4 >= 473)
            {
                int chunkIdCount = reader.ReadInt32();
                if (chunkIdCount > 0 && chunkIdCount < MAX_REASONABLE_COUNT)
                {
                    s.ChunkIds = new int[chunkIdCount];
                    for (int i = 0; i < chunkIdCount; i++)
                    {
                        s.ChunkIds[i] = reader.ReadInt32();
                    }
                }
                else
                {
                    s.ChunkIds = Array.Empty<int>();
                }
            }
            else
            {
                s.ChunkIds = Array.Empty<int>();
            }

            // Preload dependency (UE4.19+)
            if (s.FileVersionUE4 >= 517)
            {
                s.PreloadDependencyCount = reader.ReadInt32();
                s.PreloadDependencyOffset = reader.ReadInt32();
            }

            return s;
        }

        #endregion

        #region Name Table

        private string[] ReadNameTable(EndianBinaryReader reader, PackageFileSummary summary, long streamLength)
        {
            int count = Math.Min(summary.NameCount, MAX_REASONABLE_COUNT);
            if (count <= 0 || summary.NameOffset <= 0 || summary.NameOffset >= streamLength)
                return Array.Empty<string>();

            var names = new string[count];
            reader.Seek(summary.NameOffset);

            for (int i = 0; i < count; i++)
            {
                if (reader.Position >= streamLength - 4)
                {
                    // Truncated -- fill remaining with placeholders
                    for (int j = i; j < count; j++)
                        names[j] = $"__truncated_name_{j}";
                    break;
                }

                string name = ReadFString(reader);
                names[i] = name ?? $"__null_name_{i}";

                // UE4 name entries have a case-insensitive hash after the string
                if (reader.Position + 4 <= streamLength)
                {
                    // NonCasePreservingHash (uint16) + CasePreservingHash (uint16)
                    // or just a single uint32 hash depending on version
                    reader.Skip(4); // Skip hash bytes
                }
            }

            return names;
        }

        #endregion

        #region Import Table

        private ImportEntry[] ReadImportTable(EndianBinaryReader reader, PackageFileSummary summary,
            string[] names, long streamLength)
        {
            int count = Math.Min(summary.ImportCount, MAX_REASONABLE_COUNT);
            if (count <= 0 || summary.ImportOffset <= 0 || summary.ImportOffset >= streamLength)
                return Array.Empty<ImportEntry>();

            var imports = new ImportEntry[count];
            reader.Seek(summary.ImportOffset);

            for (int i = 0; i < count; i++)
            {
                if (reader.Position >= streamLength - 28)
                    break;

                var imp = new ImportEntry();

                // ClassPackage (FName index)
                int classPackageNameIndex = reader.ReadInt32();
                int classPackageNameNumber = reader.ReadInt32();

                // ClassName (FName index)
                int classNameIndex = reader.ReadInt32();
                int classNameNumber = reader.ReadInt32();

                imp.OuterIndex = reader.ReadInt32();

                // ObjectName (FName index)
                int objectNameIndex = reader.ReadInt32();
                int objectNameNumber = reader.ReadInt32();

                imp.ClassPackage = ResolveName(names, classPackageNameIndex);
                imp.ClassName = ResolveName(names, classNameIndex);
                imp.ObjectName = ResolveName(names, objectNameIndex);

                imports[i] = imp;
            }

            return imports;
        }

        #endregion

        #region Export Table

        private ExportEntry[] ReadExportTable(EndianBinaryReader reader, PackageFileSummary summary,
            string[] names, ImportEntry[] imports, long streamLength)
        {
            int count = Math.Min(summary.ExportCount, MAX_REASONABLE_COUNT);
            if (count <= 0 || summary.ExportOffset <= 0 || summary.ExportOffset >= streamLength)
                return Array.Empty<ExportEntry>();

            var exports = new ExportEntry[count];
            reader.Seek(summary.ExportOffset);

            for (int i = 0; i < count; i++)
            {
                if (reader.Position >= streamLength - 40)
                    break;

                var exp = new ExportEntry();

                exp.ClassIndex = reader.ReadInt32();
                exp.SuperIndex = reader.ReadInt32();

                // Template index (UE4.15+)
                if (summary.FileVersionUE4 >= 508)
                {
                    exp.TemplateIndex = reader.ReadInt32();
                }

                exp.OuterIndex = reader.ReadInt32();

                // ObjectName (FName)
                int objectNameIndex = reader.ReadInt32();
                int objectNameNumber = reader.ReadInt32();
                exp.ObjectName = ResolveName(names, objectNameIndex);
                if (objectNameNumber > 0)
                    exp.ObjectName = $"{exp.ObjectName}_{objectNameNumber - 1}";

                exp.ObjectFlags = reader.ReadUInt32();

                // Serial size and offset
                if (summary.LegacyFileVersion < -6 || summary.FileVersionUE4 >= 511)
                {
                    // 64-bit serial size
                    exp.SerialSize = reader.ReadInt64();
                    exp.SerialOffset = reader.ReadInt64();
                }
                else
                {
                    exp.SerialSize = reader.ReadInt32();
                    exp.SerialOffset = reader.ReadInt32();
                }

                // bForcedExport
                exp.IsForcedExport = reader.ReadInt32() != 0;

                // bNotForClient
                exp.IsNotForClient = reader.ReadInt32() != 0;

                // bNotForServer
                exp.IsNotForServer = reader.ReadInt32() != 0;

                // PackageGuid
                exp.PackageGuid = ReadGuid(reader);

                // PackageFlags
                exp.PackageFlags = reader.ReadUInt32();

                // bNotAlwaysLoadedForEditorGame (UE4.14+)
                if (summary.FileVersionUE4 >= 485)
                {
                    exp.IsNotAlwaysLoadedForEditorGame = reader.ReadInt32() != 0;
                }

                // bIsAsset (UE4.14+)
                if (summary.FileVersionUE4 >= 485)
                {
                    exp.IsAsset = reader.ReadInt32() != 0;
                }

                // FirstExportDependency (UE4.19+)
                if (summary.FileVersionUE4 >= 517)
                {
                    exp.FirstExportDependency = reader.ReadInt32();
                    exp.SerializationBeforeSerializationDependencies = reader.ReadInt32();
                    exp.CreateBeforeSerializationDependencies = reader.ReadInt32();
                    exp.SerializationBeforeCreateDependencies = reader.ReadInt32();
                    exp.CreateBeforeCreateDependencies = reader.ReadInt32();
                }

                // Resolve class name from import table
                exp.ClassName = ResolveClassNameFromIndex(exp.ClassIndex, names, imports);

                exports[i] = exp;
            }

            return exports;
        }

        #endregion

        #region Child Entry Creation

        private void PopulateMetadata(AssetEntry root, PackageFileSummary summary,
            string[] names, ImportEntry[] imports, ExportEntry[] exports)
        {
            root.Metadata["Magic"] = $"0x{summary.Magic:X8}";
            root.Metadata["LegacyFileVersion"] = summary.LegacyFileVersion;
            root.Metadata["FileVersionUE4"] = summary.FileVersionUE4;
            root.Metadata["LicenseeVersion"] = summary.LicenseeVersion;
            root.Metadata["TotalHeaderSize"] = summary.TotalHeaderSize;
            root.Metadata["FolderName"] = summary.FolderName ?? "";
            root.Metadata["PackageFlags"] = $"0x{summary.PackageFlags:X8}";
            root.Metadata["NameCount"] = summary.NameCount;
            root.Metadata["ExportCount"] = summary.ExportCount;
            root.Metadata["ImportCount"] = summary.ImportCount;
            root.Metadata["CompressionFlags"] = summary.CompressionFlags;
            root.Metadata["PackageGuid"] = summary.PackageGuid.ToString();
            root.Metadata["BulkDataStartOffset"] = summary.BulkDataStartOffset;
            root.Metadata["AssetRegistryDataOffset"] = summary.AssetRegistryDataOffset;
            root.Metadata["CustomVersionCount"] = summary.CustomVersionCount;

            if (summary.CompressedChunks != null && summary.CompressedChunks.Length > 0)
                root.Metadata["CompressedChunkCount"] = summary.CompressedChunks.Length;

            if (names.Length > 0)
                root.Metadata["NameTableSize"] = names.Length;

            if (imports.Length > 0)
                root.Metadata["ImportTableSize"] = imports.Length;
        }

        private void CreateChildEntries(AssetEntry root, string fileName,
            ExportEntry[] exports, PackageFileSummary summary)
        {
            // Determine companion .uexp path for data source info
            string directory = Path.GetDirectoryName(fileName) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string uexpPath = Path.Combine(directory, baseName + ".uexp");
            string ubulkPath = Path.Combine(directory, baseName + ".ubulk");

            bool hasUexp = !string.IsNullOrEmpty(directory) && File.Exists(uexpPath);
            bool hasUbulk = !string.IsNullOrEmpty(directory) && File.Exists(ubulkPath);

            root.Metadata["HasCompanionUexp"] = hasUexp;
            root.Metadata["HasCompanionUbulk"] = hasUbulk;

            for (int i = 0; i < exports.Length; i++)
            {
                var exp = exports[i];
                if (exp == null)
                    continue;

                AssetType childType = ClassNameToAssetType(exp.ClassName);
                string displayName = !string.IsNullOrEmpty(exp.ObjectName)
                    ? exp.ObjectName
                    : $"Export_{i}";

                var child = new AssetEntry(displayName, childType)
                {
                    SourcePath = $"{fileName}#Export[{i}]:{displayName}",
                    Offset = exp.SerialOffset,
                    Size = exp.SerialSize,
                    FormatName = FormatExportName(exp.ClassName)
                };

                child.Metadata["ExportIndex"] = i;
                child.Metadata["ClassName"] = exp.ClassName ?? "Class";
                child.Metadata["ObjectName"] = exp.ObjectName ?? "";
                child.Metadata["SerialSize"] = exp.SerialSize;
                child.Metadata["SerialOffset"] = exp.SerialOffset;
                child.Metadata["ObjectFlags"] = $"0x{exp.ObjectFlags:X8}";
                child.Metadata["ClassIndex"] = exp.ClassIndex;
                child.Metadata["SuperIndex"] = exp.SuperIndex;
                child.Metadata["OuterIndex"] = exp.OuterIndex;
                child.Metadata["IsForcedExport"] = exp.IsForcedExport;
                child.Metadata["IsAsset"] = exp.IsAsset;

                if (exp.SerialSize > 0 && summary.TotalHeaderSize > 0)
                {
                    // In split .uasset/.uexp files, export serial offsets are relative
                    // to the start of the .uexp (offset = serialOffset - totalHeaderSize)
                    child.Metadata["UexpRelativeOffset"] = exp.SerialOffset - summary.TotalHeaderSize;
                }

                if (hasUbulk)
                    child.Metadata["UbulkPath"] = ubulkPath;

                root.Children.Add(child);
            }
        }

        private static string FormatExportName(string className)
        {
            if (string.IsNullOrEmpty(className) || className == "Class")
                return "UAsset Export";
            return $"UAsset {className}";
        }

        #endregion

        #region Export (Extract)

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            if (entry == null)
                return ExportResult.Failed("Null asset entry.");

            long serialOffset = entry.Offset;
            long serialSize = entry.Size;

            if (serialSize <= 0)
                return ExportResult.Failed($"Export '{entry.Name}' has no data (size={serialSize}).");

            // Determine the data source stream: prefer companion .uexp for split packages
            Stream dataStream = null;
            bool ownsDataStream = false;
            long readOffset = serialOffset;

            int totalHeaderSize = 0;
            if (entry.Metadata.TryGetValue("UexpRelativeOffset", out object uexpRelObj))
            {
                // Attempt to open .uexp companion file for reading
                string sourcePath = entry.SourcePath;
                if (sourcePath != null && sourcePath.Contains("#"))
                    sourcePath = sourcePath.Substring(0, sourcePath.IndexOf('#'));

                string dir = Path.GetDirectoryName(sourcePath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string uexpPath = Path.Combine(dir, baseName + ".uexp");

                if (File.Exists(uexpPath))
                {
                    dataStream = File.OpenRead(uexpPath);
                    ownsDataStream = true;
                    readOffset = Convert.ToInt64(uexpRelObj);
                }
            }

            if (dataStream == null)
            {
                dataStream = source;
                readOffset = serialOffset;
            }

            try
            {
                if (readOffset < 0 || readOffset + serialSize > dataStream.Length)
                {
                    return ExportResult.Failed(
                        $"Export data range [{readOffset}, {readOffset + serialSize}) exceeds " +
                        $"stream length ({dataStream.Length}).");
                }

                dataStream.Seek(readOffset, SeekOrigin.Begin);

                string ext = GetExtensionForClassName(entry);
                string finalPath = Path.ChangeExtension(outputPath, ext);

                string outDir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                if (!options.OverwriteExisting && File.Exists(finalPath))
                    return ExportResult.Failed($"File already exists: {finalPath}");

                long remaining = serialSize;
                long totalWritten = 0;
                byte[] buffer = new byte[Math.Min(81920, serialSize)];

                using (var outStream = File.Create(finalPath))
                {
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int bytesRead = dataStream.Read(buffer, 0, toRead);
                        if (bytesRead <= 0)
                            break;

                        outStream.Write(buffer, 0, bytesRead);
                        remaining -= bytesRead;
                        totalWritten += bytesRead;
                    }
                }

                return ExportResult.Succeeded(finalPath, totalWritten);
            }
            finally
            {
                if (ownsDataStream && dataStream != null)
                    dataStream.Dispose();
            }
        }

        private static string GetExtensionForClassName(AssetEntry entry)
        {
            string className = "";
            if (entry.Metadata.TryGetValue("ClassName", out object cnObj))
                className = cnObj as string ?? "";

            switch (className)
            {
                case "Texture2D":
                case "TextureCube":
                case "TextureRenderTarget2D":
                    return ".ubulk.texture";
                case "StaticMesh":
                    return ".staticmesh.bin";
                case "SkeletalMesh":
                    return ".skelmesh.bin";
                case "SoundWave":
                    return ".sound.bin";
                case "AnimSequence":
                case "AnimMontage":
                case "AnimComposite":
                    return ".anim.bin";
                case "Material":
                case "MaterialInstanceConstant":
                case "MaterialInstanceDynamic":
                    return ".material.bin";
                case "Blueprint":
                case "WidgetBlueprint":
                case "AnimBlueprint":
                    return ".blueprint.bin";
                case "CurveTable":
                case "DataTable":
                    return ".data.bin";
                default:
                    return ".bin";
            }
        }

        #endregion

        #region Name / Class Resolution Helpers

        private static string ResolveName(string[] names, int index)
        {
            if (index >= 0 && index < names.Length)
                return names[index] ?? "None";
            return $"__unresolved_{index}";
        }

        /// <summary>
        /// Resolves a class name from a class index.
        /// Positive index => export table entry (1-based).
        /// Negative index => import table entry (negated 1-based, so -1 => imports[0]).
        /// Zero => "Class" (the UObject base).
        /// </summary>
        private static string ResolveClassNameFromIndex(int classIndex, string[] names, ImportEntry[] imports)
        {
            if (classIndex == 0)
                return "Class";

            if (classIndex < 0)
            {
                int importIndex = -classIndex - 1;
                if (importIndex >= 0 && importIndex < imports.Length && imports[importIndex] != null)
                    return imports[importIndex].ClassName ?? "Class";
            }
            else
            {
                // Positive -- references an export. Unusual for class references but valid.
                // We would need the export's own name. Return a placeholder.
                return $"ExportClass_{classIndex - 1}";
            }

            return "Class";
        }

        /// <summary>
        /// Maps a UE4 class name to the appropriate AssetType enum value.
        /// </summary>
        private static AssetType ClassNameToAssetType(string className)
        {
            if (string.IsNullOrEmpty(className))
                return AssetType.Data;

            switch (className)
            {
                // Textures
                case "Texture2D":
                case "TextureCube":
                case "TextureRenderTarget2D":
                case "LightMapTexture2D":
                case "ShadowMapTexture2D":
                case "Texture2DArray":
                case "VolumeTexture":
                case "MediaTexture":
                    return AssetType.Texture;

                // Static / Skeletal meshes
                case "StaticMesh":
                case "SkeletalMesh":
                case "DestructibleMesh":
                case "ProceduralMeshComponent":
                case "GeometryCache":
                    return AssetType.Model;

                // Audio
                case "SoundWave":
                case "SoundCue":
                case "SoundClass":
                case "SoundMix":
                case "DialogueWave":
                case "ReverbEffect":
                case "SoundAttenuation":
                    return AssetType.Audio;

                // Animation
                case "AnimSequence":
                case "AnimMontage":
                case "AnimComposite":
                case "AnimBlueprint":
                case "BlendSpace":
                case "BlendSpace1D":
                case "AimOffsetBlendSpace":
                case "Skeleton":
                case "PhysicsAsset":
                    return AssetType.Animation;

                // Video / Media
                case "MediaSource":
                case "FileMediaSource":
                case "MediaPlayer":
                    return AssetType.Video;

                // Containers / complex objects
                case "Package":
                case "Level":
                case "World":
                case "MapBuildDataRegistry":
                    return AssetType.Container;

                // Blueprints and logic
                case "Blueprint":
                case "WidgetBlueprint":
                case "UserDefinedStruct":
                case "UserDefinedEnum":
                    return AssetType.Data;

                // Materials
                case "Material":
                case "MaterialInstanceConstant":
                case "MaterialInstanceDynamic":
                case "MaterialFunction":
                case "MaterialParameterCollection":
                    return AssetType.Data;

                default:
                    return AssetType.Data;
            }
        }

        #endregion

        #region Serialization Helpers

        /// <summary>
        /// Reads an FString (length-prefixed string, UE4 format).
        /// Positive length = UTF-8/ASCII. Negative length = UTF-16.
        /// </summary>
        private static string ReadFString(EndianBinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length == 0)
                return "";

            if (length > 0)
            {
                if (length > MAX_NAME_LENGTH)
                    throw new InvalidDataException($"FString length {length} exceeds maximum {MAX_NAME_LENGTH}.");

                byte[] data = reader.ReadBytes(length);
                // UE4 FStrings are null-terminated
                return Encoding.UTF8.GetString(data, 0, Math.Max(0, data.Length - 1));
            }
            else
            {
                // Negative length = UTF-16LE character count
                int charCount = -length;
                if (charCount > MAX_NAME_LENGTH)
                    throw new InvalidDataException($"FString UTF-16 length {charCount} exceeds maximum {MAX_NAME_LENGTH}.");

                byte[] data = reader.ReadBytes(charCount * 2);
                // Strip null terminator (last 2 bytes)
                int strByteLen = Math.Max(0, data.Length - 2);
                return Encoding.Unicode.GetString(data, 0, strByteLen);
            }
        }

        private static Guid ReadGuid(EndianBinaryReader reader)
        {
            byte[] guidBytes = reader.ReadBytes(16);
            if (guidBytes.Length < 16)
                return Guid.Empty;
            return new Guid(guidBytes);
        }

        private static EngineVersion ReadEngineVersion(EndianBinaryReader reader)
        {
            return new EngineVersion
            {
                Major = reader.ReadUInt16(),
                Minor = reader.ReadUInt16(),
                Patch = reader.ReadUInt16(),
                Changelist = reader.ReadUInt32(),
                Branch = ReadFString(reader)
            };
        }

        #endregion

        #region Internal Data Structures

        private class PackageFileSummary
        {
            public uint Magic;
            public int LegacyFileVersion;
            public int LegacyUE3Version;
            public int FileVersionUE4;
            public int LicenseeVersion;
            public int CustomVersionCount;
            public CustomVersion[] CustomVersions = Array.Empty<CustomVersion>();

            public int TotalHeaderSize;
            public string FolderName;
            public uint PackageFlags;

            public int NameCount;
            public int NameOffset;
            public int ExportCount;
            public int ExportOffset;
            public int ImportCount;
            public int ImportOffset;
            public int DependsOffset;

            public int SoftPackageRefsCount;
            public int SoftPackageRefsOffset;
            public int SearchableNamesOffset;
            public int ThumbnailTableOffset;

            public Guid PackageGuid;
            public GenerationInfo[] Generations = Array.Empty<GenerationInfo>();

            public EngineVersion SavedByEngineVersion;
            public EngineVersion CompatibleWithEngineVersion;

            public uint CompressionFlags;
            public CompressedChunk[] CompressedChunks = Array.Empty<CompressedChunk>();
            public uint PackageSource;
            public string[] AdditionalPackagesToCook = Array.Empty<string>();

            public int AssetRegistryDataOffset;
            public long BulkDataStartOffset;
            public int WorldTileInfoDataOffset;

            public int GatherableTextDataCount;
            public int GatherableTextDataOffset;

            public int[] ChunkIds = Array.Empty<int>();

            public int PreloadDependencyCount;
            public int PreloadDependencyOffset;
        }

        private class CustomVersion
        {
            public Guid Key;
            public int Version;
        }

        private struct GenerationInfo
        {
            public int ExportCount;
            public int NameCount;
        }

        private class EngineVersion
        {
            public ushort Major;
            public ushort Minor;
            public ushort Patch;
            public uint Changelist;
            public string Branch;

            public override string ToString() => $"{Major}.{Minor}.{Patch}-{Changelist}";
        }

        private struct CompressedChunk
        {
            public int UncompressedOffset;
            public int UncompressedSize;
            public int CompressedOffset;
            public int CompressedSize;
        }

        private class ImportEntry
        {
            public string ClassPackage;
            public string ClassName;
            public int OuterIndex;
            public string ObjectName;
        }

        private class ExportEntry
        {
            public int ClassIndex;
            public int SuperIndex;
            public int TemplateIndex;
            public int OuterIndex;
            public string ObjectName;
            public uint ObjectFlags;
            public long SerialSize;
            public long SerialOffset;
            public bool IsForcedExport;
            public bool IsNotForClient;
            public bool IsNotForServer;
            public Guid PackageGuid;
            public uint PackageFlags;
            public bool IsNotAlwaysLoadedForEditorGame;
            public bool IsAsset;
            public int FirstExportDependency;
            public int SerializationBeforeSerializationDependencies;
            public int CreateBeforeSerializationDependencies;
            public int SerializationBeforeCreateDependencies;
            public int CreateBeforeCreateDependencies;
            public string ClassName;
        }

        #endregion
    }
}
