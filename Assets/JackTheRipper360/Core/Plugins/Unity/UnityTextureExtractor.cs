using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unity
{
    /// <summary>
    /// Unity Texture2D texture format identifiers.
    /// Maps from Unity's internal TextureFormat enum values.
    /// </summary>
    public enum UnityTextureFormat
    {
        Unknown = 0,
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        RGB565 = 7,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        ETC_RGB4 = 24,
        ASTC_RGB_4x4 = 34,
        ASTC_RGBA_4x4 = 47,
        ETC2_RGB = 48,
        RG16 = 62,
        R8 = 63
    }

    /// <summary>
    /// Streaming info for textures whose pixel data lives in a separate .resS or .resource file.
    /// </summary>
    public sealed class StreamingInfo
    {
        public long Offset { get; set; }
        public uint Size { get; set; }
        public string Path { get; set; }

        public bool IsStreamed => Size > 0 && !string.IsNullOrEmpty(Path);
    }

    /// <summary>
    /// Parsed representation of a Unity Texture2D serialized object.
    /// </summary>
    public sealed class Texture2DData
    {
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public UnityTextureFormat TextureFormat { get; set; }
        public int UnityFormatId { get; set; }
        public int MipCount { get; set; }
        public bool ReadAllowed { get; set; }
        public int ImageDataSize { get; set; }
        public byte[] ImageData { get; set; }
        public StreamingInfo StreamingInfo { get; set; }

        public bool IsCompressed =>
            TextureFormat == UnityTextureFormat.DXT1 ||
            TextureFormat == UnityTextureFormat.DXT5 ||
            TextureFormat == UnityTextureFormat.ETC_RGB4 ||
            TextureFormat == UnityTextureFormat.ETC2_RGB ||
            TextureFormat == UnityTextureFormat.ASTC_RGB_4x4 ||
            TextureFormat == UnityTextureFormat.ASTC_RGBA_4x4;

        public bool IsDxtCompressed =>
            TextureFormat == UnityTextureFormat.DXT1 ||
            TextureFormat == UnityTextureFormat.DXT5;

        public int BitsPerPixel
        {
            get
            {
                switch (TextureFormat)
                {
                    case UnityTextureFormat.Alpha8:
                    case UnityTextureFormat.R8:
                        return 8;
                    case UnityTextureFormat.ARGB4444:
                    case UnityTextureFormat.RGB565:
                    case UnityTextureFormat.RGBA4444:
                    case UnityTextureFormat.RG16:
                        return 16;
                    case UnityTextureFormat.RGB24:
                        return 24;
                    case UnityTextureFormat.RGBA32:
                    case UnityTextureFormat.ARGB32:
                        return 32;
                    case UnityTextureFormat.DXT1:
                        return 4;
                    case UnityTextureFormat.DXT5:
                        return 8;
                    case UnityTextureFormat.ETC_RGB4:
                    case UnityTextureFormat.ETC2_RGB:
                        return 4;
                    case UnityTextureFormat.ASTC_RGB_4x4:
                    case UnityTextureFormat.ASTC_RGBA_4x4:
                        return 8;
                    default:
                        return 32;
                }
            }
        }
    }

    /// <summary>
    /// Specialized extractor for Unity Texture2D serialized objects.
    /// Parses the serialized binary layout, writes DDS files for compressed formats
    /// (DXT1/DXT5) and TGA files for uncompressed formats.
    /// </summary>
    public static class UnityTextureExtractor
    {
        // DDS constants
        private const uint DDS_MAGIC = 0x20534444; // "DDS "
        private const uint DDS_HEADER_SIZE = 124;
        private const uint DDPF_STRUCT_SIZE = 32;

        // DDS header flags
        private const uint DDSD_CAPS = 0x00000001;
        private const uint DDSD_HEIGHT = 0x00000002;
        private const uint DDSD_WIDTH = 0x00000004;
        private const uint DDSD_PITCH = 0x00000008;
        private const uint DDSD_PIXELFORMAT = 0x00001000;
        private const uint DDSD_MIPMAPCOUNT = 0x00020000;
        private const uint DDSD_LINEARSIZE = 0x00080000;

        // DDS pixel format flags
        private const uint DDPF_ALPHAPIXELS = 0x00000001;
        private const uint DDPF_FOURCC = 0x00000004;
        private const uint DDPF_RGB = 0x00000040;
        private const uint DDPF_LUMINANCE = 0x00020000;

        // DDS caps
        private const uint DDSCAPS_COMPLEX = 0x00000008;
        private const uint DDSCAPS_MIPMAP = 0x00400000;
        private const uint DDSCAPS_TEXTURE = 0x00001000;

        // FOURCC codes
        private static readonly uint FOURCC_DXT1 = MakeFourCC('D', 'X', 'T', '1');
        private static readonly uint FOURCC_DXT5 = MakeFourCC('D', 'X', 'T', '5');

        private static readonly HashSet<int> KnownFormatIds = new HashSet<int>
        {
            1, 2, 3, 4, 5, 7, 10, 12, 13, 24, 34, 47, 48, 62, 63
        };

        /// <summary>
        /// Parses a Unity Texture2D object from its serialized binary data.
        /// Expects the stream to be positioned at the start of the Texture2D serialized fields.
        /// Unity serializes Texture2D with big-endian integers on Xbox 360.
        /// </summary>
        public static Texture2DData ParseTexture2D(Stream source, bool bigEndian = true)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            using (var reader = new EndianBinaryReader(source, bigEndian, leaveOpen: true))
            {
                var tex = new Texture2DData();

                // Name: length-prefixed string (int32 length + UTF8 bytes)
                int nameLength = reader.ReadInt32();
                if (nameLength < 0 || nameLength > 4096)
                    throw new InvalidDataException(
                        $"Invalid Texture2D name length: {nameLength}. Stream may not be positioned correctly.");

                if (nameLength > 0)
                    tex.Name = reader.ReadString(nameLength);
                else
                    tex.Name = string.Empty;

                // Align to 4 bytes after the name string
                reader.Align(4);

                // Width and Height
                tex.Width = reader.ReadInt32();
                tex.Height = reader.ReadInt32();

                if (tex.Width <= 0 || tex.Width > 16384 || tex.Height <= 0 || tex.Height > 16384)
                    throw new InvalidDataException(
                        $"Invalid texture dimensions: {tex.Width}x{tex.Height}.");

                // Texture format (Unity internal enum value)
                int completeImageSize = reader.ReadInt32(); // m_CompleteImageSize
                tex.UnityFormatId = reader.ReadInt32();
                tex.TextureFormat = MapTextureFormat(tex.UnityFormatId);

                // Mip count
                tex.MipCount = reader.ReadInt32();
                if (tex.MipCount < 0 || tex.MipCount > 16)
                    tex.MipCount = 1;

                // ReadAllowed (m_IsReadable)
                tex.ReadAllowed = reader.ReadByte() != 0;
                reader.Align(4);

                // Image data: length prefix followed by raw bytes
                tex.ImageDataSize = reader.ReadInt32();
                if (tex.ImageDataSize > 0 && tex.ImageDataSize <= source.Length - source.Position + 64)
                {
                    tex.ImageData = reader.ReadBytes(tex.ImageDataSize);
                }
                else
                {
                    tex.ImageData = Array.Empty<byte>();
                }

                // StreamingInfo (offset, size, path)
                tex.StreamingInfo = new StreamingInfo();
                if (reader.Position + 12 <= reader.Length)
                {
                    tex.StreamingInfo.Offset = reader.ReadUInt32();
                    tex.StreamingInfo.Size = reader.ReadUInt32();

                    int pathLength = reader.ReadInt32();
                    if (pathLength > 0 && pathLength < 4096)
                    {
                        tex.StreamingInfo.Path = reader.ReadString(pathLength);
                    }
                    else
                    {
                        tex.StreamingInfo.Path = string.Empty;
                    }
                }

                return tex;
            }
        }

        /// <summary>
        /// Exports a Unity Texture2D asset to a standard image file.
        /// DXT1/DXT5 compressed textures are exported as DDS.
        /// Uncompressed formats are exported as TGA.
        /// </summary>
        /// <param name="entry">The asset entry describing the texture.</param>
        /// <param name="source">A stream positioned at the start of the Texture2D serialized data.</param>
        /// <param name="outputPath">Target file path (extension will be replaced as needed).</param>
        /// <param name="options">Export options controlling format preference and resolution limits.</param>
        /// <returns>An ExportResult indicating success or failure.</returns>
        public static ExportResult ExportTexture2D(AssetEntry entry, Stream source, string outputPath,
            ExportOptions options)
        {
            if (entry == null)
                return ExportResult.Failed("Asset entry is null.");
            if (source == null || !source.CanRead)
                return ExportResult.Failed("Source stream is null or unreadable.");

            Texture2DData tex;
            try
            {
                source.Seek(entry.Offset, SeekOrigin.Begin);
                tex = ParseTexture2D(source, bigEndian: true);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Failed to parse Texture2D '{entry.Name}': {ex.Message}");
            }

            if (tex.ImageData == null || tex.ImageData.Length == 0)
            {
                if (tex.StreamingInfo != null && tex.StreamingInfo.IsStreamed)
                {
                    return ExportResult.Failed(
                        $"Texture '{tex.Name}' is streamed from '{tex.StreamingInfo.Path}' " +
                        $"(offset={tex.StreamingInfo.Offset}, size={tex.StreamingInfo.Size}). " +
                        "External resource loading is not yet supported.");
                }
                return ExportResult.Failed($"Texture '{tex.Name}' has no image data.");
            }

            // Populate metadata on the asset entry
            PopulateMetadata(entry, tex);

            // Determine output format
            string preferredFormat = options?.PreferredFormat?.ToLowerInvariant();
            bool forceDds = preferredFormat == "dds";
            bool forceTga = preferredFormat == "tga";
            bool forcePng = preferredFormat == "png";

            string finalPath;
            long bytesWritten;

            try
            {
                if (tex.IsDxtCompressed && !forceTga && !forcePng)
                {
                    // DXT compressed: write as DDS which preserves the compressed data directly
                    finalPath = Path.ChangeExtension(outputPath, ".dds");
                    bytesWritten = WriteDds(tex, finalPath);
                }
                else if (forceDds)
                {
                    // Force DDS for uncompressed data too
                    finalPath = Path.ChangeExtension(outputPath, ".dds");
                    bytesWritten = WriteDds(tex, finalPath);
                }
                else
                {
                    // Uncompressed or non-DXT compressed: write as TGA
                    finalPath = Path.ChangeExtension(outputPath, ".tga");
                    bytesWritten = WriteTga(tex, finalPath);
                }
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Failed to write texture '{tex.Name}': {ex.Message}");
            }

            return ExportResult.Succeeded(finalPath, bytesWritten);
        }

        /// <summary>
        /// Extracts metadata from a parsed Texture2D into an AssetEntry's metadata dictionary.
        /// </summary>
        public static void PopulateMetadata(AssetEntry entry, Texture2DData tex)
        {
            if (entry == null || tex == null) return;

            entry.Metadata["TextureName"] = tex.Name;
            entry.Metadata["Width"] = tex.Width;
            entry.Metadata["Height"] = tex.Height;
            entry.Metadata["FormatName"] = tex.TextureFormat.ToString();
            entry.Metadata["UnityFormatId"] = tex.UnityFormatId;
            entry.Metadata["MipCount"] = tex.MipCount;
            entry.Metadata["ImageDataSize"] = tex.ImageDataSize;
            entry.Metadata["IsCompressed"] = tex.IsCompressed;
            entry.Metadata["BitsPerPixel"] = tex.BitsPerPixel;
            entry.Metadata["ReadAllowed"] = tex.ReadAllowed;
            entry.Metadata["IsStreamed"] = tex.StreamingInfo?.IsStreamed ?? false;

            if (tex.StreamingInfo != null && tex.StreamingInfo.IsStreamed)
            {
                entry.Metadata["StreamingPath"] = tex.StreamingInfo.Path;
                entry.Metadata["StreamingOffset"] = tex.StreamingInfo.Offset;
                entry.Metadata["StreamingSize"] = tex.StreamingInfo.Size;
            }

            entry.FormatName = $"Unity Texture2D ({tex.TextureFormat}, {tex.Width}x{tex.Height})";
            entry.Type = AssetType.Texture;
        }

        /// <summary>
        /// Maps Unity's internal TextureFormat integer ID to our enum.
        /// </summary>
        public static UnityTextureFormat MapTextureFormat(int formatId)
        {
            switch (formatId)
            {
                case 1: return UnityTextureFormat.Alpha8;
                case 2: return UnityTextureFormat.ARGB4444;
                case 3: return UnityTextureFormat.RGB24;
                case 4: return UnityTextureFormat.RGBA32;
                case 5: return UnityTextureFormat.ARGB32;
                case 7: return UnityTextureFormat.RGB565;
                case 10: return UnityTextureFormat.DXT1;
                case 12: return UnityTextureFormat.DXT5;
                case 13: return UnityTextureFormat.RGBA4444;
                case 24: return UnityTextureFormat.ETC_RGB4;
                case 34: return UnityTextureFormat.ASTC_RGB_4x4;
                case 47: return UnityTextureFormat.ASTC_RGBA_4x4;
                case 48: return UnityTextureFormat.ETC2_RGB;
                case 62: return UnityTextureFormat.RG16;
                case 63: return UnityTextureFormat.R8;
                default: return UnityTextureFormat.Unknown;
            }
        }

        #region DDS Writer

        /// <summary>
        /// Writes a DDS file with a full 128-byte header (4 magic + 124 struct) followed by pixel data.
        /// Supports DXT1/DXT5 via FOURCC and uncompressed formats via RGB pixel format descriptors.
        /// Preserves the full mipmap chain when present.
        /// </summary>
        private static long WriteDds(Texture2DData tex, string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                WriteDdsHeader(writer, tex);
                writer.Write(tex.ImageData);
                return fs.Length;
            }
        }

        /// <summary>
        /// Writes a complete DDS_HEADER: 4-byte magic + 124-byte header struct.
        /// </summary>
        private static void WriteDdsHeader(BinaryWriter writer, Texture2DData tex)
        {
            // Magic
            writer.Write(DDS_MAGIC);

            // Header size (always 124)
            writer.Write(DDS_HEADER_SIZE);

            // Flags
            uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
            if (tex.MipCount > 1)
                flags |= DDSD_MIPMAPCOUNT;

            if (tex.IsDxtCompressed)
                flags |= DDSD_LINEARSIZE;
            else
                flags |= DDSD_PITCH;

            writer.Write(flags);

            // Height, Width
            writer.Write((uint)tex.Height);
            writer.Write((uint)tex.Width);

            // Pitch or LinearSize
            if (tex.IsDxtCompressed)
            {
                uint linearSize = CalculateCompressedSize(tex.Width, tex.Height, tex.TextureFormat);
                writer.Write(linearSize);
            }
            else
            {
                uint pitch = (uint)(tex.Width * tex.BitsPerPixel + 7) / 8;
                writer.Write(pitch);
            }

            // Depth (unused for 2D textures)
            writer.Write((uint)0);

            // MipMapCount
            writer.Write((uint)tex.MipCount);

            // Reserved1[11]
            for (int i = 0; i < 11; i++)
                writer.Write((uint)0);

            // Pixel format struct (32 bytes)
            WriteDdsPixelFormat(writer, tex);

            // Caps
            uint caps = DDSCAPS_TEXTURE;
            if (tex.MipCount > 1)
                caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
            writer.Write(caps);

            // Caps2, Caps3, Caps4, Reserved2
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
        }

        /// <summary>
        /// Writes the DDS_PIXELFORMAT sub-structure (32 bytes).
        /// Uses DDPF_FOURCC for DXT formats, DDPF_RGB (with optional ALPHAPIXELS) for uncompressed.
        /// </summary>
        private static void WriteDdsPixelFormat(BinaryWriter writer, Texture2DData tex)
        {
            writer.Write(DDPF_STRUCT_SIZE); // dwSize

            switch (tex.TextureFormat)
            {
                case UnityTextureFormat.DXT1:
                    writer.Write(DDPF_FOURCC);
                    writer.Write(FOURCC_DXT1);
                    writer.Write((uint)0); // RGBBitCount
                    writer.Write((uint)0); // RBitMask
                    writer.Write((uint)0); // GBitMask
                    writer.Write((uint)0); // BBitMask
                    writer.Write((uint)0); // ABitMask
                    break;

                case UnityTextureFormat.DXT5:
                    writer.Write(DDPF_FOURCC);
                    writer.Write(FOURCC_DXT5);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    break;

                case UnityTextureFormat.RGBA32:
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0); // FourCC
                    writer.Write((uint)32); // RGBBitCount
                    writer.Write(0x000000FFu); // R
                    writer.Write(0x0000FF00u); // G
                    writer.Write(0x00FF0000u); // B
                    writer.Write(0xFF000000u); // A
                    break;

                case UnityTextureFormat.ARGB32:
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)32);
                    writer.Write(0x00FF0000u); // R
                    writer.Write(0x0000FF00u); // G
                    writer.Write(0x000000FFu); // B
                    writer.Write(0xFF000000u); // A
                    break;

                case UnityTextureFormat.RGB24:
                    writer.Write(DDPF_RGB);
                    writer.Write((uint)0);
                    writer.Write((uint)24);
                    writer.Write(0x00FF0000u); // R
                    writer.Write(0x0000FF00u); // G
                    writer.Write(0x000000FFu); // B
                    writer.Write((uint)0);     // A
                    break;

                case UnityTextureFormat.RGB565:
                    writer.Write(DDPF_RGB);
                    writer.Write((uint)0);
                    writer.Write((uint)16);
                    writer.Write(0x0000F800u); // R
                    writer.Write(0x000007E0u); // G
                    writer.Write(0x0000001Fu); // B
                    writer.Write((uint)0);
                    break;

                case UnityTextureFormat.ARGB4444:
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)16);
                    writer.Write(0x00000F00u); // R
                    writer.Write(0x000000F0u); // G
                    writer.Write(0x0000000Fu); // B
                    writer.Write(0x0000F000u); // A
                    break;

                case UnityTextureFormat.RGBA4444:
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)16);
                    writer.Write(0x0000F000u); // R
                    writer.Write(0x00000F00u); // G
                    writer.Write(0x000000F0u); // B
                    writer.Write(0x0000000Fu); // A
                    break;

                case UnityTextureFormat.Alpha8:
                    writer.Write(DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)8);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write(0x000000FFu); // A
                    break;

                case UnityTextureFormat.R8:
                    writer.Write(DDPF_LUMINANCE);
                    writer.Write((uint)0);
                    writer.Write((uint)8);
                    writer.Write(0x000000FFu); // R (luminance)
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    break;

                case UnityTextureFormat.RG16:
                    writer.Write(DDPF_LUMINANCE | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)16);
                    writer.Write(0x000000FFu); // R (luminance)
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write(0x0000FF00u); // A (second channel)
                    break;

                default:
                    // Fallback: treat as 32-bit RGBA
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write((uint)0);
                    writer.Write((uint)32);
                    writer.Write(0x000000FFu);
                    writer.Write(0x0000FF00u);
                    writer.Write(0x00FF0000u);
                    writer.Write(0xFF000000u);
                    break;
            }
        }

        /// <summary>
        /// Calculates the byte size of the top-level mip for a block-compressed format.
        /// DXT1: 8 bytes per 4x4 block. DXT5: 16 bytes per 4x4 block.
        /// </summary>
        private static uint CalculateCompressedSize(int width, int height, UnityTextureFormat format)
        {
            int blockWidth = Math.Max(1, (width + 3) / 4);
            int blockHeight = Math.Max(1, (height + 3) / 4);
            int blockSize = format == UnityTextureFormat.DXT1 ? 8 : 16;
            return (uint)(blockWidth * blockHeight * blockSize);
        }

        /// <summary>
        /// Calculates total byte size of the full mipmap chain for a compressed format.
        /// </summary>
        private static uint CalculateCompressedMipChainSize(int width, int height, int mipCount,
            UnityTextureFormat format)
        {
            uint total = 0;
            int w = width;
            int h = height;
            for (int i = 0; i < mipCount; i++)
            {
                total += CalculateCompressedSize(w, h, format);
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            return total;
        }

        #endregion

        #region TGA Writer

        /// <summary>
        /// Writes pixel data as an uncompressed TGA file.
        /// Converts from the source Unity format to 32-bit BGRA for maximum compatibility.
        /// </summary>
        private static long WriteTga(Texture2DData tex, string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            byte[] bgra = ConvertToBgra32(tex);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // TGA Header (18 bytes)
                writer.Write((byte)0);   // ID length
                writer.Write((byte)0);   // Color map type
                writer.Write((byte)2);   // Image type: uncompressed true-color
                writer.Write((short)0);  // Color map first entry
                writer.Write((short)0);  // Color map length
                writer.Write((byte)0);   // Color map entry size
                writer.Write((short)0);  // X origin
                writer.Write((short)0);  // Y origin
                writer.Write((short)tex.Width);
                writer.Write((short)tex.Height);
                writer.Write((byte)32);  // Bits per pixel
                writer.Write((byte)0x28); // Image descriptor: top-left origin, 8 alpha bits

                writer.Write(bgra);
                return fs.Length;
            }
        }

        /// <summary>
        /// Converts raw texture data from any supported uncompressed Unity format to BGRA32.
        /// Only the top-level mip is converted (mip 0).
        /// </summary>
        private static byte[] ConvertToBgra32(Texture2DData tex)
        {
            int pixelCount = tex.Width * tex.Height;
            byte[] output = new byte[pixelCount * 4];
            byte[] src = tex.ImageData;

            switch (tex.TextureFormat)
            {
                case UnityTextureFormat.RGBA32:
                    for (int i = 0; i < pixelCount && i * 4 + 3 < src.Length; i++)
                    {
                        int si = i * 4;
                        int di = i * 4;
                        output[di + 0] = src[si + 2]; // B
                        output[di + 1] = src[si + 1]; // G
                        output[di + 2] = src[si + 0]; // R
                        output[di + 3] = src[si + 3]; // A
                    }
                    break;

                case UnityTextureFormat.ARGB32:
                    for (int i = 0; i < pixelCount && i * 4 + 3 < src.Length; i++)
                    {
                        int si = i * 4;
                        int di = i * 4;
                        output[di + 0] = src[si + 3]; // B
                        output[di + 1] = src[si + 2]; // G
                        output[di + 2] = src[si + 1]; // R
                        output[di + 3] = src[si + 0]; // A
                    }
                    break;

                case UnityTextureFormat.RGB24:
                    for (int i = 0; i < pixelCount && i * 3 + 2 < src.Length; i++)
                    {
                        int si = i * 3;
                        int di = i * 4;
                        output[di + 0] = src[si + 2]; // B
                        output[di + 1] = src[si + 1]; // G
                        output[di + 2] = src[si + 0]; // R
                        output[di + 3] = 0xFF;        // A
                    }
                    break;

                case UnityTextureFormat.Alpha8:
                    for (int i = 0; i < pixelCount && i < src.Length; i++)
                    {
                        int di = i * 4;
                        output[di + 0] = 0xFF;
                        output[di + 1] = 0xFF;
                        output[di + 2] = 0xFF;
                        output[di + 3] = src[i];
                    }
                    break;

                case UnityTextureFormat.R8:
                    for (int i = 0; i < pixelCount && i < src.Length; i++)
                    {
                        int di = i * 4;
                        output[di + 0] = src[i];
                        output[di + 1] = src[i];
                        output[di + 2] = src[i];
                        output[di + 3] = 0xFF;
                    }
                    break;

                case UnityTextureFormat.RG16:
                    for (int i = 0; i < pixelCount && i * 2 + 1 < src.Length; i++)
                    {
                        int si = i * 2;
                        int di = i * 4;
                        output[di + 0] = 0;           // B
                        output[di + 1] = src[si + 1]; // G
                        output[di + 2] = src[si + 0]; // R
                        output[di + 3] = 0xFF;        // A
                    }
                    break;

                case UnityTextureFormat.RGB565:
                    for (int i = 0; i < pixelCount && i * 2 + 1 < src.Length; i++)
                    {
                        int si = i * 2;
                        ushort pixel = (ushort)(src[si] | (src[si + 1] << 8));
                        int r = (pixel >> 11) & 0x1F;
                        int g = (pixel >> 5) & 0x3F;
                        int b = pixel & 0x1F;

                        int di = i * 4;
                        output[di + 0] = (byte)((b << 3) | (b >> 2));
                        output[di + 1] = (byte)((g << 2) | (g >> 4));
                        output[di + 2] = (byte)((r << 3) | (r >> 2));
                        output[di + 3] = 0xFF;
                    }
                    break;

                case UnityTextureFormat.ARGB4444:
                    for (int i = 0; i < pixelCount && i * 2 + 1 < src.Length; i++)
                    {
                        int si = i * 2;
                        ushort pixel = (ushort)(src[si] | (src[si + 1] << 8));
                        int a = (pixel >> 12) & 0xF;
                        int r = (pixel >> 8) & 0xF;
                        int g = (pixel >> 4) & 0xF;
                        int b = pixel & 0xF;

                        int di = i * 4;
                        output[di + 0] = (byte)(b | (b << 4));
                        output[di + 1] = (byte)(g | (g << 4));
                        output[di + 2] = (byte)(r | (r << 4));
                        output[di + 3] = (byte)(a | (a << 4));
                    }
                    break;

                case UnityTextureFormat.RGBA4444:
                    for (int i = 0; i < pixelCount && i * 2 + 1 < src.Length; i++)
                    {
                        int si = i * 2;
                        ushort pixel = (ushort)(src[si] | (src[si + 1] << 8));
                        int r = (pixel >> 12) & 0xF;
                        int g = (pixel >> 8) & 0xF;
                        int b = (pixel >> 4) & 0xF;
                        int a = pixel & 0xF;

                        int di = i * 4;
                        output[di + 0] = (byte)(b | (b << 4));
                        output[di + 1] = (byte)(g | (g << 4));
                        output[di + 2] = (byte)(r | (r << 4));
                        output[di + 3] = (byte)(a | (a << 4));
                    }
                    break;

                default:
                    // For unsupported formats, copy raw bytes as-is up to BGRA buffer size
                    int copyLen = Math.Min(src.Length, output.Length);
                    Buffer.BlockCopy(src, 0, output, 0, copyLen);
                    break;
            }

            return output;
        }

        #endregion

        #region Helpers

        private static uint MakeFourCC(char a, char b, char c, char d) =>
            (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

        /// <summary>
        /// Returns a human-readable format description string for a Unity texture format.
        /// </summary>
        public static string GetFormatDescription(UnityTextureFormat format)
        {
            switch (format)
            {
                case UnityTextureFormat.Alpha8: return "Alpha 8-bit";
                case UnityTextureFormat.ARGB4444: return "ARGB 16-bit (4444)";
                case UnityTextureFormat.RGB24: return "RGB 24-bit";
                case UnityTextureFormat.RGBA32: return "RGBA 32-bit";
                case UnityTextureFormat.ARGB32: return "ARGB 32-bit";
                case UnityTextureFormat.RGB565: return "RGB 16-bit (565)";
                case UnityTextureFormat.DXT1: return "DXT1 (BC1) Compressed";
                case UnityTextureFormat.DXT5: return "DXT5 (BC3) Compressed";
                case UnityTextureFormat.RGBA4444: return "RGBA 16-bit (4444)";
                case UnityTextureFormat.ETC_RGB4: return "ETC1 RGB4 Compressed";
                case UnityTextureFormat.ASTC_RGB_4x4: return "ASTC RGB 4x4 Compressed";
                case UnityTextureFormat.ASTC_RGBA_4x4: return "ASTC RGBA 4x4 Compressed";
                case UnityTextureFormat.ETC2_RGB: return "ETC2 RGB Compressed";
                case UnityTextureFormat.RG16: return "RG 16-bit";
                case UnityTextureFormat.R8: return "R 8-bit (Grayscale)";
                default: return $"Unknown (ID={((int)format)})";
            }
        }

        /// <summary>
        /// Checks whether a given Unity format ID is recognized by this extractor.
        /// </summary>
        public static bool IsFormatSupported(int formatId) => KnownFormatIds.Contains(formatId);

        #endregion
    }
}
