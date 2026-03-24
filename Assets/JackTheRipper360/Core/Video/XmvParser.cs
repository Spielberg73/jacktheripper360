using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Video
{
    /// <summary>
    /// Parser for XMV/WMV video files used by Xbox 360.
    /// XMV is based on the ASF (Advanced Systems Format) container used by WMV.
    /// </summary>
    public class XmvParser : IAssetParser
    {
        public AssetType Type => AssetType.Video;

        // ASF Header Object GUID prefix
        private static readonly byte[] ASF_HEADER = { 0x30, 0x26, 0xB2, 0x75 };

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 16) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[4];
            stream.Read(header, 0, 4);

            // Check for ASF header GUID start
            if (header[0] == ASF_HEADER[0] && header[1] == ASF_HEADER[1] &&
                header[2] == ASF_HEADER[2] && header[3] == ASF_HEADER[3])
                return true;

            // Check file extension
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext == ".xmv" || ext == ".wmv";
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                var entry = new AssetEntry(fileName, AssetType.Video)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = "XMV/WMV"
                };

                // Read ASF header GUID (16 bytes)
                byte[] headerGuid = reader.ReadBytes(16);

                // Header object size
                long headerSize = reader.ReadInt64();

                // Number of header objects
                uint numHeaders = reader.ReadUInt32();
                reader.ReadByte(); // reserved1
                reader.ReadByte(); // reserved2

                entry.Metadata["HeaderSize"] = headerSize;
                entry.Metadata["HeaderObjectCount"] = (int)numHeaders;

                // Parse sub-objects to find file properties and stream properties
                for (int i = 0; i < numHeaders && reader.Position < headerSize; i++)
                {
                    byte[] objectGuid = reader.ReadBytes(16);
                    long objectSize = reader.ReadInt64();
                    long objectEnd = reader.Position + objectSize - 24;

                    // File Properties Object GUID: A1DC AB8C 47A9 CF11 ...
                    if (objectGuid[0] == 0xA1 && objectGuid[1] == 0xDC && objectGuid[2] == 0xAB && objectGuid[3] == 0x8C)
                    {
                        reader.Skip(16); // file ID
                        long fileSize = reader.ReadInt64();
                        reader.Skip(8); // creation date
                        long dataPacketCount = reader.ReadInt64();
                        long playDuration100ns = reader.ReadInt64(); // in 100ns units
                        long sendDuration = reader.ReadInt64();
                        long preroll = reader.ReadInt64();
                        uint flags = reader.ReadUInt32();
                        uint minPacketSize = reader.ReadUInt32();
                        uint maxPacketSize = reader.ReadUInt32();
                        uint maxBitrate = reader.ReadUInt32();

                        double durationSeconds = playDuration100ns / 10000000.0;
                        entry.Metadata["Duration"] = durationSeconds;
                        entry.Metadata["Bitrate"] = (int)maxBitrate;
                    }

                    if (objectEnd > reader.Position)
                        reader.Seek(objectEnd);
                }

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string finalPath = Path.ChangeExtension(outputPath, ".wmv");
            source.Seek(0, SeekOrigin.Begin);
            byte[] data = new byte[source.Length];
            source.Read(data, 0, data.Length);
            File.WriteAllBytes(finalPath, data);
            return ExportResult.Succeeded(finalPath, data.Length);
        }
    }
}
