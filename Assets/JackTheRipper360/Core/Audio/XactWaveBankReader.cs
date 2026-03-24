using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Reader for XACT Wave Bank (.xwb) files.
    /// Extracts individual audio entries from Xbox 360 wave banks.
    /// </summary>
    public class XactWaveBankReader : IAssetParser
    {
        public AssetType Type => AssetType.Audio;

        // Wave bank codec types
        private const int WAVEBANKMINIFORMAT_TAG_PCM = 0x0;
        private const int WAVEBANKMINIFORMAT_TAG_XMA = 0x1;
        private const int WAVEBANKMINIFORMAT_TAG_ADPCM = 0x2;
        private const int WAVEBANKMINIFORMAT_TAG_WMA = 0x3;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 12) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            // "WBND" or "DNBW" (depending on endianness)
            return (magic[0] == 'W' && magic[1] == 'B' && magic[2] == 'N' && magic[3] == 'D') ||
                   (magic[0] == 'D' && magic[1] == 'N' && magic[2] == 'B' && magic[3] == 'W');
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            bool isBigEndian = false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);
            isBigEndian = (magic[0] == 'W'); // "WBND" = big-endian Xbox 360

            using (var reader = new EndianBinaryReader(stream, isBigEndian, leaveOpen: true))
            {
                reader.Seek(4);
                uint version = reader.ReadUInt32();
                uint headerVersion = reader.ReadUInt32();

                // Read segment offsets and lengths (5 segments)
                long[] segOffsets = new long[5];
                long[] segLengths = new long[5];
                for (int i = 0; i < 5; i++)
                {
                    segOffsets[i] = reader.ReadUInt32();
                    segLengths[i] = reader.ReadUInt32();
                }

                // Segment 0: Bank data
                // Segment 1: Entry metadata
                // Segment 2: Seek tables (optional)
                // Segment 3: Entry names (optional)
                // Segment 4: Wave data

                // Read bank data
                reader.Seek(segOffsets[0]);
                uint flags = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();
                string bankName = reader.ReadString(64);

                uint entryMetadataSize = reader.ReadUInt32();
                uint entryNameSize = reader.ReadUInt32();
                uint alignment = reader.ReadUInt32();

                // Compact format flag
                bool isCompact = (flags & 0x00020000) != 0;

                var parentEntry = new AssetEntry(fileName, AssetType.Audio)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"XACT Wave Bank ({entryCount} entries)"
                };
                parentEntry.Metadata["BankName"] = bankName.TrimEnd('\0');
                parentEntry.Metadata["EntryCount"] = (int)entryCount;
                parentEntry.Metadata["Version"] = (int)version;

                // Read entry names if available
                string[] entryNames = null;
                if (segLengths[3] > 0)
                {
                    entryNames = new string[entryCount];
                    reader.Seek(segOffsets[3]);
                    for (int i = 0; i < entryCount; i++)
                    {
                        entryNames[i] = reader.ReadString(64).TrimEnd('\0');
                    }
                }

                // Read entries
                reader.Seek(segOffsets[1]);
                for (int i = 0; i < entryCount; i++)
                {
                    string entryName = entryNames != null && i < entryNames.Length
                        ? entryNames[i] : $"wave_{i:D4}";

                    if (!isCompact)
                    {
                        // Standard entry format (24 bytes per entry)
                        uint flagsAndDuration = reader.ReadUInt32();
                        uint formatTag = reader.ReadUInt32();
                        uint dataOffset = reader.ReadUInt32();
                        uint dataLength = reader.ReadUInt32();
                        uint loopStart = reader.ReadUInt32();
                        uint loopLength = reader.ReadUInt32();

                        int codec = (int)(formatTag & 0x3);
                        int channels = (int)((formatTag >> 2) & 0x7) + 1;
                        int sampleRate = (int)((formatTag >> 5) & 0x3FFFF) + 1;
                        int blockAlign = (int)((formatTag >> 23) & 0xFF);

                        string codecName;
                        switch (codec)
                        {
                            case WAVEBANKMINIFORMAT_TAG_PCM: codecName = "PCM"; break;
                            case WAVEBANKMINIFORMAT_TAG_XMA: codecName = "XMA"; break;
                            case WAVEBANKMINIFORMAT_TAG_ADPCM: codecName = "ADPCM"; break;
                            case WAVEBANKMINIFORMAT_TAG_WMA: codecName = "WMA"; break;
                            default: codecName = "Unknown"; break;
                        }

                        var child = new AssetEntry(entryName, AssetType.Audio)
                        {
                            Offset = segOffsets[4] + dataOffset,
                            Size = dataLength,
                            FormatName = codecName
                        };
                        child.Metadata["Codec"] = codec;
                        child.Metadata["Channels"] = channels;
                        child.Metadata["SampleRate"] = sampleRate;
                        child.Metadata["BlockAlign"] = blockAlign;
                        child.Metadata["DataOffset"] = segOffsets[4] + dataOffset;
                        child.Metadata["DataSize"] = (int)dataLength;

                        parentEntry.Children.Add(child);
                    }
                    else
                    {
                        // Compact entry format
                        uint compactEntry = reader.ReadUInt32();
                        uint dataOffset = (compactEntry >> 4) * alignment;

                        var child = new AssetEntry(entryName, AssetType.Audio)
                        {
                            Offset = segOffsets[4] + dataOffset,
                            Size = 0, // Unknown in compact format
                            FormatName = "Compact"
                        };
                        child.Metadata["DataOffset"] = segOffsets[4] + dataOffset;

                        parentEntry.Children.Add(child);
                    }
                }

                return parentEntry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            // If this is a parent wave bank entry, export all children
            if (entry.Children.Count > 0)
            {
                string dir = Path.GetDirectoryName(outputPath);
                string baseName = Path.GetFileNameWithoutExtension(outputPath);
                string exportDir = Path.Combine(dir, baseName);
                Directory.CreateDirectory(exportDir);

                int exported = 0;
                foreach (var child in entry.Children)
                {
                    var result = ExportSingleEntry(child, source, Path.Combine(exportDir, child.Name), options);
                    if (result.Success) exported++;
                }

                return ExportResult.Succeeded(exportDir);
            }

            return ExportSingleEntry(entry, source, outputPath, options);
        }

        private ExportResult ExportSingleEntry(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            if (!entry.Metadata.ContainsKey("DataOffset") || !entry.Metadata.ContainsKey("DataSize"))
                return ExportResult.Failed("Missing data offset/size metadata.");

            long dataOffset = Convert.ToInt64(entry.Metadata["DataOffset"]);
            int dataSize = Convert.ToInt32(entry.Metadata["DataSize"]);

            if (dataSize <= 0)
                return ExportResult.Failed("Invalid data size.");

            source.Seek(dataOffset, SeekOrigin.Begin);
            byte[] audioData = new byte[dataSize];
            source.Read(audioData, 0, dataSize);

            int codec = entry.Metadata.ContainsKey("Codec") ? (int)entry.Metadata["Codec"] : -1;
            int channels = entry.Metadata.ContainsKey("Channels") ? (int)entry.Metadata["Channels"] : 1;
            int sampleRate = entry.Metadata.ContainsKey("SampleRate") ? (int)entry.Metadata["SampleRate"] : 44100;

            if (codec == WAVEBANKMINIFORMAT_TAG_PCM)
            {
                // Direct PCM - export as WAV
                string wavPath = Path.ChangeExtension(outputPath, ".wav");
                WavWriter.WriteToFile(wavPath, audioData, sampleRate, channels, 16);
                return ExportResult.Succeeded(wavPath, audioData.Length);
            }
            else
            {
                // XMA/ADPCM/WMA - export raw data for external decoding
                string ext = codec == WAVEBANKMINIFORMAT_TAG_XMA ? ".xma" :
                             codec == WAVEBANKMINIFORMAT_TAG_WMA ? ".wma" : ".bin";
                string rawPath = Path.ChangeExtension(outputPath, ext);
                File.WriteAllBytes(rawPath, audioData);
                return ExportResult.Succeeded(rawPath, audioData.Length);
            }
        }
    }
}
