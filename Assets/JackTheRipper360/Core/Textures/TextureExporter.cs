using System;
using System.Collections.Generic;
using System.IO;

namespace JackTheRipper360.Core.Textures
{
    /// <summary>
    /// Pure C# PNG encoder for exporting texture data.
    /// Produces valid PNG files without any external dependencies.
    /// </summary>
    public static class TextureExporter
    {
        /// <summary>
        /// Encode RGBA pixel data as a PNG file.
        /// </summary>
        public static byte[] EncodePNG(byte[] rgbaData, int width, int height)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // PNG signature
                writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

                // IHDR chunk
                WriteChunk(writer, "IHDR", w =>
                {
                    WriteBigEndianUInt32(w, (uint)width);
                    WriteBigEndianUInt32(w, (uint)height);
                    w.Write((byte)8);  // bit depth
                    w.Write((byte)6);  // color type: RGBA
                    w.Write((byte)0);  // compression
                    w.Write((byte)0);  // filter
                    w.Write((byte)0);  // interlace
                });

                // IDAT chunk(s) - image data
                byte[] rawData = PrepareImageData(rgbaData, width, height);
                byte[] compressedData = DeflateCompress(rawData);
                WriteChunk(writer, "IDAT", w => w.Write(compressedData));

                // IEND chunk
                WriteChunk(writer, "IEND", w => { });

                return ms.ToArray();
            }
        }

        private static byte[] PrepareImageData(byte[] rgbaData, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] result = new byte[height * (1 + rowBytes)];

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * rowBytes;
                int destRow = y * (1 + rowBytes);
                result[destRow] = 0; // filter: none
                Buffer.BlockCopy(rgbaData, srcRow, result, destRow + 1, Math.Min(rowBytes, rgbaData.Length - srcRow));
            }

            return result;
        }

        /// <summary>
        /// Simple DEFLATE compression using zlib format.
        /// Uses store-only blocks for simplicity (no actual compression, but valid format).
        /// </summary>
        private static byte[] DeflateCompress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                // zlib header
                ms.WriteByte(0x78); // CMF
                ms.WriteByte(0x01); // FLG

                // Write store blocks
                int offset = 0;
                while (offset < data.Length)
                {
                    int blockSize = Math.Min(65535, data.Length - offset);
                    bool isLast = (offset + blockSize >= data.Length);

                    ms.WriteByte(isLast ? (byte)0x01 : (byte)0x00); // BFINAL + BTYPE=00 (stored)
                    ms.WriteByte((byte)(blockSize & 0xFF));
                    ms.WriteByte((byte)((blockSize >> 8) & 0xFF));
                    ms.WriteByte((byte)(~blockSize & 0xFF));
                    ms.WriteByte((byte)((~blockSize >> 8) & 0xFF));
                    ms.Write(data, offset, blockSize);
                    offset += blockSize;
                }

                // Adler32 checksum
                uint adler = Adler32(data);
                ms.WriteByte((byte)((adler >> 24) & 0xFF));
                ms.WriteByte((byte)((adler >> 16) & 0xFF));
                ms.WriteByte((byte)((adler >> 8) & 0xFF));
                ms.WriteByte((byte)(adler & 0xFF));

                return ms.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(BinaryWriter writer, string type, Action<BinaryWriter> writeContent)
        {
            using (var contentMs = new MemoryStream())
            using (var contentWriter = new BinaryWriter(contentMs))
            {
                writeContent(contentWriter);
                contentWriter.Flush();
                byte[] content = contentMs.ToArray();

                byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

                WriteBigEndianUInt32(writer, (uint)content.Length);
                writer.Write(typeBytes);
                writer.Write(content);

                // CRC32 over type + content
                byte[] crcInput = new byte[4 + content.Length];
                Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
                Buffer.BlockCopy(content, 0, crcInput, 4, content.Length);
                uint crc = Crc32(crcInput);
                WriteBigEndianUInt32(writer, crc);
            }
        }

        private static void WriteBigEndianUInt32(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static uint[] _crcTable;

        private static uint Crc32(byte[] data)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint crc = i;
                    for (int j = 0; j < 8; j++)
                        crc = (crc & 1) != 0 ? (0xEDB88320 ^ (crc >> 1)) : (crc >> 1);
                    _crcTable[i] = crc;
                }
            }

            uint c = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
                c = _crcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }

        /// <summary>
        /// Export RGBA data as a TGA file (simpler format, no compression needed).
        /// </summary>
        public static byte[] EncodeTGA(byte[] rgbaData, int width, int height)
        {
            byte[] tga = new byte[18 + width * height * 4];

            // TGA header
            tga[2] = 2;   // Uncompressed true-color
            tga[12] = (byte)(width & 0xFF);
            tga[13] = (byte)((width >> 8) & 0xFF);
            tga[14] = (byte)(height & 0xFF);
            tga[15] = (byte)((height >> 8) & 0xFF);
            tga[16] = 32; // bits per pixel
            tga[17] = 0x28; // origin upper-left + 8 alpha bits

            // Convert RGBA to BGRA and flip vertically (TGA is bottom-up by default)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstIdx = 18 + (y * width + x) * 4;

                    tga[dstIdx + 0] = rgbaData[srcIdx + 2]; // B
                    tga[dstIdx + 1] = rgbaData[srcIdx + 1]; // G
                    tga[dstIdx + 2] = rgbaData[srcIdx + 0]; // R
                    tga[dstIdx + 3] = rgbaData[srcIdx + 3]; // A
                }
            }

            return tga;
        }
    }
}
