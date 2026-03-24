using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Parser for xWMA audio format used by Xbox 360.
    /// </summary>
    public class XwmaParser : IAssetParser
    {
        public AssetType Type => AssetType.Audio;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 12) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[4];
            stream.Read(header, 0, 4);

            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
            {
                stream.Seek(8, SeekOrigin.Begin);
                byte[] wave = new byte[4];
                stream.Read(wave, 0, 4);
                if (wave[0] == 'x' && wave[1] == 'W' && wave[2] == 'M' && wave[3] == 'A')
                    return true;

                // Also check standard WAVE with WMA format tags
                if (wave[0] == 'W' && wave[1] == 'A' && wave[2] == 'V' && wave[3] == 'E')
                {
                    stream.Seek(20, SeekOrigin.Begin);
                    byte[] fmt = new byte[2];
                    stream.Read(fmt, 0, 2);
                    ushort formatTag = (ushort)(fmt[0] | (fmt[1] << 8));
                    return formatTag == 0x0161 || formatTag == 0x0162; // WMAv2 or WMA Pro
                }
            }

            return false;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                reader.ReadString(4); // "RIFF"
                uint fileSize = reader.ReadUInt32();
                reader.ReadString(4); // "xWMA" or "WAVE"

                int channels = 0;
                int sampleRate = 0;

                // Find fmt chunk
                while (reader.Position < stream.Length - 8)
                {
                    string chunkId = reader.ReadString(4);
                    uint chunkSize = reader.ReadUInt32();
                    long chunkStart = reader.Position;

                    if (chunkId == "fmt ")
                    {
                        reader.ReadUInt16(); // format tag
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        break;
                    }

                    reader.Seek(chunkStart + chunkSize);
                }

                var entry = new AssetEntry(fileName, AssetType.Audio)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = "xWMA"
                };
                entry.Metadata["Channels"] = channels;
                entry.Metadata["SampleRate"] = sampleRate;

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string finalPath = Path.ChangeExtension(outputPath, ".wma");
            source.Seek(0, SeekOrigin.Begin);
            byte[] data = new byte[source.Length];
            source.Read(data, 0, data.Length);
            File.WriteAllBytes(finalPath, data);
            return ExportResult.Succeeded(finalPath, data.Length);
        }
    }
}
