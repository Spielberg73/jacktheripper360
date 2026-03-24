using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// Multi-threaded asset scanner using Parallel.ForEach for significantly
    /// faster scanning of large game directories.
    /// </summary>
    public class ParallelAssetScanner
    {
        private readonly AssetDatabase _database;
        private readonly List<IAssetParser> _parsers;
        private readonly PluginRegistry _pluginRegistry;
        private int _maxDegreeOfParallelism;

        public ParallelAssetScanner(AssetDatabase database, PluginRegistry pluginRegistry = null,
            int maxParallelism = 0)
        {
            _database = database;
            _pluginRegistry = pluginRegistry;
            _maxDegreeOfParallelism = maxParallelism > 0 ? maxParallelism : Environment.ProcessorCount;

            _parsers = new List<IAssetParser>
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

            // Add plugin parsers
            if (_pluginRegistry != null)
                _parsers.AddRange(_pluginRegistry.GetAllParsers());
        }

        /// <summary>
        /// Scan a directory using multiple threads.
        /// </summary>
        public ScanResult ScanDirectoryParallel(string path, IProgress<ScanProgress> progress = null,
            CancellationToken cancellationToken = default)
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
                result.ErrorMessage = $"Error enumerating directory: {ex.Message}";
                return result;
            }

            var errors = new ConcurrentBag<string>();
            int assetsFound = 0;
            int containersFound = 0;
            int processed = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            try
            {
                Parallel.ForEach(files, parallelOptions, filePath =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fileResult = ScanSingleFile(filePath);
                        if (fileResult != null)
                        {
                            _database.AddEntry(fileResult);
                            Interlocked.Increment(ref assetsFound);

                            if (fileResult.Type == AssetType.Container)
                                Interlocked.Increment(ref containersFound);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{filePath}: {ex.Message}");
                    }

                    int current = Interlocked.Increment(ref processed);
                    progress?.Report(new ScanProgress
                    {
                        CurrentFile = filePath,
                        FilesProcessed = current,
                        TotalFiles = files.Length
                    });
                });
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "Scan cancelled.";
            }

            result.AssetsFound = assetsFound;
            result.ContainersFound = containersFound;
            result.Errors = new List<string>(errors);

            return result;
        }

        private AssetEntry ScanSingleFile(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var match = FormatDetector.Identify(stream);

                if (match.Confidence < 0.1f)
                    match = FormatDetector.IdentifyByExtension(filePath);

                if (match.Type == AssetType.Unknown)
                    return null;

                // Try to parse with specific parser
                foreach (var parser in _parsers)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    if (parser.CanParse(stream, filePath))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        var entry = parser.Parse(stream, filePath);
                        if (entry != null)
                        {
                            entry.SourcePath = filePath;
                            return entry;
                        }
                    }
                }

                // Return generic entry
                return new AssetEntry(Path.GetFileName(filePath), match.Type)
                {
                    SourcePath = filePath,
                    Size = stream.Length,
                    FormatName = match.FormatName
                };
            }
        }
    }
}
