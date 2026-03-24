namespace JackTheRipper360.Core.Textures
{
    public enum TextureFormat
    {
        Unknown,
        DXT1,
        DXT3,
        DXT5,
        A8R8G8B8,
        X8R8G8B8,
        R5G6B5,
        A1R5G5B5,
        A4R4G4B4,
        A8,
        L8,
        ATI1, // BC4
        ATI2, // BC5
        DXN,  // Xbox 360 normal map format
        CTX1  // Xbox 360 compressed tangent
    }

    public class TextureFormatInfo
    {
        public TextureFormat Format { get; set; }
        public int BitsPerPixel { get; set; }
        public int BlockWidth { get; set; }
        public int BlockHeight { get; set; }
        public int BlockSize { get; set; }
        public bool IsCompressed { get; set; }

        public static TextureFormatInfo Get(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 4, BlockWidth = 4, BlockHeight = 4, BlockSize = 8, IsCompressed = true };
                case TextureFormat.DXT3:
                case TextureFormat.DXT5:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 8, BlockWidth = 4, BlockHeight = 4, BlockSize = 16, IsCompressed = true };
                case TextureFormat.A8R8G8B8:
                case TextureFormat.X8R8G8B8:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 32, BlockWidth = 1, BlockHeight = 1, BlockSize = 4, IsCompressed = false };
                case TextureFormat.R5G6B5:
                case TextureFormat.A1R5G5B5:
                case TextureFormat.A4R4G4B4:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 16, BlockWidth = 1, BlockHeight = 1, BlockSize = 2, IsCompressed = false };
                case TextureFormat.A8:
                case TextureFormat.L8:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 8, BlockWidth = 1, BlockHeight = 1, BlockSize = 1, IsCompressed = false };
                case TextureFormat.ATI1:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 4, BlockWidth = 4, BlockHeight = 4, BlockSize = 8, IsCompressed = true };
                case TextureFormat.ATI2:
                case TextureFormat.DXN:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 8, BlockWidth = 4, BlockHeight = 4, BlockSize = 16, IsCompressed = true };
                default:
                    return new TextureFormatInfo { Format = format, BitsPerPixel = 32, BlockWidth = 1, BlockHeight = 1, BlockSize = 4, IsCompressed = false };
            }
        }

        /// <summary>
        /// Calculate total data size in bytes for a texture at the given resolution.
        /// </summary>
        public int CalculateDataSize(int width, int height)
        {
            if (IsCompressed)
            {
                int blocksWide = (width + BlockWidth - 1) / BlockWidth;
                int blocksHigh = (height + BlockHeight - 1) / BlockHeight;
                return blocksWide * blocksHigh * BlockSize;
            }
            return width * height * BlockSize;
        }
    }
}
