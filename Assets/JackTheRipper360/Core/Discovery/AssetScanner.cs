using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;

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
                new GenericArchiveReader()
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
                new Animation.AnimationParser()
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

            string[] files;
            try
            {
                files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error scanning directory: {ex.Message}";
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
                    TotalFiles = files.Length
                });
            }

            return result;
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
                    containerEntry.Metadata["Title"] = info.Title;
                    containerEntry.Metadata["EntryCount"] = info.EntryCount;

                    // Scan entries inside the container
                    foreach (var entry in reader.GetEntries())
                    {
                        if (entry.IsDirectory) continue;

                        try
                        {
                            using (var entryStream = reader.OpenEntry(entry))
                            {
                                var entryMatch = FormatDetector.Identify(entryStream);
                                if (entryMatch.Confidence < 0.1f)
                                    entryMatch = FormatDetector.IdentifyByExtension(entry.Name);

                                var assetEntry = new AssetEntry(entry.Name, entryMatch.Type)
                                {
                                    SourcePath = $"{filePath}:{entry.Path}",
                                    Offset = entry.Offset,
                                    Size = entry.Size,
                                    FormatName = entryMatch.FormatName
                                };

                                // Try detailed parsing
                                foreach (var parser in _assetParsers)
                                {
                                    entryStream.Seek(0, SeekOrigin.Begin);
                                    if (parser.CanParse(entryStream, entry.Name))
                                    {
                                        entryStream.Seek(0, SeekOrigin.Begin);
                                        var parsed = parser.Parse(entryStream, entry.Name);
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
                            result.Errors.Add($"{filePath}:{entry.Path}: {ex.Message}");
                        }
                    }

                    _database.AddEntry(containerEntry);
                    result.ContainersFound++;
                    return;
                }
            }
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
