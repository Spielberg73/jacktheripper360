using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Textures
{
    /// <summary>
    /// Parser for DDS (DirectDraw Surface) texture files.
    /// Xbox 360 DDS textures may have swizzled/tiled pixel data.
    /// </summary>
    public class DdsParser : IAssetParser
    {
        public AssetType Type => AssetType.Texture;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < 128) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);
            return magic[0] == 0x44 && magic[1] == 0x44 && magic[2] == 0x53 && magic[3] == 0x20;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                reader.ReadUInt32(); // magic "DDS "

                // DDS header
                uint headerSize = reader.ReadUInt32();
                uint flags = reader.ReadUInt32();
                uint height = reader.ReadUInt32();
                uint width = reader.ReadUInt32();
                uint pitchOrLinearSize = reader.ReadUInt32();
                uint depth = reader.ReadUInt32();
                uint mipMapCount = reader.ReadUInt32();

                reader.Skip(44); // reserved

                // Pixel format
                uint pfSize = reader.ReadUInt32();
                uint pfFlags = reader.ReadUInt32();
                uint fourCC = reader.ReadUInt32();
                uint rgbBitCount = reader.ReadUInt32();
                uint rMask = reader.ReadUInt32();
                uint gMask = reader.ReadUInt32();
                uint bMask = reader.ReadUInt32();
                uint aMask = reader.ReadUInt32();

                TextureFormat texFormat = IdentifyFormat(pfFlags, fourCC, rgbBitCount);

                var entry = new AssetEntry(fileName, AssetType.Texture)
                {
                    SourcePath = fileName,
                    Size = stream.Length,
                    FormatName = $"DDS ({texFormat})"
                };

                entry.Metadata["Width"] = (int)width;
                entry.Metadata["Height"] = (int)height;
                entry.Metadata["MipMaps"] = (int)mipMapCount;
                entry.Metadata["Format"] = texFormat;
                entry.Metadata["Depth"] = (int)depth;
                entry.Metadata["DataOffset"] = (int)(4 + headerSize);

                return entry;
            }
        }

        private TextureFormat IdentifyFormat(uint pfFlags, uint fourCC, uint rgbBitCount)
        {
            if ((pfFlags & Xbox360Constants.DDPF_FOURCC) != 0)
            {
                switch (fourCC)
                {
                    case Xbox360Constants.FOURCC_DXT1: return TextureFormat.DXT1;
                    case Xbox360Constants.FOURCC_DXT3: return TextureFormat.DXT3;
                    case Xbox360Constants.FOURCC_DXT5: return TextureFormat.DXT5;
                    case Xbox360Constants.FOURCC_ATI1: return TextureFormat.ATI1;
                    case Xbox360Constants.FOURCC_ATI2: return TextureFormat.ATI2;
                }
            }

            if ((pfFlags & Xbox360Constants.DDPF_RGB) != 0)
            {
                if (rgbBitCount == 32) return TextureFormat.A8R8G8B8;
                if (rgbBitCount == 16) return TextureFormat.R5G6B5;
            }

            if (rgbBitCount == 8) return TextureFormat.L8;

            return TextureFormat.Unknown;
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            int width = (int)entry.Metadata["Width"];
            int height = (int)entry.Metadata["Height"];
            TextureFormat format = (TextureFormat)entry.Metadata["Format"];
            int dataOffset = (int)entry.Metadata["DataOffset"];

            // Read texture data
            source.Seek(dataOffset, SeekOrigin.Begin);
            var formatInfo = TextureFormatInfo.Get(format);
            int dataSize = formatInfo.CalculateDataSize(width, height);
            byte[] textureData = new byte[dataSize];
            source.Read(textureData, 0, dataSize);

            // Untile if Xbox 360 format
            byte[] untiledData = Xbox360TextureDecoder.Untile(textureData, width, height, formatInfo);

            // Decode to RGBA
            byte[] rgbaData = Xbox360TextureDecoder.DecodeToRGBA(untiledData, width, height, format);

            // Export as PNG
            string finalPath = Path.ChangeExtension(outputPath, ".png");
            byte[] pngData = TextureExporter.EncodePNG(rgbaData, width, height);
            File.WriteAllBytes(finalPath, pngData);

            return ExportResult.Succeeded(finalPath, pngData.Length);
        }
    }
}
