using System.IO;

namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Interface for format-specific asset parsers.
    /// </summary>
    public interface IAssetParser
    {
        AssetType Type { get; }
        bool CanParse(Stream stream, string fileName);
        AssetEntry Parse(Stream stream, string fileName);
        ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options);
    }
}
