using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Source
{
    /// <summary>
    /// Parser for Valve Texture Format (VTF) files used by the Source engine.
    /// Supports VTF version 7.x headers, image format identification, and export
    /// to DDS (for compressed formats) or TGA (for uncompressed formats).
    /// </summary>
    public class SourceVtfParser : IAssetParser
    {
        private const int MinVtfFileSize = 64;
        private const uint VtfMagic = 0x00465456; // "VTF\0" as little-endian uint

        // Supported VTF image format identifiers
        private const int Format_RGBA8888 = 0;
        private const int Format_RGB888 = 2;
        private const int Format_BGR888 = 3;
        private const int Format_BGRA8888 = 12;
        private const int Format_DXT1 = 13;
        private const int Format_DXT3 = 14;
        private const int Format_DXT5 = 15;
        private const int Format_A8 = 33;

        // DDS constants
        private const uint DDS_MAGIC = 0x20534444; // "DDS "
        private const uint DDS_HEADER_SIZE = 124;
        private const uint DDSD_CAPS = 0x1;
        private const uint DDSD_HEIGHT = 0x2;
        private const uint DDSD_WIDTH = 0x4;
        private const uint DDSD_PIXELFORMAT = 0x1000;
        private const uint DDSD_MIPMAPCOUNT = 0x20000;
        private const uint DDSD_LINEARSIZE = 0x80000;
        private const uint DDPF_FOURCC = 0x4;
        private const uint DDSCAPS_TEXTURE = 0x1000;
        private const uint DDSCAPS_MIPMAP = 0x400008;

        public AssetType Type => AssetType.Texture;

        public bool CanParse(Stream stream, string fileName)
        {
            if (stream.Length < MinVtfFileSize)
                return false;

            string ext = Path.GetExtension(fileName);
            if (!string.Equals(ext, ".vtf", StringComparison.OrdinalIgnoreCase))
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            if (stream.Read(magic, 0, 4) < 4)
                return false;

            return magic[0] == 'V' && magic[1] == 'T' && magic[2] == 'F' && magic[3] == 0;
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                // Signature (4 bytes)
                string signature = reader.ReadString(4);
                if (signature != "VTF")
                    throw new InvalidDataException($"Invalid VTF signature: '{signature}'");

                // Version
                uint versionMajor = reader.ReadUInt32();
                uint versionMinor = reader.ReadUInt32();

                // Header size
                uint headerSize = reader.ReadUInt32();

                // Image dimensions
                ushort width = reader.ReadUInt16();
                ushort height = reader.ReadUInt16();

                // Flags
                uint flags = reader.ReadUInt32();

                // Frames
                ushort frames = reader.ReadUInt16();
                ushort firstFrame = reader.ReadUInt16();

                // Padding (4 bytes)
                reader.Skip(4);

                // Reflectivity vector (3 floats)
                float reflectX = reader.ReadSingle();
                float reflectY = reader.ReadSingle();
                float reflectZ = reader.ReadSingle();

                // Padding (4 bytes)
                reader.Skip(4);

                // Bumpmap scale
                float bumpmapScale = reader.ReadSingle();

                // High-res image format
                int highResImageFormat = reader.ReadInt32();

                // Mipmap count
                byte mipmapCount = reader.ReadByte();

                // Low-res image format and dimensions
                int lowResFormat = reader.ReadInt32();
                byte lowResWidth = reader.ReadByte();
                byte lowResHeight = reader.ReadByte();

                var entry = new AssetEntry(fileName, AssetType.Texture)
                {
                    SourcePath = fileName,
                    Offset = 0,
                    Size = stream.Length,
                    FormatName = $"VTF v{versionMajor}.{versionMinor} ({GetFormatName(highResImageFormat)})"
                };

                entry.Metadata["VersionMajor"] = (int)versionMajor;
                entry.Metadata["VersionMinor"] = (int)versionMinor;
                entry.Metadata["HeaderSize"] = (int)headerSize;
                entry.Metadata["Width"] = (int)width;
                entry.Metadata["Height"] = (int)height;
                entry.Metadata["Flags"] = (int)flags;
                entry.Metadata["Frames"] = (int)frames;
                entry.Metadata["FirstFrame"] = (int)firstFrame;
                entry.Metadata["ReflectivityX"] = reflectX;
                entry.Metadata["ReflectivityY"] = reflectY;
                entry.Metadata["ReflectivityZ"] = reflectZ;
                entry.Metadata["BumpmapScale"] = bumpmapScale;
                entry.Metadata["ImageFormat"] = highResImageFormat;
                entry.Metadata["ImageFormatName"] = GetFormatName(highResImageFormat);
                entry.Metadata["MipmapCount"] = (int)mipmapCount;
                entry.Metadata["LowResFormat"] = lowResFormat;
                entry.Metadata["LowResWidth"] = (int)lowResWidth;
                entry.Metadata["LowResHeight"] = (int)lowResHeight;
                entry.Metadata["IsCompressed"] = IsCompressedFormat(highResImageFormat);

                return entry;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            if (!entry.Metadata.ContainsKey("ImageFormat"))
                return ExportResult.Failed("Missing image format metadata. Re-parse the VTF file first.");

            int imageFormat = (int)entry.Metadata["ImageFormat"];
            int width = (int)entry.Metadata["Width"];
            int height = (int)entry.Metadata["Height"];
            int mipmapCount = (int)entry.Metadata["MipmapCount"];
            uint headerSize = (uint)(int)entry.Metadata["HeaderSize"];

            if (IsCompressedFormat(imageFormat))
            {
                return ExportAsDds(source, outputPath, imageFormat, width, height, mipmapCount, headerSize);
            }
            else
            {
                return ExportAsTga(source, outputPath, imageFormat, width, height, mipmapCount, headerSize);
            }
        }

        #region DDS Export

        /// <summary>
        /// Exports a VTF with a compressed (DXT) image format as a DDS file.
        /// VTF stores mipmaps smallest-first, so data must be read in reverse order.
        /// </summary>
        private ExportResult ExportAsDds(Stream source, string outputPath, int imageFormat,
            int width, int height, int mipmapCount, uint headerSize)
        {
            string ddsPath = Path.ChangeExtension(outputPath, ".dds");
            string dir = Path.GetDirectoryName(ddsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Calculate the data offset: skip VTF header and low-res image
            // The high-res image data starts after the header
            long dataOffset = headerSize;

            // Calculate total high-res image data size (all mipmaps, smallest-first in VTF)
            long totalDataSize = 0;
            for (int mip = mipmapCount - 1; mip >= 0; mip--)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                totalDataSize += CalculateImageSize(imageFormat, mipWidth, mipHeight);
            }

            // Read all mipmap data from VTF (smallest-first order)
            source.Seek(dataOffset, SeekOrigin.Begin);

            // Skip the low-res thumbnail image if present
            // Low-res is typically DXT1 and sits between header and high-res data
            // We need to skip past it to reach the high-res mips
            long highResStart = source.Length - totalDataSize;
            if (highResStart < dataOffset)
                highResStart = dataOffset;
            source.Seek(highResStart, SeekOrigin.Begin);

            // Read mips smallest-first from VTF, then write largest-first to DDS
            var mipBuffers = new byte[mipmapCount][];
            for (int mip = mipmapCount - 1; mip >= 0; mip--)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                int mipSize = CalculateImageSize(imageFormat, mipWidth, mipHeight);
                mipBuffers[mip] = new byte[mipSize];
                ReadFully(source, mipBuffers[mip], mipSize);
            }

            // Build DDS file
            using (var output = new FileStream(ddsPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(output))
            {
                // DDS magic
                writer.Write(DDS_MAGIC);

                // DDS header
                writer.Write(DDS_HEADER_SIZE);  // dwSize
                uint ddsFlags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE;
                if (mipmapCount > 1)
                    ddsFlags |= DDSD_MIPMAPCOUNT;
                writer.Write(ddsFlags);         // dwFlags
                writer.Write((uint)height);     // dwHeight
                writer.Write((uint)width);      // dwWidth
                writer.Write((uint)CalculateImageSize(imageFormat, width, height)); // dwPitchOrLinearSize
                writer.Write((uint)0);          // dwDepth
                writer.Write((uint)mipmapCount); // dwMipMapCount

                // dwReserved1[11]
                for (int i = 0; i < 11; i++)
                    writer.Write((uint)0);

                // DDS_PIXELFORMAT
                writer.Write((uint)32);         // dwSize
                writer.Write(DDPF_FOURCC);      // dwFlags
                writer.Write(GetDdsFourCc(imageFormat)); // dwFourCC
                writer.Write((uint)0);          // dwRGBBitCount
                writer.Write((uint)0);          // dwRBitMask
                writer.Write((uint)0);          // dwGBitMask
                writer.Write((uint)0);          // dwBBitMask
                writer.Write((uint)0);          // dwABitMask

                // dwCaps
                uint caps = DDSCAPS_TEXTURE;
                if (mipmapCount > 1)
                    caps |= DDSCAPS_MIPMAP;
                writer.Write(caps);             // dwCaps
                writer.Write((uint)0);          // dwCaps2
                writer.Write((uint)0);          // dwCaps3
                writer.Write((uint)0);          // dwCaps4
                writer.Write((uint)0);          // dwReserved2

                // Write mip data largest-first (DDS order)
                for (int mip = 0; mip < mipmapCount; mip++)
                {
                    writer.Write(mipBuffers[mip]);
                }

                return ExportResult.Succeeded(ddsPath, output.Length);
            }
        }

        #endregion

        #region TGA Export

        /// <summary>
        /// Exports a VTF with an uncompressed image format as a TGA file.
        /// Reads only the largest mipmap (mip level 0). VTF stores mipmaps smallest-first,
        /// so the largest mip is at the end of the image data.
        /// </summary>
        private ExportResult ExportAsTga(Stream source, string outputPath, int imageFormat,
            int width, int height, int mipmapCount, uint headerSize)
        {
            string tgaPath = Path.ChangeExtension(outputPath, ".tga");
            string dir = Path.GetDirectoryName(tgaPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            int mip0Size = CalculateImageSize(imageFormat, width, height);

            // The largest mip is last in VTF data (smallest-first ordering)
            long mip0Offset = source.Length - mip0Size;
            if (mip0Offset < headerSize)
                return ExportResult.Failed("VTF data is too small to contain the full-resolution image.");

            source.Seek(mip0Offset, SeekOrigin.Begin);
            byte[] pixelData = new byte[mip0Size];
            ReadFully(source, pixelData, mip0Size);

            // Convert to BGRA for TGA output
            byte[] bgra = ConvertToBgra(pixelData, imageFormat, width, height);
            if (bgra == null)
                return ExportResult.Failed($"Unsupported VTF image format for TGA export: {GetFormatName(imageFormat)}");

            using (var output = new FileStream(tgaPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(output))
            {
                // TGA header
                writer.Write((byte)0);          // ID length
                writer.Write((byte)0);          // Color map type
                writer.Write((byte)2);          // Image type (uncompressed true-color)
                writer.Write((short)0);         // Color map first entry
                writer.Write((short)0);         // Color map length
                writer.Write((byte)0);          // Color map entry size
                writer.Write((short)0);         // X origin
                writer.Write((short)0);         // Y origin
                writer.Write((short)width);     // Width
                writer.Write((short)height);    // Height
                writer.Write((byte)32);         // Bits per pixel
                writer.Write((byte)0x28);       // Image descriptor (top-left origin, 8 alpha bits)

                writer.Write(bgra);

                return ExportResult.Succeeded(tgaPath, output.Length);
            }
        }

        #endregion

        #region Image Format Helpers

        /// <summary>
        /// Returns a human-readable name for a VTF image format identifier.
        /// </summary>
        private static string GetFormatName(int format)
        {
            switch (format)
            {
                case Format_RGBA8888: return "RGBA8888";
                case Format_RGB888: return "RGB888";
                case Format_BGR888: return "BGR888";
                case Format_BGRA8888: return "BGRA8888";
                case Format_DXT1: return "DXT1";
                case Format_DXT3: return "DXT3";
                case Format_DXT5: return "DXT5";
                case Format_A8: return "A8";
                default: return $"Unknown ({format})";
            }
        }

        /// <summary>
        /// Returns true if the format is a block-compressed (DXT) format.
        /// </summary>
        private static bool IsCompressedFormat(int format)
        {
            return format == Format_DXT1 || format == Format_DXT3 || format == Format_DXT5;
        }

        /// <summary>
        /// Returns the number of bits per pixel for uncompressed formats, or 0 for compressed.
        /// </summary>
        private static int GetBitsPerPixel(int format)
        {
            switch (format)
            {
                case Format_RGBA8888: return 32;
                case Format_RGB888: return 24;
                case Format_BGR888: return 24;
                case Format_BGRA8888: return 32;
                case Format_A8: return 8;
                default: return 0;
            }
        }

        /// <summary>
        /// Calculates the byte size of image data for a given format and dimensions.
        /// Block-compressed formats use 4x4 blocks.
        /// </summary>
        private static int CalculateImageSize(int format, int width, int height)
        {
            switch (format)
            {
                case Format_DXT1:
                {
                    int blocksW = Math.Max(1, (width + 3) / 4);
                    int blocksH = Math.Max(1, (height + 3) / 4);
                    return blocksW * blocksH * 8;
                }
                case Format_DXT3:
                case Format_DXT5:
                {
                    int blocksW = Math.Max(1, (width + 3) / 4);
                    int blocksH = Math.Max(1, (height + 3) / 4);
                    return blocksW * blocksH * 16;
                }
                default:
                {
                    int bpp = GetBitsPerPixel(format);
                    if (bpp == 0)
                        return width * height * 4; // Fallback: assume 32bpp
                    return width * height * bpp / 8;
                }
            }
        }

        /// <summary>
        /// Returns the DDS FourCC code for a given VTF compressed format.
        /// </summary>
        private static uint GetDdsFourCc(int format)
        {
            switch (format)
            {
                case Format_DXT1: return 0x31545844; // "DXT1"
                case Format_DXT3: return 0x33545844; // "DXT3"
                case Format_DXT5: return 0x35545844; // "DXT5"
                default: return 0;
            }
        }

        /// <summary>
        /// Converts raw pixel data from a VTF image format to BGRA8888 for TGA export.
        /// </summary>
        private static byte[] ConvertToBgra(byte[] data, int format, int width, int height)
        {
            int pixelCount = width * height;
            byte[] bgra = new byte[pixelCount * 4];

            switch (format)
            {
                case Format_RGBA8888:
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int src = i * 4;
                        int dst = i * 4;
                        bgra[dst + 0] = data[src + 2]; // B
                        bgra[dst + 1] = data[src + 1]; // G
                        bgra[dst + 2] = data[src + 0]; // R
                        bgra[dst + 3] = data[src + 3]; // A
                    }
                    return bgra;

                case Format_RGB888:
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int src = i * 3;
                        int dst = i * 4;
                        bgra[dst + 0] = data[src + 2]; // B
                        bgra[dst + 1] = data[src + 1]; // G
                        bgra[dst + 2] = data[src + 0]; // R
                        bgra[dst + 3] = 0xFF;          // A
                    }
                    return bgra;

                case Format_BGR888:
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int src = i * 3;
                        int dst = i * 4;
                        bgra[dst + 0] = data[src + 0]; // B
                        bgra[dst + 1] = data[src + 1]; // G
                        bgra[dst + 2] = data[src + 2]; // R
                        bgra[dst + 3] = 0xFF;          // A
                    }
                    return bgra;

                case Format_BGRA8888:
                    // Already in BGRA order
                    if (data.Length >= bgra.Length)
                    {
                        Buffer.BlockCopy(data, 0, bgra, 0, bgra.Length);
                    }
                    return bgra;

                case Format_A8:
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int dst = i * 4;
                        bgra[dst + 0] = 0xFF; // B
                        bgra[dst + 1] = 0xFF; // G
                        bgra[dst + 2] = 0xFF; // R
                        bgra[dst + 3] = data[i]; // A
                    }
                    return bgra;

                default:
                    return null;
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Reads exactly the requested number of bytes from a stream.
        /// </summary>
        private static void ReadFully(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    break;
                offset += read;
            }
        }

        #endregion
    }
}
