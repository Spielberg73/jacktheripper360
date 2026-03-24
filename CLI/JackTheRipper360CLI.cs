using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;

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

            return 0;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  JackTheRipper360CLI scan <path>                     Scan and list assets");
            Console.WriteLine("  JackTheRipper360CLI export <path> <output_dir>      Export all assets");
            Console.WriteLine("  JackTheRipper360CLI export <path> <out> --type=X    Export filtered by type");
            Console.WriteLine("  JackTheRipper360CLI info <file>                     Show file format info");
            Console.WriteLine("  JackTheRipper360CLI list-formats                    List supported formats");
            Console.WriteLine();
            Console.WriteLine("Types: Texture, Audio, Model, Video, Animation");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  JackTheRipper360CLI scan /games/halo3/");
            Console.WriteLine("  JackTheRipper360CLI export game.iso ./extracted --type=texture");
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
