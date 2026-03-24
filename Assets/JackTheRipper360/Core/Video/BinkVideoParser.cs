using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Video
{
    /// <summary>
    /// Parser for Bink Video (.bik) files commonly used in Xbox 360 games.
    /// Extracts header metadata and frame information.
    /// </summary>
    public class BinkVideoParser : IAssetParser
    {
        public AssetType Type => AssetType.Video;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 8) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[3];
            stream.Read(magic, 0, 3);
            return magic[0] == 'B' && magic[1] == 'I' && magic[2] == 'K';
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                string magic = reader.ReadString(4); // "BIKi" where i is version
                uint fileSize = reader.ReadUInt32();
                uint frameCount = reader.ReadUInt32();
                uint largestFrameSize = reader.ReadUInt32();
                reader.ReadUInt32(); // last frame size (internal)

                uint width = reader.ReadUInt32();
                uint height = reader.ReadUInt32();

                uint fpsDividend = reader.ReadUInt32();
                uint fpsDivisor = reader.ReadUInt32();
                float fps = fpsDivisor > 0 ? (float)fpsDividend / fpsDivisor : 30f;

                uint videoFlags = reader.ReadUInt32();
                uint audioTrackCount = reader.ReadUInt32();

                var entry = new AssetEntry(fileName, AssetType.Video)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"Bink Video ({magic.TrimEnd('\0')})"
                };

                entry.Metadata["Width"] = (int)width;
                entry.Metadata["Height"] = (int)height;
                entry.Metadata["FrameCount"] = (int)frameCount;
                entry.Metadata["FPS"] = fps;
                entry.Metadata["AudioTracks"] = (int)audioTrackCount;
                entry.Metadata["Version"] = magic.TrimEnd('\0');

                // Read audio track info
                if (audioTrackCount > 0 && audioTrackCount < 256)
                {
                    for (int i = 0; i < audioTrackCount; i++)
                    {
                        reader.ReadUInt16(); // audio unknown
                        ushort audioChannels = reader.ReadUInt16();
                        entry.Metadata[$"AudioTrack{i}_Channels"] = (int)audioChannels;
                    }

                    for (int i = 0; i < audioTrackCount; i++)
                    {
                        uint sampleRate = reader.ReadUInt16();
                        ushort flags = reader.ReadUInt16();
                        entry.Metadata[$"AudioTrack{i}_SampleRate"] = (int)sampleRate;
                    }
                }

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            // Export as raw .bik file (Bink is a proprietary format)
            string finalPath = Path.ChangeExtension(outputPath, ".bik");

            source.Seek(0, SeekOrigin.Begin);
            byte[] data = new byte[source.Length];
            source.Read(data, 0, data.Length);
            File.WriteAllBytes(finalPath, data);

            return ExportResult.Succeeded(finalPath, data.Length);
        }
    }
}
