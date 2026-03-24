using System;
using System.Collections.Generic;
using System.IO;

namespace JackTheRipper360.Core.Textures
{
    /// <summary>
    /// Handles mipmap chain reconstruction and full DDS export with all mip levels.
    /// </summary>
    public static class MipmapProcessor
    {
        /// <summary>
        /// Calculate total data size for a texture with all its mipmap levels.
        /// </summary>
        public static int CalculateMipmapChainSize(int width, int height, TextureFormatInfo formatInfo, int mipCount)
        {
            int totalSize = 0;
            int w = width;
            int h = height;

            for (int i = 0; i < mipCount; i++)
            {
                totalSize += formatInfo.CalculateDataSize(w, h);
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            return totalSize;
        }

        /// <summary>
        /// Calculate the number of mipmap levels for a given resolution.
        /// </summary>
        public static int CalculateMipCount(int width, int height)
        {
            int maxDim = Math.Max(width, height);
            int mipCount = 1;
            while (maxDim > 1)
            {
                maxDim /= 2;
                mipCount++;
            }
            return mipCount;
        }

        /// <summary>
        /// Extract individual mipmap levels from a contiguous data buffer.
        /// </summary>
        public static List<MipmapLevel> ExtractMipmaps(byte[] data, int width, int height,
            TextureFormatInfo formatInfo, int mipCount)
        {
            var mipmaps = new List<MipmapLevel>();
            int offset = 0;
            int w = width;
            int h = height;

            for (int i = 0; i < mipCount; i++)
            {
                int mipSize = formatInfo.CalculateDataSize(w, h);
                if (offset + mipSize > data.Length) break;

                byte[] mipData = new byte[mipSize];
                Buffer.BlockCopy(data, offset, mipData, 0, mipSize);

                mipmaps.Add(new MipmapLevel
                {
                    Level = i,
                    Width = w,
                    Height = h,
                    Data = mipData,
                    Offset = offset,
                    Size = mipSize
                });

                offset += mipSize;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }

            return mipmaps;
        }

        /// <summary>
        /// Generate mipmaps from a base RGBA image by averaging 2x2 blocks.
        /// </summary>
        public static List<MipmapLevel> GenerateMipmaps(byte[] rgbaData, int width, int height)
        {
            var mipmaps = new List<MipmapLevel>();

            mipmaps.Add(new MipmapLevel
            {
                Level = 0,
                Width = width,
                Height = height,
                Data = (byte[])rgbaData.Clone()
            });

            int w = width;
            int h = height;
            byte[] currentData = rgbaData;

            while (w > 1 || h > 1)
            {
                int newW = Math.Max(1, w / 2);
                int newH = Math.Max(1, h / 2);
                byte[] newData = new byte[newW * newH * 4];

                for (int y = 0; y < newH; y++)
                {
                    for (int x = 0; x < newW; x++)
                    {
                        int sx = x * 2;
                        int sy = y * 2;

                        // Average 2x2 block
                        int r = 0, g = 0, b = 0, a = 0;
                        int count = 0;

                        for (int dy = 0; dy < 2 && sy + dy < h; dy++)
                        {
                            for (int dx = 0; dx < 2 && sx + dx < w; dx++)
                            {
                                int srcIdx = ((sy + dy) * w + (sx + dx)) * 4;
                                if (srcIdx + 3 < currentData.Length)
                                {
                                    r += currentData[srcIdx];
                                    g += currentData[srcIdx + 1];
                                    b += currentData[srcIdx + 2];
                                    a += currentData[srcIdx + 3];
                                    count++;
                                }
                            }
                        }

                        if (count > 0)
                        {
                            int dstIdx = (y * newW + x) * 4;
                            newData[dstIdx] = (byte)(r / count);
                            newData[dstIdx + 1] = (byte)(g / count);
                            newData[dstIdx + 2] = (byte)(b / count);
                            newData[dstIdx + 3] = (byte)(a / count);
                        }
                    }
                }

                mipmaps.Add(new MipmapLevel
                {
                    Level = mipmaps.Count,
                    Width = newW,
                    Height = newH,
                    Data = newData
                });

                w = newW;
                h = newH;
                currentData = newData;
            }

            return mipmaps;
        }

        /// <summary>
        /// Export a texture with all mipmaps as a proper DDS file with corrected headers.
        /// </summary>
        public static byte[] ExportAsDDS(byte[] textureData, int width, int height,
            TextureFormat format, int mipCount)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // DDS magic
                writer.Write(0x20534444); // "DDS "

                // DDS header (124 bytes)
                writer.Write(124); // header size
                uint flags = 0x1 | 0x2 | 0x4 | 0x1000; // CAPS | HEIGHT | WIDTH | PIXELFORMAT
                if (mipCount > 1) flags |= 0x20000; // MIPMAPCOUNT
                flags |= 0x80000; // LINEARSIZE
                writer.Write(flags);
                writer.Write(height);
                writer.Write(width);

                var formatInfo = TextureFormatInfo.Get(format);
                int pitchOrLinearSize = formatInfo.CalculateDataSize(width, height);
                writer.Write(pitchOrLinearSize);

                writer.Write(0); // depth
                writer.Write(mipCount);

                // Reserved (11 DWORDs)
                for (int i = 0; i < 11; i++)
                    writer.Write(0);

                // Pixel format (32 bytes)
                writer.Write(32); // struct size
                WriteDDSPixelFormat(writer, format);

                // Caps
                uint caps = 0x1000; // TEXTURE
                if (mipCount > 1) caps |= 0x8 | 0x400000; // COMPLEX | MIPMAP
                writer.Write(caps);
                writer.Write(0); // caps2
                writer.Write(0); // caps3
                writer.Write(0); // caps4
                writer.Write(0); // reserved

                // Write texture data (all mip levels)
                writer.Write(textureData, 0, Math.Min(textureData.Length,
                    CalculateMipmapChainSize(width, height, formatInfo, mipCount)));

                return ms.ToArray();
            }
        }

        private static void WriteDDSPixelFormat(BinaryWriter writer, TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                    writer.Write(0x4); // DDPF_FOURCC
                    writer.Write(0x31545844); // "DXT1"
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    break;
                case TextureFormat.DXT3:
                    writer.Write(0x4);
                    writer.Write(0x33545844); // "DXT3"
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    break;
                case TextureFormat.DXT5:
                    writer.Write(0x4);
                    writer.Write(0x35545844); // "DXT5"
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    break;
                case TextureFormat.A8R8G8B8:
                    writer.Write(0x41); // DDPF_RGB | DDPF_ALPHAPIXELS
                    writer.Write(0); // no fourcc
                    writer.Write(32); // bit count
                    writer.Write(0x00FF0000); // R mask
                    writer.Write(0x0000FF00); // G mask
                    writer.Write(0x000000FF); // B mask
                    writer.Write(unchecked((int)0xFF000000)); // A mask
                    break;
                default:
                    writer.Write(0x40); // DDPF_RGB
                    writer.Write(0);
                    writer.Write(32);
                    writer.Write(0x00FF0000); writer.Write(0x0000FF00);
                    writer.Write(0x000000FF); writer.Write(0);
                    break;
            }
        }
    }

    public class MipmapLevel
    {
        public int Level { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Data { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }
    }
}
