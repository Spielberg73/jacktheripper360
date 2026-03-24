using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Core.Plugins;
using JackTheRipper360.Core.Plugins.Unity;
using JackTheRipper360.Core.Plugins.Unreal;

namespace JackTheRipper360.CLI
{
    /// <summary>
    /// Standalone CLI application for JackTheRipper360.
    /// Runs without Unity - uses only the Core library (pure C#).
    ///
    /// Usage:
    ///   JackTheRipper360CLI scan <path>                 - Scan and list assets
    ///   JackTheRipper360CLI export <path> <output_dir>  - Export all assets
    ///   JackTheRipper360CLI export <path> <output_dir> --type=texture  - Export by type
    ///   JackTheRipper360CLI reimport <path> <output_dir> --engine=unity  - Export for engine reimport
    ///   JackTheRipper360CLI info <file>                 - Show file format info
    ///   JackTheRipper360CLI list-formats                - List supported formats
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=== JackTheRipper360 CLI - Xbox 360 Asset Extractor ===");
            Console.WriteLine();

            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "scan":
                    return args.Length >= 2 ? CommandScan(args[1]) : MissingArg("path");

                case "export":
                    return args.Length >= 3 ? CommandExport(args) : MissingArg("path output_dir");

                case "reimport":
                    return args.Length >= 3 ? CommandReimport(args) : MissingArg("path output_dir --engine=unity|unreal|both");

                case "info":
                    return args.Length >= 2 ? CommandInfo(args[1]) : MissingArg("file");

                case "list-formats":
                    return CommandListFormats();

                case "--help":
                case "-h":
                case "help":
                    PrintUsage();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }

        static int CommandScan(string path)
        {
            Console.WriteLine($"Scanning: {path}");
            Console.WriteLine();

            var database = new AssetDatabase();
            var scanner = new AssetScanner(database);

            var progress = new Progress<ScanProgress>(p =>
            {
                Console.Write($"\r  [{p.Progress * 100:F0}%] {Path.GetFileName(p.CurrentFile),-50}");
            });

            ScanResult result;
            if (File.Exists(path))
            {
                result = new ScanResult();
                scanner.ScanFile(path, result);
            }
            else if (Directory.Exists(path))
            {
                result = scanner.ScanDirectory(path, progress);
            }
            else
            {
                Console.Error.WriteLine($"Path not found: {path}");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine();

            // Print results
            var entries = database.GetAllEntriesFlat();
            var typeCounts = database.GetTypeCounts();

            Console.WriteLine($"Found {result.AssetsFound} assets in {result.ContainersFound} containers:");
            Console.WriteLine();

            foreach (var kvp in typeCounts)
                Console.WriteLine($"  {kvp.Key,-15} {kvp.Value,6}");

            Console.WriteLine();
            Console.WriteLine("Assets:");
            Console.WriteLine($"  {"Name",-40} {"Type",-12} {"Format",-20} {"Size",10}");
            Console.WriteLine(new string('-', 85));

            foreach (var entry in entries)
            {
                string name = entry.Name?.Length > 38 ? entry.Name.Substring(0, 38) + ".." : entry.Name;
                Console.WriteLine($"  {name,-40} {entry.Type,-12} {entry.FormatName ?? "?",-20} {FormatSize(entry.Size),10}");

                // Show children (indented)
                foreach (var child in entry.Children)
                {
                    string childName = child.Name?.Length > 36 ? child.Name.Substring(0, 36) + ".." : child.Name;
                    Console.WriteLine($"    {childName,-38} {child.Type,-12} {child.FormatName ?? "?",-20} {FormatSize(child.Size),10}");
                }
            }

            if (result.Errors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Errors ({result.Errors.Count}):");
                foreach (var error in result.Errors)
                    Console.Error.WriteLine($"  {error}");
            }

            return 0;
        }

        static int CommandExport(string[] args)
        {
            string inputPath = args[1];
            string outputDir = args[2];

            // Parse optional flags
            AssetType? typeFilter = null;
            string formatFilter = null;

            for (int i = 3; i < args.Length; i++)
            {
                if (args[i].StartsWith("--type="))
                {
                    string typeStr = args[i].Substring(7);
                    if (Enum.TryParse(typeStr, true, out AssetType t))
                        typeFilter = t;
                }
                else if (args[i].StartsWith("--format="))
                {
                    formatFilter = args[i].Substring(9);
                }
            }

            Console.WriteLine($"Scanning: {inputPath}");

            var database = new AssetDatabase();
            var scanner = new AssetScanner(database);

            if (File.Exists(inputPath))
            {
                var r = new ScanResult();
                scanner.ScanFile(inputPath, r);
            }
            else if (Directory.Exists(inputPath))
            {
                scanner.ScanDirectory(inputPath);
            }
            else
            {
                Console.Error.WriteLine($"Path not found: {inputPath}");
                return 1;
            }

            var entries = database.GetAllEntriesFlat();
            Console.WriteLine($"Found {entries.Count} assets.");

            // Filter
            var toExport = new List<AssetEntry>();
            foreach (var entry in entries)
            {
                if (typeFilter.HasValue && entry.Type != typeFilter.Value) continue;
                if (entry.Type == AssetType.Unknown || entry.Type == AssetType.Container) continue;
                toExport.Add(entry);
            }

            Console.WriteLine($"Exporting {toExport.Count} assets to: {outputDir}");
            Directory.CreateDirectory(outputDir);

            // Create parsers for export
            var parsers = new List<IAssetParser>
            {
                new Textures.DdsParser(),
                new Textures.XprParser(),
                new Audio.XmaDecoder(),
                new Audio.XwmaParser(),
                new Audio.XactWaveBankReader(),
                new Video.BinkVideoParser(),
                new Video.XmvParser(),
                new Models.Xbox360MeshParser(),
                new Animation.AnimationParser()
            };

            int exported = 0;
            int failed = 0;

            foreach (var entry in toExport)
            {
                string typeDir = Path.Combine(outputDir, entry.Type.ToString());
                Directory.CreateDirectory(typeDir);

                string safeName = SanitizeFileName(entry.Name ?? $"asset_{exported}");
                string outputPath = Path.Combine(typeDir, safeName);

                try
                {
                    string sourcePath = entry.SourcePath;
                    if (sourcePath != null && sourcePath.Contains(":"))
                        sourcePath = sourcePath.Split(':')[0];

                    if (sourcePath == null || !File.Exists(sourcePath))
                    {
                        failed++;
                        continue;
                    }

                    using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bool didExport = false;
                        foreach (var parser in parsers)
                        {
                            if (parser.Type != entry.Type) continue;
                            stream.Seek(entry.Offset > 0 ? entry.Offset : 0, SeekOrigin.Begin);
                            if (parser.CanParse(stream, entry.Name))
                            {
                                stream.Seek(entry.Offset > 0 ? entry.Offset : 0, SeekOrigin.Begin);
                                var result = parser.Export(entry, stream, outputPath,
                                    new ExportOptions { PreferredFormat = formatFilter });

                                if (result.Success)
                                {
                                    exported++;
                                    didExport = true;
                                    Console.WriteLine($"  [OK] {entry.Name} -> {result.OutputPath}");
                                }
                                else
                                {
                                    failed++;
                                    Console.Error.WriteLine($"  [FAIL] {entry.Name}: {result.ErrorMessage}");
                                }
                                break;
                            }
                        }

                        if (!didExport)
                        {
                            // Raw export
                            stream.Seek(entry.Offset > 0 ? entry.Offset : 0, SeekOrigin.Begin);
                            int size = (int)Math.Min(entry.Size > 0 ? entry.Size : stream.Length, stream.Length - stream.Position);
                            byte[] data = new byte[size];
                            stream.Read(data, 0, size);
                            File.WriteAllBytes(outputPath + ".bin", data);
                            exported++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"  [ERROR] {entry.Name}: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Export complete: {exported} succeeded, {failed} failed.");

            return failed > 0 ? 2 : 0;
        }

        static int CommandInfo(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return 1;
            }

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var match = FormatDetector.Identify(stream);
                if (match.Confidence < 0.1f)
                    match = FormatDetector.IdentifyByExtension(filePath);

                Console.WriteLine($"File:       {Path.GetFileName(filePath)}");
                Console.WriteLine($"Size:       {FormatSize(stream.Length)}");
                Console.WriteLine($"Format:     {match.FormatName}");
                Console.WriteLine($"Type:       {match.Type}");
                Console.WriteLine($"Confidence: {match.Confidence:P0}");

                // Show first 64 bytes as hex
                Console.WriteLine();
                Console.WriteLine("Header (first 64 bytes):");
                stream.Seek(0, SeekOrigin.Begin);
                byte[] header = new byte[Math.Min(64, stream.Length)];
                stream.Read(header, 0, header.Length);

                for (int i = 0; i < header.Length; i += 16)
                {
                    Console.Write($"  {i:X4}: ");
                    for (int j = 0; j < 16 && i + j < header.Length; j++)
                        Console.Write($"{header[i + j]:X2} ");

                    Console.Write("  ");
                    for (int j = 0; j < 16 && i + j < header.Length; j++)
                    {
                        byte b = header[i + j];
                        Console.Write(b >= 32 && b < 127 ? (char)b : '.');
                    }
                    Console.WriteLine();
                }
            }

            return 0;
        }

        static int CommandReimport(string[] args)
        {
            string inputPath = args[1];
            string outputDir = args[2];

            // Parse engine flag
            TargetEngine engine = TargetEngine.Unity;
            bool generateMeta = true;
            bool generateImportSettings = true;

            for (int i = 3; i < args.Length; i++)
            {
                if (args[i].StartsWith("--engine="))
                {
                    string engineStr = args[i].Substring(9).ToLowerInvariant();
                    switch (engineStr)
                    {
                        case "unity": engine = TargetEngine.Unity; break;
                        case "unreal": engine = TargetEngine.UnrealEngine; break;
                        case "both": engine = TargetEngine.Both; break;
                        default:
                            Console.Error.WriteLine($"Unknown engine: {engineStr}. Use: unity, unreal, both");
                            return 1;
                    }
                }
                else if (args[i] == "--no-meta") generateMeta = false;
                else if (args[i] == "--no-import-settings") generateImportSettings = false;
            }

            Console.WriteLine($"Scanning: {inputPath}");
            Console.WriteLine($"Target engine: {engine}");

            var database = new AssetDatabase();
            var scanner = new AssetScanner(database);

            if (File.Exists(inputPath))
            {
                var r = new ScanResult();
                scanner.ScanFile(inputPath, r);
            }
            else if (Directory.Exists(inputPath))
            {
                scanner.ScanDirectory(inputPath);
            }
            else
            {
                Console.Error.WriteLine($"Path not found: {inputPath}");
                return 1;
            }

            var entries = database.GetAllEntriesFlat();
            var toConvert = new List<AssetEntry>();
            foreach (var entry in entries)
            {
                if (entry.Type == AssetType.Unknown || entry.Type == AssetType.Container) continue;
                toConvert.Add(entry);
            }

            Console.WriteLine($"Found {toConvert.Count} convertible assets.");
            Console.WriteLine($"Converting for {engine} reimport...");
            Console.WriteLine();

            var converter = new EngineReimportConverter();
            var profile = new ConversionProfile
            {
                Target = engine,
                GenerateMetaFiles = generateMeta,
                GenerateImportSettings = generateImportSettings,
                PreserveDirectoryStructure = true
            };

            var batchResult = converter.ConvertBatch(
                toConvert, outputDir, profile,
                new Progress<BatchProgress>(p =>
                {
                    Console.Write($"\r  [{p.Processed}/{p.Total}] {p.CurrentAsset,-50}");
                }));

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Reimport export complete:");
            Console.WriteLine($"  Succeeded: {batchResult.Succeeded}");
            Console.WriteLine($"  Failed:    {batchResult.Failed}");
            Console.WriteLine($"  Output:    {outputDir}");

            if (batchResult.Warnings.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Warnings ({batchResult.Warnings.Count}):");
                foreach (var w in batchResult.Warnings)
                    Console.WriteLine($"  - {w}");
            }

            if (engine == TargetEngine.Unity || engine == TargetEngine.Both)
            {
                Console.WriteLine();
                Console.WriteLine("Unity: Drag the output folder into your Unity Project Assets/ folder.");
                Console.WriteLine("       .meta files have been pre-generated with import settings.");
            }

            if (engine == TargetEngine.UnrealEngine || engine == TargetEngine.Both)
            {
                Console.WriteLine();
                Console.WriteLine("Unreal: Use 'Import' in Content Browser pointing to the output folder.");
                Console.WriteLine("        JSON import configs are included for automated settings.");
            }

            return batchResult.Failed > 0 ? 2 : 0;
        }

        static int CommandListFormats()
        {
            Console.WriteLine("Supported formats:");
            Console.WriteLine();
            Console.WriteLine("  Containers:");
            Console.WriteLine("    XDVDFS    - Xbox 360 DVD Filesystem (game disc ISOs)");
            Console.WriteLine("    STFS      - CON/LIVE/PIRS packages (XBLA, DLC, saves)");
            Console.WriteLine("    GOD       - Games on Demand");
            Console.WriteLine("    XEX2      - Xbox 360 Executables");
            Console.WriteLine("    UPK       - Unreal Engine 3 Packages");
            Console.WriteLine("    BIG       - EA BIG Archive");
            Console.WriteLine();
            Console.WriteLine("  Textures:");
            Console.WriteLine("    DDS       - DirectDraw Surface (DXT1/3/5, ARGB, RGB565, etc.)");
            Console.WriteLine("    XPR0/XPR2 - Xbox Packed Resource textures");
            Console.WriteLine("    Export:   -> PNG, TGA, DDS (with corrected headers)");
            Console.WriteLine();
            Console.WriteLine("  Audio:");
            Console.WriteLine("    XMA/XMA2  - Xbox Media Audio (native C# decoder)");
            Console.WriteLine("    xWMA      - Xbox WMA variant");
            Console.WriteLine("    XWB       - XACT Wave Bank (multiple entries)");
            Console.WriteLine("    XSB       - XACT Sound Bank (cue names)");
            Console.WriteLine("    ADPCM     - Xbox IMA ADPCM (decoded to PCM)");
            Console.WriteLine("    Export:   -> WAV (PCM/ADPCM), raw XMA/WMA");
            Console.WriteLine();
            Console.WriteLine("  Video:");
            Console.WriteLine("    Bink      - Bink Video (.bik) with frame extraction");
            Console.WriteLine("    XMV/WMV   - Xbox Media Video");
            Console.WriteLine("    Export:   -> raw .bik/.wmv");
            Console.WriteLine();
            Console.WriteLine("  Models:");
            Console.WriteLine("    Generic   - Vertex/Index buffer extraction");
            Console.WriteLine("    Export:   -> OBJ, FBX (ASCII), glTF 2.0");
            Console.WriteLine();
            Console.WriteLine("  Animation:");
            Console.WriteLine("    Generic   - Bone channel keyframe data");
            Console.WriteLine("    Export:   -> JSON");
            Console.WriteLine();
            Console.WriteLine("  === Unity Engine Formats ===");
            Console.WriteLine("  Unity AssetBundle:");
            Console.WriteLine("    UnityFS   - Unity File System bundle (LZ4/LZMA/uncompressed)");
            Console.WriteLine("    UnityWeb  - LZMA-compressed web bundle");
            Console.WriteLine("    UnityRaw  - Uncompressed asset bundle");
            Console.WriteLine("    .assets   - Serialized asset files");
            Console.WriteLine("    Objects:  Texture2D, Mesh, AudioClip, AnimationClip, Shader,");
            Console.WriteLine("              Material, Sprite, Font, TextAsset, GameObject");
            Console.WriteLine("    Export:   -> PNG/DDS (textures), OBJ/FBX (meshes), WAV (audio)");
            Console.WriteLine();
            Console.WriteLine("  === Unreal Engine Formats ===");
            Console.WriteLine("  Unreal PAK:");
            Console.WriteLine("    .pak      - UE4/UE5 PAK archive (Zlib/LZ4/Oodle)");
            Console.WriteLine("    .uasset   - Unreal Asset packages (UE4/UE5)");
            Console.WriteLine("    .uexp     - Export data companion files");
            Console.WriteLine("    .ubulk    - Bulk data (large textures, audio)");
            Console.WriteLine("    .upk      - Unreal Package (UE3)");
            Console.WriteLine("    Objects:  Texture2D, StaticMesh, SkeletalMesh, SoundWave,");
            Console.WriteLine("              AnimSequence, Material, Blueprint");
            Console.WriteLine("    Export:   -> DDS (textures), OBJ/FBX (meshes), OGG/WAV (audio)");
            Console.WriteLine();
            Console.WriteLine("  === Engine Reimport ===");
            Console.WriteLine("  Unity Reimport:");
            Console.WriteLine("    Auto-generates .meta files with import settings");
            Console.WriteLine("    Outputs: Assets/ImportedAssets/{Textures,Models,Audio,Animations}");
            Console.WriteLine("  Unreal Reimport:");
            Console.WriteLine("    Auto-generates JSON import configs");
            Console.WriteLine("    Outputs: Content/ImportedAssets/{Textures,Meshes,Audio,Animations}");

            return 0;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  JackTheRipper360CLI scan <path>                     Scan and list assets");
            Console.WriteLine("  JackTheRipper360CLI export <path> <output_dir>      Export all assets");
            Console.WriteLine("  JackTheRipper360CLI export <path> <out> --type=X    Export filtered by type");
            Console.WriteLine("  JackTheRipper360CLI reimport <path> <out> --engine=E Convert for engine reimport");
            Console.WriteLine("  JackTheRipper360CLI info <file>                     Show file format info");
            Console.WriteLine("  JackTheRipper360CLI list-formats                    List supported formats");
            Console.WriteLine();
            Console.WriteLine("Types: Texture, Audio, Model, Video, Animation");
            Console.WriteLine("Engines: unity, unreal, both");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  JackTheRipper360CLI scan /games/halo3/");
            Console.WriteLine("  JackTheRipper360CLI export game.iso ./extracted --type=texture");
            Console.WriteLine("  JackTheRipper360CLI reimport game.iso ./unity_assets --engine=unity");
            Console.WriteLine("  JackTheRipper360CLI reimport ./pak_files ./ue_import --engine=unreal");
            Console.WriteLine("  JackTheRipper360CLI info package.stfs");
        }

        static int MissingArg(string arg)
        {
            Console.Error.WriteLine($"Missing required argument: {arg}");
            PrintUsage();
            return 1;
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
