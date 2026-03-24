using System;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.IdTech
{
    /// <summary>
    /// Exports id Tech engine asset data to standard reimportable formats.
    /// Handles Quake WAD mip textures->TGA, MD3 models->OBJ, BSP lightmaps->TGA,
    /// and Doom graphics (flats/sprites)->TGA.
    /// </summary>
    public static class IdTechAssetExporter
    {
        #region Quake Palette

        /// <summary>
        /// Standard Quake 256-color palette (768 bytes, RGB triplets).
        /// </summary>
        private static readonly byte[] QuakePalette =
        {
            0,0,0, 15,15,15, 31,31,31, 47,47,47, 63,63,63, 75,75,75, 91,91,91, 107,107,107,
            123,123,123, 139,139,139, 155,155,155, 171,171,171, 187,187,187, 203,203,203, 219,219,219, 235,235,235,
            15,11,7, 23,15,11, 31,23,11, 39,27,15, 47,35,19, 55,43,23, 63,47,23, 75,55,27,
            83,59,27, 91,67,31, 99,75,31, 107,83,31, 115,87,31, 123,95,35, 131,103,35, 143,111,35,
            11,11,15, 19,19,27, 27,27,39, 39,39,51, 47,47,63, 55,55,75, 63,63,87, 71,71,103,
            79,79,115, 91,91,127, 99,99,139, 107,107,151, 115,115,163, 123,123,175, 131,131,187, 139,139,203,
            0,0,0, 7,7,0, 11,11,0, 19,19,0, 27,27,0, 35,35,0, 43,43,7, 47,47,7,
            55,55,7, 63,63,7, 71,71,7, 75,75,11, 83,83,11, 91,91,11, 99,99,11, 107,107,15,
            7,0,0, 15,0,0, 23,0,0, 31,0,0, 39,0,0, 47,0,0, 55,0,0, 63,0,0,
            71,0,0, 79,0,0, 87,0,0, 95,0,0, 103,0,0, 111,0,0, 119,0,0, 127,0,0,
            19,19,0, 27,27,0, 35,35,0, 47,43,0, 55,47,0, 67,55,0, 75,59,7, 87,67,7,
            95,71,7, 107,75,11, 119,83,15, 131,87,19, 139,91,19, 151,95,27, 163,99,31, 175,103,35,
            35,19,7, 47,23,11, 59,31,15, 75,35,19, 87,43,23, 99,47,31, 115,55,35, 127,59,43,
            143,67,51, 159,79,51, 175,99,47, 191,119,47, 207,143,43, 223,171,39, 239,203,31, 255,243,27,
            11,7,0, 27,19,0, 43,35,15, 55,43,19, 71,51,27, 83,55,35, 99,63,43, 111,71,51,
            127,83,63, 139,95,71, 155,107,83, 167,123,95, 175,135,107, 187,147,123, 199,163,135, 211,175,147,
            43,0,0, 59,0,0, 75,7,0, 95,7,0, 111,15,0, 127,23,7, 147,31,7, 163,39,11,
            183,51,15, 195,75,27, 207,99,43, 219,127,59, 227,151,79, 231,171,95, 235,187,111, 239,203,127,
            55,43,23, 55,35,15, 47,27,7, 39,19,0, 35,15,0, 27,11,0, 19,7,0, 11,7,0,
            0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0, 0,0,0,
            39,39,39, 59,59,59, 79,75,75, 99,91,91, 119,107,107, 139,123,123, 159,139,139, 179,155,155,
            199,175,175, 219,191,191, 239,211,211, 27,27,27, 35,35,35, 43,43,43, 51,47,47, 63,59,59,
            75,55,27, 83,59,27, 91,63,31, 99,67,31, 107,71,35, 115,75,35, 123,79,39, 131,83,39,
            7,7,7, 11,11,11, 15,15,15, 19,19,19, 23,23,23, 27,27,27, 31,31,31, 35,35,35,
            39,39,39, 43,43,43, 47,47,47, 51,51,51, 55,55,55, 59,59,59, 63,63,63, 67,67,67,
            71,71,71, 75,75,75, 79,79,79, 83,83,83, 87,87,87, 91,91,91, 95,95,95, 99,99,99,
            103,103,103, 107,107,107, 111,111,111, 115,115,115, 119,119,119, 123,123,123, 127,127,127, 131,131,131,
            135,135,135, 139,139,139, 143,143,143, 147,147,147, 151,151,151, 155,155,155, 159,159,159, 163,163,163,
            167,167,167, 171,171,171, 175,175,175, 179,179,179, 183,183,183, 187,187,187, 191,191,191, 195,195,195,
            199,199,199, 203,203,203, 207,207,207, 211,211,211, 215,215,215, 219,219,219, 223,223,223, 227,227,227,
            231,231,231, 235,235,235, 239,239,239, 243,243,243, 247,247,247, 251,251,251, 255,255,255, 163,115,91,
            171,123,99, 179,131,107, 187,139,115, 195,147,123, 203,155,131, 207,163,139, 211,171,147
        };

        #endregion

        #region Mip Texture Export (Quake WAD)

        /// <summary>
        /// Export a Quake WAD mip texture to an uncompressed 24-bit TGA file.
        /// </summary>
        public static ExportResult ExportMipTexture(byte[] data, string outputPath)
        {
            try
            {
                if (data == null || data.Length < 40)
                    return ExportResult.Failed("Mip texture data is too short.");

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Parse mip texture header
                // char name[16], uint32 width, uint32 height, uint32 offsets[4]
                int width = BitConverter.ToInt32(data, 16);
                int height = BitConverter.ToInt32(data, 20);
                int mip0Offset = BitConverter.ToInt32(data, 24);

                if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
                    return ExportResult.Failed($"Invalid mip texture dimensions: {width}x{height}");

                int pixelCount = width * height;
                if (mip0Offset + pixelCount > data.Length)
                    return ExportResult.Failed("Mip texture pixel data extends beyond buffer.");

                // Convert paletted pixels to 24-bit BGR for TGA
                byte[] bgrPixels = new byte[pixelCount * 3];
                for (int i = 0; i < pixelCount; i++)
                {
                    byte index = data[mip0Offset + i];
                    int palIdx = index * 3;
                    if (palIdx + 2 < QuakePalette.Length)
                    {
                        bgrPixels[i * 3 + 0] = QuakePalette[palIdx + 2]; // B
                        bgrPixels[i * 3 + 1] = QuakePalette[palIdx + 1]; // G
                        bgrPixels[i * 3 + 2] = QuakePalette[palIdx + 0]; // R
                    }
                }

                long bytesWritten = WriteTga24(outputPath, width, height, bgrPixels);
                return ExportResult.Succeeded(outputPath, bytesWritten);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"MipTexture export failed: {ex.Message}");
            }
        }

        #endregion

        #region MD3 Export (id Tech 3)

        /// <summary>
        /// Export an id Tech 3 MD3 model to Wavefront OBJ (first frame).
        /// </summary>
        public static ExportResult ExportMD3(byte[] data, string outputPath)
        {
            try
            {
                if (data == null || data.Length < 108)
                    return ExportResult.Failed("MD3 data is too short.");

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Validate magic and version
                string magic = Encoding.ASCII.GetString(data, 0, 4);
                if (magic != "IDP3")
                    return ExportResult.Failed($"Invalid MD3 magic: {magic}");

                int version = BitConverter.ToInt32(data, 4);
                if (version != 15)
                    return ExportResult.Failed($"Unsupported MD3 version: {version}");

                // MD3 header fields
                int numSurfaces = BitConverter.ToInt32(data, 76);
                int surfacesOffset = BitConverter.ToInt32(data, 104);

                if (numSurfaces <= 0 || numSurfaces > 1024)
                    return ExportResult.Failed($"Invalid MD3 surface count: {numSurfaces}");

                var sb = new StringBuilder();
                sb.AppendLine("# MD3 model exported by JackTheRipper360");
                sb.AppendLine("# id Tech 3 format");
                sb.AppendLine();

                int globalVertexOffset = 1; // OBJ indices are 1-based
                int surfOffset = surfacesOffset;

                for (int s = 0; s < numSurfaces; s++)
                {
                    if (surfOffset + 108 > data.Length)
                        break;

                    // Surface header
                    string surfMagic = Encoding.ASCII.GetString(data, surfOffset, 4);
                    if (surfMagic != "IDP3")
                        return ExportResult.Failed($"Invalid MD3 surface magic at offset {surfOffset}");

                    string surfName = ReadNullTermString(data, surfOffset + 4, 64);
                    int numFrames = BitConverter.ToInt32(data, surfOffset + 72);
                    int numShaders = BitConverter.ToInt32(data, surfOffset + 76);
                    int numVerts = BitConverter.ToInt32(data, surfOffset + 80);
                    int numTriangles = BitConverter.ToInt32(data, surfOffset + 84);
                    int ofsTriangles = BitConverter.ToInt32(data, surfOffset + 88);
                    int ofsShaders = BitConverter.ToInt32(data, surfOffset + 92);
                    int ofsST = BitConverter.ToInt32(data, surfOffset + 96);
                    int ofsVerts = BitConverter.ToInt32(data, surfOffset + 100);
                    int ofsEnd = BitConverter.ToInt32(data, surfOffset + 104);

                    sb.AppendLine($"g {surfName}");

                    // Vertices (first frame): int16 x, y, z, int16 normal (packed)
                    // Each vertex is 8 bytes
                    int vertDataOffset = surfOffset + ofsVerts;
                    for (int v = 0; v < numVerts; v++)
                    {
                        int vo = vertDataOffset + v * 8;
                        if (vo + 8 > data.Length) break;

                        float x = BitConverter.ToInt16(data, vo) / 64.0f;
                        float y = BitConverter.ToInt16(data, vo + 2) / 64.0f;
                        float z = BitConverter.ToInt16(data, vo + 4) / 64.0f;
                        sb.AppendLine($"v {x:F6} {y:F6} {z:F6}");
                    }

                    // Decode normals from packed int16
                    for (int v = 0; v < numVerts; v++)
                    {
                        int vo = vertDataOffset + v * 8;
                        if (vo + 8 > data.Length) break;

                        ushort encodedNormal = BitConverter.ToUInt16(data, vo + 6);
                        float lat = ((encodedNormal >> 8) & 0xFF) * (2.0f * (float)Math.PI / 255.0f);
                        float lng = (encodedNormal & 0xFF) * (2.0f * (float)Math.PI / 255.0f);
                        float nx = (float)(Math.Cos(lat) * Math.Sin(lng));
                        float ny = (float)(Math.Sin(lat) * Math.Sin(lng));
                        float nz = (float)Math.Cos(lng);
                        sb.AppendLine($"vn {nx:F6} {ny:F6} {nz:F6}");
                    }

                    // Texture coordinates: float s, float t (8 bytes each)
                    int stDataOffset = surfOffset + ofsST;
                    for (int v = 0; v < numVerts; v++)
                    {
                        int sto = stDataOffset + v * 8;
                        if (sto + 8 > data.Length) break;

                        float u = BitConverter.ToSingle(data, sto);
                        float t = BitConverter.ToSingle(data, sto + 4);
                        sb.AppendLine($"vt {u:F6} {1.0f - t:F6}");
                    }

                    // Triangles: 3x int32 indices (12 bytes each)
                    int triDataOffset = surfOffset + ofsTriangles;
                    for (int t = 0; t < numTriangles; t++)
                    {
                        int to = triDataOffset + t * 12;
                        if (to + 12 > data.Length) break;

                        int i0 = BitConverter.ToInt32(data, to) + globalVertexOffset;
                        int i1 = BitConverter.ToInt32(data, to + 4) + globalVertexOffset;
                        int i2 = BitConverter.ToInt32(data, to + 8) + globalVertexOffset;
                        sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
                    }

                    sb.AppendLine();
                    globalVertexOffset += numVerts;
                    surfOffset += ofsEnd;
                }

                string objText = sb.ToString();
                File.WriteAllText(outputPath, objText, Encoding.ASCII);
                long bytesWritten = new FileInfo(outputPath).Length;
                return ExportResult.Succeeded(outputPath, bytesWritten);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"MD3 export failed: {ex.Message}");
            }
        }

        #endregion

        #region Lightmap Export (BSP)

        /// <summary>
        /// Export a 128x128 RGB lightmap from a BSP to an uncompressed 24-bit TGA.
        /// </summary>
        public static ExportResult ExportLightmap(byte[] rgbData, string outputPath)
        {
            try
            {
                const int LightmapWidth = 128;
                const int LightmapHeight = 128;
                const int ExpectedSize = LightmapWidth * LightmapHeight * 3;

                if (rgbData == null || rgbData.Length < ExpectedSize)
                    return ExportResult.Failed($"Lightmap data too short. Expected {ExpectedSize} bytes, got {rgbData?.Length ?? 0}.");

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Convert RGB to BGR for TGA
                byte[] bgrPixels = new byte[ExpectedSize];
                for (int i = 0; i < LightmapWidth * LightmapHeight; i++)
                {
                    bgrPixels[i * 3 + 0] = rgbData[i * 3 + 2]; // B
                    bgrPixels[i * 3 + 1] = rgbData[i * 3 + 1]; // G
                    bgrPixels[i * 3 + 2] = rgbData[i * 3 + 0]; // R
                }

                long bytesWritten = WriteTga24(outputPath, LightmapWidth, LightmapHeight, bgrPixels);
                return ExportResult.Succeeded(outputPath, bytesWritten);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Lightmap export failed: {ex.Message}");
            }
        }

        #endregion

        #region Doom Graphic Export

        /// <summary>
        /// Export a Doom flat (64x64 raw paletted graphic) to a 24-bit TGA.
        /// </summary>
        public static ExportResult ExportDoomFlat(byte[] data, byte[] palette, string outputPath)
        {
            try
            {
                const int FlatWidth = 64;
                const int FlatHeight = 64;
                const int FlatSize = FlatWidth * FlatHeight;

                if (data == null || data.Length < FlatSize)
                    return ExportResult.Failed($"Doom flat data too short. Expected {FlatSize} bytes, got {data?.Length ?? 0}.");

                if (palette == null || palette.Length < 768)
                    return ExportResult.Failed("Doom palette must be 768 bytes (256 RGB triplets).");

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                byte[] bgrPixels = new byte[FlatSize * 3];
                for (int i = 0; i < FlatSize; i++)
                {
                    int palIdx = data[i] * 3;
                    bgrPixels[i * 3 + 0] = palette[palIdx + 2]; // B
                    bgrPixels[i * 3 + 1] = palette[palIdx + 1]; // G
                    bgrPixels[i * 3 + 2] = palette[palIdx + 0]; // R
                }

                long bytesWritten = WriteTga24(outputPath, FlatWidth, FlatHeight, bgrPixels);
                return ExportResult.Succeeded(outputPath, bytesWritten);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Doom flat export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Export a Doom sprite (column-based graphic) to a 32-bit TGA with transparency.
        /// </summary>
        public static ExportResult ExportDoomSprite(byte[] data, byte[] palette, string outputPath)
        {
            try
            {
                if (data == null || data.Length < 8)
                    return ExportResult.Failed("Doom sprite data is too short.");

                if (palette == null || palette.Length < 768)
                    return ExportResult.Failed("Doom palette must be 768 bytes (256 RGB triplets).");

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Parse sprite header
                int width = BitConverter.ToInt16(data, 0);
                int height = BitConverter.ToInt16(data, 2);
                int leftOffset = BitConverter.ToInt16(data, 4);
                int topOffset = BitConverter.ToInt16(data, 6);

                if (width <= 0 || width > 4096 || height <= 0 || height > 4096)
                    return ExportResult.Failed($"Invalid sprite dimensions: {width}x{height}");

                if (8 + width * 4 > data.Length)
                    return ExportResult.Failed("Sprite column offset table extends beyond data.");

                // Read column offsets
                int[] columnOffsets = new int[width];
                for (int c = 0; c < width; c++)
                    columnOffsets[c] = BitConverter.ToInt32(data, 8 + c * 4);

                // BGRA pixel buffer (initialized to transparent)
                byte[] bgraPixels = new byte[width * height * 4];

                // Process each column
                for (int col = 0; col < width; col++)
                {
                    int offset = columnOffsets[col];
                    if (offset < 0 || offset >= data.Length)
                        continue;

                    while (offset < data.Length)
                    {
                        byte rowStart = data[offset];
                        if (rowStart == 0xFF)
                            break; // End of column

                        offset++;
                        if (offset >= data.Length) break;

                        byte postLength = data[offset];
                        offset++; // skip unused padding byte

                        if (offset >= data.Length) break;
                        offset++; // actual padding byte

                        for (int row = 0; row < postLength; row++)
                        {
                            if (offset >= data.Length) break;

                            int y = rowStart + row;
                            if (y >= 0 && y < height)
                            {
                                byte palIndex = data[offset];
                                int pixelOffset = (y * width + col) * 4;
                                int palOfs = palIndex * 3;
                                bgraPixels[pixelOffset + 0] = palette[palOfs + 2]; // B
                                bgraPixels[pixelOffset + 1] = palette[palOfs + 1]; // G
                                bgraPixels[pixelOffset + 2] = palette[palOfs + 0]; // R
                                bgraPixels[pixelOffset + 3] = 255;                 // A
                            }
                            offset++;
                        }

                        if (offset < data.Length)
                            offset++; // skip trailing padding byte
                    }
                }

                long bytesWritten = WriteTga32(outputPath, width, height, bgraPixels);
                return ExportResult.Succeeded(outputPath, bytesWritten);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Doom sprite export failed: {ex.Message}");
            }
        }

        #endregion

        #region TGA Helpers

        /// <summary>
        /// Write an uncompressed 24-bit TGA (type 2) file.
        /// Pixel data must be in BGR order, top-to-bottom row order.
        /// </summary>
        private static long WriteTga24(string path, int width, int height, byte[] bgrPixels)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // TGA header (18 bytes)
                bw.Write((byte)0);   // ID length
                bw.Write((byte)0);   // Color map type
                bw.Write((byte)2);   // Image type: uncompressed true-color
                bw.Write((short)0);  // Color map origin
                bw.Write((short)0);  // Color map length
                bw.Write((byte)0);   // Color map depth
                bw.Write((short)0);  // X origin
                bw.Write((short)0);  // Y origin
                bw.Write((short)width);
                bw.Write((short)height);
                bw.Write((byte)24);  // Bits per pixel
                bw.Write((byte)0x20); // Image descriptor: top-left origin

                bw.Write(bgrPixels, 0, width * height * 3);
                return fs.Length;
            }
        }

        /// <summary>
        /// Write an uncompressed 32-bit TGA (type 2) file with alpha channel.
        /// Pixel data must be in BGRA order, top-to-bottom row order.
        /// </summary>
        private static long WriteTga32(string path, int width, int height, byte[] bgraPixels)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // TGA header (18 bytes)
                bw.Write((byte)0);   // ID length
                bw.Write((byte)0);   // Color map type
                bw.Write((byte)2);   // Image type: uncompressed true-color
                bw.Write((short)0);  // Color map origin
                bw.Write((short)0);  // Color map length
                bw.Write((byte)0);   // Color map depth
                bw.Write((short)0);  // X origin
                bw.Write((short)0);  // Y origin
                bw.Write((short)width);
                bw.Write((short)height);
                bw.Write((byte)32);  // Bits per pixel
                bw.Write((byte)0x28); // Image descriptor: top-left origin + 8 alpha bits

                bw.Write(bgraPixels, 0, width * height * 4);
                return fs.Length;
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Read a null-terminated ASCII string from a byte buffer.
        /// </summary>
        private static string ReadNullTermString(byte[] data, int offset, int maxLength)
        {
            int end = offset;
            int limit = Math.Min(offset + maxLength, data.Length);
            while (end < limit && data[end] != 0)
                end++;
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        #endregion
    }
}
