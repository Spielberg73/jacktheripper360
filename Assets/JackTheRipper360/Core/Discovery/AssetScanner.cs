using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;
using JackTheRipper360.Core.Plugins.Unity;
using JackTheRipper360.Core.Plugins.Unreal;
using JackTheRipper360.Core.Plugins.IdTech;
using JackTheRipper360.Core.Plugins.Source;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// Scans directories and container files to discover all Xbox 360 assets.
    /// Reports progress via IProgress for async UI updates.
    /// </summary>
    public class AssetScanner
    {
        private readonly AssetDatabase _database;
        private readonly List<IContainerReader> _containerReaders;
        private readonly List<IAssetParser> _assetParsers;

        public AssetScanner(AssetDatabase database, List<IAssetParser> parsers = null)
        {
            _database = database;
            _containerReaders = new List<IContainerReader>
            {
                new XdvdfsReader(),
                new StfsReader(),
                new XexParser(),
                new GenericArchiveReader(),
                new UnrealPakReader(),
                // id Tech containers
                new IdTechPakReader(),
                new IdTechPk3Reader(),
                // Source Engine containers
                new SourceVpkReader()
            };

            _assetParsers = parsers ?? CreateDefaultParsers();
        }

        private static List<IAssetParser> CreateDefaultParsers()
        {
            return new List<IAssetParser>
            {
                new Textures.DdsParser(),
                new Textures.XprParser(),
                new Audio.XmaDecoder(),
                new Audio.XwmaParser(),
                new Audio.XactWaveBankReader(),
                new Video.BinkVideoParser(),
                new Video.XmvParser(),
                new Models.Xbox360MeshParser(),
                new Animation.AnimationParser(),
                // Unity Engine parsers
                new UnityAssetParser(),
                // Unreal Engine parsers
                new UnrealAssetParser(),
                // id Tech parsers
                new IdTechWadParser(),
                new IdTechBspParser(),
                // Source Engine parsers
                new SourceVtfParser(),
                new SourceMdlParser()
            };
        }

        /// <summary>
        /// Scan a directory for Xbox 360 game assets.
        /// </summary>
        public ScanResult ScanDirectory(string path, IProgress<ScanProgress> progress = null)
        {
            var result = new ScanResult();

            if (!Directory.Exists(path))
            {
                result.ErrorMessage = $"Directory not found: {path}";
                return result;
            }

            // Enumerate files safely - Directory.GetFiles with AllDirectories
            // throws if ANY subdirectory is inaccessible (common on Windows)
            var files = new List<string>();
            EnumerateFilesSafe(path, files, result);

            if (files.Count == 0 && result.Errors.Count > 0)
            {
                result.ErrorMessage = $"No accessible files found. {result.Errors.Count} access errors.";
                return result;
            }

            int processed = 0;
            foreach (string file in files)
            {
                try
                {
                    ScanFile(file, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{file}: {ex.Message}");
                }

                processed++;
                progress?.Report(new ScanProgress
                {
                    CurrentFile = file,
                    FilesProcessed = processed,
                    TotalFiles = files.Count
                });
            }

            return result;
        }

        private void EnumerateFilesSafe(string directory, List<string> files, ScanResult result)
        {
            try
            {
                foreach (string file in Directory.GetFiles(directory))
                    files.Add(file);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Access denied: {directory}: {ex.Message}");
            }

            try
            {
                foreach (string subDir in Directory.GetDirectories(directory))
                    EnumerateFilesSafe(subDir, files, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Access denied: {directory}: {ex.Message}");
            }
        }

        /// <summary>
        /// Scan a single file for Xbox 360 assets.
        /// </summary>
        public void ScanFile(string filePath, ScanResult result)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Try format detection
                var match = FormatDetector.Identify(stream);

                if (match.Confidence < 0.1f)
                {
                    // Try extension-based detection
                    match = FormatDetector.IdentifyByExtension(filePath);
                }

                if (match.Type == AssetType.Unknown)
                    return;

                // If it's a container, open it and scan contents
                if (match.Type == AssetType.Container || match.Type == AssetType.Executable)
                {
                    ScanContainer(stream, filePath, match, result);
                    return;
                }

                // Try to parse as an asset
                foreach (var parser in _assetParsers)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    if (parser.CanParse(stream, filePath))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        var entry = parser.Parse(stream, filePath);
                        if (entry != null)
                        {
                            entry.SourcePath = filePath;
                            _database.AddEntry(entry);
                            result.AssetsFound++;
                            return;
                        }
                    }
                }

                // Add as generic entry
                var genericEntry = new AssetEntry(Path.GetFileName(filePath), match.Type)
                {
                    SourcePath = filePath,
                    Size = stream.Length,
                    FormatName = match.FormatName
                };
                _database.AddEntry(genericEntry);
                result.AssetsFound++;
            }
        }

        private void ScanContainer(Stream stream, string filePath, FormatDetector.FormatMatch match, ScanResult result)
        {
            foreach (var reader in _containerReaders)
            {
                stream.Seek(0, SeekOrigin.Begin);
                if (reader.CanRead(stream))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    var info = reader.ReadHeader(stream);

                    var containerEntry = new AssetEntry(Path.GetFileName(filePath), AssetType.Container)
                    {
                        SourcePath = filePath,
                        Size = stream.Length,
                        FormatName = info.Format
                    };
                    containerEntry.Metadata["Title"] = info.Title ?? "";
                    containerEntry.Metadata["EntryCount"] = info.EntryCount;

                    // Copy entries to avoid collection modification during iteration
                    var entries = new List<ContainerEntry>(reader.GetEntries());

                    foreach (var entry in entries)
                    {
                        if (entry.IsDirectory) continue;

                        try
                        {
                            string safeName = SanitizeName(entry.Name);
                            string safePath = SanitizeName(entry.Path);

                            using (var entryStream = reader.OpenEntry(entry))
                            {
                                var entryMatch = FormatDetector.Identify(entryStream);
                                if (entryMatch.Confidence < 0.1f)
                                    entryMatch = FormatDetector.IdentifyByExtension(safeName);

                                var assetEntry = new AssetEntry(safeName, entryMatch.Type)
                                {
                                    SourcePath = $"{filePath}|{safePath}",
                                    Offset = entry.Offset,
                                    Size = entry.Size,
                                    FormatName = entryMatch.FormatName
                                };

                                // Try detailed parsing
                                foreach (var parser in _assetParsers)
                                {
                                    entryStream.Seek(0, SeekOrigin.Begin);
                                    if (parser.CanParse(entryStream, safeName))
                                    {
                                        entryStream.Seek(0, SeekOrigin.Begin);
                                        var parsed = parser.Parse(entryStream, safeName);
                                        if (parsed != null)
                                        {
                                            parsed.SourcePath = assetEntry.SourcePath;
                                            parsed.Offset = entry.Offset;
                                            assetEntry = parsed;
                                            break;
                                        }
                                    }
                                }

                                containerEntry.Children.Add(assetEntry);
                                result.AssetsFound++;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Don't spam errors for binary-named entries
                            if (result.Errors.Count < 50)
                                result.Errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                        }
                    }

                    _database.AddEntry(containerEntry);
                    result.ContainersFound++;
                    return;
                }
            }
        }

        /// <summary>
        /// Replace non-printable and path-illegal characters in entry names.
        /// XEX resources often have binary names that cause path errors.
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c < 32 || c > 126 || c == ':' || c == '<' || c == '>' || c == '"' ||
                    c == '|' || c == '?' || c == '*')
                    chars[i] = '_';
            }
            return new string(chars).TrimEnd('_', ' ');
        }
    }

    public class ScanResult
    {
        public int AssetsFound { get; set; }
        public int ContainersFound { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }

    public class ScanProgress
    {
        public string CurrentFile { get; set; }
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public float Progress => TotalFiles > 0 ? (float)FilesProcessed / TotalFiles : 0;
    }
}
