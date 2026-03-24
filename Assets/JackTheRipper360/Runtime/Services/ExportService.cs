using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Core.Plugins.Unity;
using JackTheRipper360.Core.Plugins.Unreal;
using JackTheRipper360.Core.Plugins.IdTech;
using JackTheRipper360.Core.Plugins.Source;

namespace JackTheRipper360.Runtime.Services
{
    /// <summary>
    /// Orchestrates asset export using Core exporters.
    /// Supports single and batch export with progress reporting.
    /// </summary>
    public class ExportService
    {
        private readonly List<IAssetParser> _parsers;

        public event Action<ExportProgress> OnProgress;
        public event Action<BatchExportResult> OnBatchComplete;

        public ExportService()
        {
            _parsers = new List<IAssetParser>
            {
                new Core.Textures.DdsParser(),
                new Core.Textures.XprParser(),
                new Core.Audio.XmaDecoder(),
                new Core.Audio.XwmaParser(),
                new Core.Audio.XactWaveBankReader(),
                new Core.Video.BinkVideoParser(),
                new Core.Video.XmvParser(),
                new Core.Models.Xbox360MeshParser(),
                new Core.Animation.AnimationParser(),
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
        /// Export a single asset.
        /// </summary>
        public ExportResult ExportAsset(AssetEntry entry, string outputPath, ExportOptions options)
        {
            try
            {
                string sourcePath = entry.SourcePath;
                if (sourcePath.Contains(":"))
                {
                    // Container entry - need to extract from container first
                    sourcePath = sourcePath.Split(':')[0];
                }

                using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (entry.Offset > 0)
                        stream.Seek(entry.Offset, SeekOrigin.Begin);

                    foreach (var parser in _parsers)
                    {
                        if (parser.Type == entry.Type)
                        {
                            stream.Seek(entry.Offset > 0 ? entry.Offset : 0, SeekOrigin.Begin);
                            if (parser.CanParse(stream, entry.Name))
                            {
                                stream.Seek(entry.Offset > 0 ? entry.Offset : 0, SeekOrigin.Begin);
                                return parser.Export(entry, stream, outputPath, options);
                            }
                        }
                    }

                    // Fallback: raw export
                    return ExportRaw(entry, stream, outputPath);
                }
            }
            catch (Exception ex)
            {
                return ExportResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Batch export multiple assets.
        /// </summary>
        public void BatchExport(IReadOnlyList<AssetEntry> entries, string outputDirectory, ExportOptions options)
        {
            Task.Run(() =>
            {
                var batchResult = new BatchExportResult();
                int total = entries.Count;

                for (int i = 0; i < total; i++)
                {
                    var entry = entries[i];
                    string outputPath = Path.Combine(outputDirectory, SanitizeFileName(entry.Name));

                    // Create subdirectory by type
                    string typeDir = Path.Combine(outputDirectory, entry.Type.ToString());
                    Directory.CreateDirectory(typeDir);
                    outputPath = Path.Combine(typeDir, SanitizeFileName(entry.Name));

                    var result = ExportAsset(entry, outputPath, options);

                    if (result.Success)
                        batchResult.Succeeded++;
                    else
                    {
                        batchResult.Failed++;
                        batchResult.Errors.Add($"{entry.Name}: {result.ErrorMessage}");
                    }

                    OnProgress?.Invoke(new ExportProgress
                    {
                        CurrentAsset = entry.Name,
                        AssetsProcessed = i + 1,
                        TotalAssets = total
                    });
                }

                OnBatchComplete?.Invoke(batchResult);
            });
        }

        private ExportResult ExportRaw(AssetEntry entry, Stream source, string outputPath)
        {
            string rawPath = outputPath + ".bin";
            source.Seek(entry.Offset, SeekOrigin.Begin);

            int size = (int)Math.Min(entry.Size > 0 ? entry.Size : source.Length - entry.Offset, source.Length - source.Position);
            byte[] data = new byte[size];
            source.Read(data, 0, size);
            File.WriteAllBytes(rawPath, data);

            return ExportResult.Succeeded(rawPath, data.Length);
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    public class ExportProgress
    {
        public string CurrentAsset { get; set; }
        public int AssetsProcessed { get; set; }
        public int TotalAssets { get; set; }
        public float Progress => TotalAssets > 0 ? (float)AssetsProcessed / TotalAssets : 0;
    }

    public class BatchExportResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
