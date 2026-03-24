using System;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Parser for XMA/XMA2 audio format (Xbox 360 proprietary codec based on WMA Pro).
    /// Extracts header metadata and raw XMA data. Full decoding requires external tools
    /// (e.g., FFmpeg with xma support, or Microsoft's XMA decoder).
    /// </summary>
    public class XmaDecoder : IAssetParser
    {
        public AssetType Type => AssetType.Audio;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 12) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[4];
            stream.Read(header, 0, 4);

            // Check for RIFF header with XMA format
            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
            {
                stream.Seek(8, SeekOrigin.Begin);
                byte[] wave = new byte[4];
                stream.Read(wave, 0, 4);
                if (wave[0] == 'W' && wave[1] == 'A' && wave[2] == 'V' && wave[3] == 'E')
                {
                    // Check for XMA2 fmt chunk
                    stream.Seek(20, SeekOrigin.Begin);
                    byte[] fmtCode = new byte[2];
                    stream.Read(fmtCode, 0, 2);
                    ushort formatTag = (ushort)(fmtCode[0] | (fmtCode[1] << 8));
                    return formatTag == 0x0166 || formatTag == 0x0165; // XMA2 or XMA
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
                reader.ReadString(4); // "WAVE"

                int channels = 0;
                int sampleRate = 0;
                int bitsPerSample = 0;
                long dataOffset = 0;
                int dataSize = 0;
                string formatName = "XMA";

                // Parse chunks
                while (reader.Position < stream.Length - 8)
                {
                    string chunkId = reader.ReadString(4);
                    uint chunkSize = reader.ReadUInt32();
                    long chunkStart = reader.Position;

                    switch (chunkId)
                    {
                        case "fmt ":
                            ushort formatTag = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadInt32(); // avg bytes per sec
                            reader.ReadUInt16(); // block align
                            bitsPerSample = reader.ReadUInt16();

                            if (formatTag == 0x0166)
                                formatName = "XMA2";
                            else if (formatTag == 0x0165)
                                formatName = "XMA";
                            break;

                        case "data":
                            dataOffset = chunkStart;
                            dataSize = (int)chunkSize;
                            break;
                    }

                    reader.Seek(chunkStart + chunkSize);
                    reader.Align(2);
                }

                var entry = new AssetEntry(fileName, AssetType.Audio)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = formatName
                };

                entry.Metadata["Channels"] = channels;
                entry.Metadata["SampleRate"] = sampleRate;
                entry.Metadata["BitsPerSample"] = bitsPerSample;
                entry.Metadata["DataOffset"] = dataOffset;
                entry.Metadata["DataSize"] = dataSize;

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            // Export as raw XMA with RIFF wrapper for external decoding
            string finalPath = Path.ChangeExtension(outputPath, ".xma");

            source.Seek(0, SeekOrigin.Begin);
            byte[] data = new byte[source.Length];
            source.Read(data, 0, data.Length);
            File.WriteAllBytes(finalPath, data);

            return ExportResult.Succeeded(finalPath, data.Length);
        }
    }
}
