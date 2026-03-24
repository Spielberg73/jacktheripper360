using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Textures
{
    /// <summary>
    /// Parser for XPR0/XPR2 texture resource files used by Xbox 360.
    /// These wrap GPU texture data with Xbox-specific headers.
    /// </summary>
    public class XprParser : IAssetParser
    {
        public AssetType Type => AssetType.Texture;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 8) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            return (magic[0] == 'X' && magic[1] == 'P' && magic[2] == 'R' && (magic[3] == '0' || magic[3] == '2'));
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);
                string magic = reader.ReadString(4);
                uint totalSize = reader.ReadUInt32();
                uint headerSize = reader.ReadUInt32();
                uint textureCount = reader.ReadUInt32();

                // Parse texture descriptor (simplified)
                int width = 0, height = 0;
                TextureFormat format = TextureFormat.Unknown;
                int dataOffset = (int)headerSize;

                if (magic == "XPR2" && textureCount > 0)
                {
                    // XPR2 has GPU texture fetch constant descriptors
                    reader.Seek(16); // After header
                    uint fetchConstant0 = reader.ReadUInt32();
                    uint fetchConstant1 = reader.ReadUInt32();
                    uint fetchConstant2 = reader.ReadUInt32();

                    // Extract dimensions from fetch constant
                    width = (int)((fetchConstant1 & 0x1FFF) + 1);
                    height = (int)(((fetchConstant1 >> 13) & 0x1FFF) + 1);

                    // Extract format from fetch constant
                    uint gpuFormat = (fetchConstant0 >> 20) & 0x3F;
                    format = DecodeGpuFormat(gpuFormat);
                }
                else if (magic == "XPR0")
                {
                    reader.Seek(12);
                    // XPR0 simpler header
                    width = reader.ReadUInt16();
                    height = reader.ReadUInt16();
                    uint formatCode = reader.ReadUInt32();
                    format = DecodeGpuFormat(formatCode & 0x3F);
                    dataOffset = reader.ReadInt32();
                }

                var entry = new AssetEntry(fileName, AssetType.Texture)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"XPR ({format})"
                };

                entry.Metadata["Width"] = width;
                entry.Metadata["Height"] = height;
                entry.Metadata["Format"] = format;
                entry.Metadata["DataOffset"] = dataOffset;
                entry.Metadata["XprVersion"] = magic;

                return entry;
            }
        }

        private TextureFormat DecodeGpuFormat(uint gpuFormat)
        {
            // Xbox 360 GPU format codes (GPUTEXTUREFORMAT enum)
            switch (gpuFormat)
            {
                case 0x04: return TextureFormat.A8;
                case 0x06: return TextureFormat.DXT1;
                case 0x07: return TextureFormat.DXT3;
                case 0x08: return TextureFormat.DXT5;
                case 0x0A: return TextureFormat.R5G6B5;
                case 0x0C: return TextureFormat.A1R5G5B5;
                case 0x0E: return TextureFormat.A4R4G4B4;
                case 0x12: return TextureFormat.A8R8G8B8;
                case 0x14: return TextureFormat.L8;
                case 0x1A: return TextureFormat.DXN;
                case 0x1E: return TextureFormat.ATI1;
                case 0x1F: return TextureFormat.ATI2;
                case 0x36: return TextureFormat.CTX1;
                default: return TextureFormat.Unknown;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            int width = (int)entry.Metadata["Width"];
            int height = (int)entry.Metadata["Height"];
            TextureFormat format = (TextureFormat)entry.Metadata["Format"];
            int dataOffset = (int)entry.Metadata["DataOffset"];

            if (width <= 0 || height <= 0)
                return ExportResult.Failed("Invalid texture dimensions.");

            source.Seek(dataOffset, SeekOrigin.Begin);
            var formatInfo = TextureFormatInfo.Get(format);
            int dataSize = formatInfo.CalculateDataSize(width, height);
            byte[] textureData = new byte[dataSize];
            int bytesRead = source.Read(textureData, 0, dataSize);

            if (bytesRead < dataSize)
                return ExportResult.Failed($"Insufficient data: expected {dataSize} bytes, got {bytesRead}.");

            byte[] untiledData = Xbox360TextureDecoder.Untile(textureData, width, height, formatInfo);
            byte[] rgbaData = Xbox360TextureDecoder.DecodeToRGBA(untiledData, width, height, format);

            string finalPath = Path.ChangeExtension(outputPath, ".png");
            byte[] pngData = TextureExporter.EncodePNG(rgbaData, width, height);
            File.WriteAllBytes(finalPath, pngData);

            return ExportResult.Succeeded(finalPath, pngData.Length);
        }
    }
}
