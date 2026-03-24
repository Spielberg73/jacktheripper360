using System;

namespace JackTheRipper360.Core.Textures
{
    /// <summary>
    /// Handles Xbox 360 GPU-specific texture untiling/unswizzling and decoding.
    /// Xbox 360 GPU stores textures in a tiled/swizzled memory layout for optimal
    /// memory access patterns. This decoder reverses that process.
    /// </summary>
    public static class Xbox360TextureDecoder
    {
        /// <summary>
        /// Untile Xbox 360 GPU tiled texture data to linear layout.
        /// </summary>
        public static byte[] Untile(byte[] tiledData, int width, int height, TextureFormatInfo formatInfo)
        {
            if (formatInfo.IsCompressed)
            {
                // For block-compressed formats, work in block units
                int blockWidth = (width + formatInfo.BlockWidth - 1) / formatInfo.BlockWidth;
                int blockHeight = (height + formatInfo.BlockHeight - 1) / formatInfo.BlockHeight;
                return UntileBlocks(tiledData, blockWidth, blockHeight, formatInfo.BlockSize);
            }
            else
            {
                return UntilePixels(tiledData, width, height, formatInfo.BlockSize);
            }
        }

        private static byte[] UntileBlocks(byte[] tiledData, int blockWidth, int blockHeight, int blockSize)
        {
            byte[] output = new byte[blockWidth * blockHeight * blockSize];

            // Xbox 360 uses 32x32 pixel tiles (8x8 blocks for DXT)
            const int tileBlockW = 8;
            const int tileBlockH = 8;

            int tilesWide = (blockWidth + tileBlockW - 1) / tileBlockW;
            int tilesHigh = (blockHeight + tileBlockH - 1) / tileBlockH;

            int srcOffset = 0;
            for (int ty = 0; ty < tilesHigh; ty++)
            {
                for (int tx = 0; tx < tilesWide; tx++)
                {
                    for (int by = 0; by < tileBlockH; by++)
                    {
                        int destBlockY = ty * tileBlockH + by;
                        if (destBlockY >= blockHeight) { srcOffset += tileBlockW * blockSize; continue; }

                        for (int bx = 0; bx < tileBlockW; bx++)
                        {
                            int destBlockX = tx * tileBlockW + bx;
                            if (destBlockX >= blockWidth) { srcOffset += blockSize; continue; }

                            // Apply Morton/Z-order swizzle within the tile
                            int swizzledIndex = MortonIndex(bx, by, tileBlockW, tileBlockH);
                            int swizzledOffset = (ty * tilesWide + tx) * tileBlockW * tileBlockH * blockSize
                                               + swizzledIndex * blockSize;

                            int destOffset = (destBlockY * blockWidth + destBlockX) * blockSize;

                            if (swizzledOffset + blockSize <= tiledData.Length &&
                                destOffset + blockSize <= output.Length)
                            {
                                Buffer.BlockCopy(tiledData, swizzledOffset, output, destOffset, blockSize);
                            }

                            srcOffset += blockSize;
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] UntilePixels(byte[] tiledData, int width, int height, int bytesPerPixel)
        {
            byte[] output = new byte[width * height * bytesPerPixel];

            // Xbox 360 uses 32x32 pixel macro tiles
            const int tileW = 32;
            const int tileH = 32;

            int tilesWide = (width + tileW - 1) / tileW;
            int tilesHigh = (height + tileH - 1) / tileH;

            for (int ty = 0; ty < tilesHigh; ty++)
            {
                for (int tx = 0; tx < tilesWide; tx++)
                {
                    for (int py = 0; py < tileH; py++)
                    {
                        int destY = ty * tileH + py;
                        if (destY >= height) continue;

                        for (int px = 0; px < tileW; px++)
                        {
                            int destX = tx * tileW + px;
                            if (destX >= width) continue;

                            int swizzledIndex = MortonIndex(px, py, tileW, tileH);
                            int srcOffset = ((ty * tilesWide + tx) * tileW * tileH + swizzledIndex) * bytesPerPixel;
                            int destOffset = (destY * width + destX) * bytesPerPixel;

                            if (srcOffset + bytesPerPixel <= tiledData.Length &&
                                destOffset + bytesPerPixel <= output.Length)
                            {
                                Buffer.BlockCopy(tiledData, srcOffset, output, destOffset, bytesPerPixel);
                            }
                        }
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// Compute Morton/Z-order curve index for coordinate (x, y) within a tile.
        /// This interleaves the bits of x and y to produce the swizzled address.
        /// </summary>
        private static int MortonIndex(int x, int y, int tileWidth, int tileHeight)
        {
            int index = 0;
            int bit = 1;

            int maxCoord = Math.Max(tileWidth, tileHeight);
            while (bit < maxCoord)
            {
                if ((x & bit) != 0) index |= bit * 2;
                if ((y & bit) != 0) index |= bit;
                bit <<= 1;
            }

            // Proper bit interleaving for Morton code
            index = 0;
            for (int i = 0; i < 16; i++)
            {
                index |= ((x >> i) & 1) << (2 * i + 1);
                index |= ((y >> i) & 1) << (2 * i);
            }

            return index;
        }

        /// <summary>
        /// Decode texture data from its compressed/raw format to RGBA8888.
        /// </summary>
        public static byte[] DecodeToRGBA(byte[] data, int width, int height, TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1: return DecodeDXT1(data, width, height);
                case TextureFormat.DXT3: return DecodeDXT3(data, width, height);
                case TextureFormat.DXT5: return DecodeDXT5(data, width, height);
                case TextureFormat.A8R8G8B8: return DecodeA8R8G8B8(data, width, height);
                case TextureFormat.X8R8G8B8: return DecodeX8R8G8B8(data, width, height);
                case TextureFormat.R5G6B5: return DecodeR5G6B5(data, width, height);
                case TextureFormat.A8: return DecodeA8(data, width, height);
                case TextureFormat.L8: return DecodeL8(data, width, height);
                default: return DecodeA8R8G8B8(data, width, height);
            }
        }

        private static byte[] DecodeDXT1(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            int offset = 0;

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    if (offset + 8 > data.Length) break;

                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint indices = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    byte[] colors = new byte[16]; // 4 colors x RGBA
                    DecodeRGB565(c0, out colors[0], out colors[1], out colors[2]); colors[3] = 255;
                    DecodeRGB565(c1, out colors[4], out colors[5], out colors[6]); colors[7] = 255;

                    if (c0 > c1)
                    {
                        colors[8] = (byte)((2 * colors[0] + colors[4]) / 3);
                        colors[9] = (byte)((2 * colors[1] + colors[5]) / 3);
                        colors[10] = (byte)((2 * colors[2] + colors[6]) / 3);
                        colors[11] = 255;
                        colors[12] = (byte)((colors[0] + 2 * colors[4]) / 3);
                        colors[13] = (byte)((colors[1] + 2 * colors[5]) / 3);
                        colors[14] = (byte)((colors[2] + 2 * colors[6]) / 3);
                        colors[15] = 255;
                    }
                    else
                    {
                        colors[8] = (byte)((colors[0] + colors[4]) / 2);
                        colors[9] = (byte)((colors[1] + colors[5]) / 2);
                        colors[10] = (byte)((colors[2] + colors[6]) / 2);
                        colors[11] = 255;
                        colors[12] = 0; colors[13] = 0; colors[14] = 0; colors[15] = 0;
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            int y = by * 4 + py;
                            if (x >= width || y >= height) continue;

                            int idx = (int)((indices >> (2 * (py * 4 + px))) & 3);
                            int destOffset = (y * width + x) * 4;
                            output[destOffset] = colors[idx * 4];
                            output[destOffset + 1] = colors[idx * 4 + 1];
                            output[destOffset + 2] = colors[idx * 4 + 2];
                            output[destOffset + 3] = colors[idx * 4 + 3];
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeDXT3(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            int offset = 0;

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    if (offset + 16 > data.Length) break;

                    // Read alpha values (4 bits per pixel)
                    ulong alphaData = 0;
                    for (int i = 0; i < 8; i++)
                        alphaData |= (ulong)data[offset + i] << (i * 8);
                    offset += 8;

                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint indices = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    byte[] colors = new byte[12];
                    DecodeRGB565(c0, out colors[0], out colors[1], out colors[2]);
                    DecodeRGB565(c1, out colors[3], out colors[4], out colors[5]);
                    colors[6] = (byte)((2 * colors[0] + colors[3]) / 3);
                    colors[7] = (byte)((2 * colors[1] + colors[4]) / 3);
                    colors[8] = (byte)((2 * colors[2] + colors[5]) / 3);
                    colors[9] = (byte)((colors[0] + 2 * colors[3]) / 3);
                    colors[10] = (byte)((colors[1] + 2 * colors[4]) / 3);
                    colors[11] = (byte)((colors[2] + 2 * colors[5]) / 3);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            int y = by * 4 + py;
                            if (x >= width || y >= height) continue;

                            int idx = (int)((indices >> (2 * (py * 4 + px))) & 3);
                            int alphaIndex = py * 4 + px;
                            byte alpha = (byte)(((alphaData >> (alphaIndex * 4)) & 0xF) * 17);

                            int destOffset = (y * width + x) * 4;
                            output[destOffset] = colors[idx * 3];
                            output[destOffset + 1] = colors[idx * 3 + 1];
                            output[destOffset + 2] = colors[idx * 3 + 2];
                            output[destOffset + 3] = alpha;
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeDXT5(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            int offset = 0;

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    if (offset + 16 > data.Length) break;

                    // Alpha block
                    byte alpha0 = data[offset];
                    byte alpha1 = data[offset + 1];
                    ulong alphaBits = 0;
                    for (int i = 2; i < 8; i++)
                        alphaBits |= (ulong)data[offset + i] << ((i - 2) * 8);
                    offset += 8;

                    byte[] alphaTable = new byte[8];
                    alphaTable[0] = alpha0;
                    alphaTable[1] = alpha1;
                    if (alpha0 > alpha1)
                    {
                        for (int i = 1; i < 7; i++)
                            alphaTable[i + 1] = (byte)(((7 - i) * alpha0 + i * alpha1) / 7);
                    }
                    else
                    {
                        for (int i = 1; i < 5; i++)
                            alphaTable[i + 1] = (byte)(((5 - i) * alpha0 + i * alpha1) / 5);
                        alphaTable[6] = 0;
                        alphaTable[7] = 255;
                    }

                    // Color block
                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint indices = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    byte[] colors = new byte[12];
                    DecodeRGB565(c0, out colors[0], out colors[1], out colors[2]);
                    DecodeRGB565(c1, out colors[3], out colors[4], out colors[5]);
                    colors[6] = (byte)((2 * colors[0] + colors[3]) / 3);
                    colors[7] = (byte)((2 * colors[1] + colors[4]) / 3);
                    colors[8] = (byte)((2 * colors[2] + colors[5]) / 3);
                    colors[9] = (byte)((colors[0] + 2 * colors[3]) / 3);
                    colors[10] = (byte)((colors[1] + 2 * colors[4]) / 3);
                    colors[11] = (byte)((colors[2] + 2 * colors[5]) / 3);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            int y = by * 4 + py;
                            if (x >= width || y >= height) continue;

                            int idx = (int)((indices >> (2 * (py * 4 + px))) & 3);
                            int alphaIdx = (int)((alphaBits >> (3 * (py * 4 + px))) & 7);

                            int destOffset = (y * width + x) * 4;
                            output[destOffset] = colors[idx * 3];
                            output[destOffset + 1] = colors[idx * 3 + 1];
                            output[destOffset + 2] = colors[idx * 3 + 2];
                            output[destOffset + 3] = alphaTable[alphaIdx];
                        }
                    }
                }
            }

            return output;
        }

        private static byte[] DecodeA8R8G8B8(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height && i * 4 + 3 < data.Length; i++)
            {
                // ARGB -> RGBA
                output[i * 4] = data[i * 4 + 2];     // R
                output[i * 4 + 1] = data[i * 4 + 1]; // G
                output[i * 4 + 2] = data[i * 4 + 0]; // B
                output[i * 4 + 3] = data[i * 4 + 3]; // A
            }
            return output;
        }

        private static byte[] DecodeX8R8G8B8(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height && i * 4 + 3 < data.Length; i++)
            {
                output[i * 4] = data[i * 4 + 2];
                output[i * 4 + 1] = data[i * 4 + 1];
                output[i * 4 + 2] = data[i * 4 + 0];
                output[i * 4 + 3] = 255;
            }
            return output;
        }

        private static byte[] DecodeR5G6B5(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height && i * 2 + 1 < data.Length; i++)
            {
                ushort pixel = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
                DecodeRGB565(pixel, out output[i * 4], out output[i * 4 + 1], out output[i * 4 + 2]);
                output[i * 4 + 3] = 255;
            }
            return output;
        }

        private static byte[] DecodeA8(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height && i < data.Length; i++)
            {
                output[i * 4] = 255;
                output[i * 4 + 1] = 255;
                output[i * 4 + 2] = 255;
                output[i * 4 + 3] = data[i];
            }
            return output;
        }

        private static byte[] DecodeL8(byte[] data, int width, int height)
        {
            byte[] output = new byte[width * height * 4];
            for (int i = 0; i < width * height && i < data.Length; i++)
            {
                output[i * 4] = data[i];
                output[i * 4 + 1] = data[i];
                output[i * 4 + 2] = data[i];
                output[i * 4 + 3] = 255;
            }
            return output;
        }

        private static void DecodeRGB565(ushort value, out byte r, out byte g, out byte b)
        {
            r = (byte)(((value >> 11) & 0x1F) * 255 / 31);
            g = (byte)(((value >> 5) & 0x3F) * 255 / 63);
            b = (byte)((value & 0x1F) * 255 / 31);
        }
    }
}
