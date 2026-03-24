using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Video
{
    /// <summary>
    /// Orchestrates video export from Xbox 360 video formats.
    /// </summary>
    public static class VideoExporter
    {
        public static ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string format = entry.FormatName?.ToUpperInvariant() ?? "";

            string ext;
            if (format.Contains("BINK") || format.Contains("BIK"))
                ext = ".bik";
            else if (format.Contains("XMV") || format.Contains("WMV"))
                ext = ".wmv";
            else
                ext = ".bin";

            string finalPath = Path.ChangeExtension(outputPath, ext);

            source.Seek(0, SeekOrigin.Begin);
            using (var fs = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                long totalBytes = 0;
                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fs.Write(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }
                return ExportResult.Succeeded(finalPath, totalBytes);
            }
        }
    }
}
