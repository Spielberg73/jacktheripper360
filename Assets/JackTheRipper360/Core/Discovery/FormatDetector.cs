using System;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// Identifies file formats by magic bytes and file signatures.
    /// </summary>
    public static class FormatDetector
    {
        public struct FormatMatch
        {
            public AssetType Type;
            public string FormatName;
            public float Confidence; // 0.0 - 1.0
        }

        /// <summary>
        /// Identify a file's format by reading its magic bytes.
        /// </summary>
        public static FormatMatch Identify(Stream stream)
        {
            if (stream.Length < 4)
                return new FormatMatch { Type = AssetType.Unknown, FormatName = "Unknown", Confidence = 0 };

            long savedPos = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[Math.Min(32, stream.Length)];
            stream.Read(header, 0, header.Length);
            stream.Position = savedPos;

            // Check container formats
            if (MatchesMagic(header, Xbox360Constants.XEX2_MAGIC))
                return new FormatMatch { Type = AssetType.Executable, FormatName = "XEX2", Confidence = 1.0f };

            uint magic32 = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
            if (magic32 == Xbox360Constants.STFS_MAGIC_CON || magic32 == Xbox360Constants.STFS_MAGIC_LIVE || magic32 == Xbox360Constants.STFS_MAGIC_PIRS)
                return new FormatMatch { Type = AssetType.Container, FormatName = "STFS", Confidence = 1.0f };

            // Check XDVDFS (need to read at sector 32)
            if (stream.Length > Xbox360Constants.XDVDFS_ROOT_SECTOR * Xbox360Constants.XDVDFS_SECTOR_SIZE + 20)
            {
                stream.Seek(Xbox360Constants.XDVDFS_ROOT_SECTOR * Xbox360Constants.XDVDFS_SECTOR_SIZE, SeekOrigin.Begin);
                byte[] xdvdfs = new byte[20];
                stream.Read(xdvdfs, 0, 20);
                stream.Position = savedPos;
                if (MatchesMagic(xdvdfs, Xbox360Constants.XDVDFS_MAGIC))
                    return new FormatMatch { Type = AssetType.Container, FormatName = "XDVDFS", Confidence = 1.0f };
            }

            // Texture formats
            if (MatchesMagic(header, Xbox360Constants.DDS_MAGIC))
                return new FormatMatch { Type = AssetType.Texture, FormatName = "DDS", Confidence = 1.0f };

            if (MatchesMagic(header, Xbox360Constants.XPR0_MAGIC))
                return new FormatMatch { Type = AssetType.Texture, FormatName = "XPR0", Confidence = 1.0f };

            if (MatchesMagic(header, Xbox360Constants.XPR2_MAGIC))
                return new FormatMatch { Type = AssetType.Texture, FormatName = "XPR2", Confidence = 1.0f };

            // Video formats
            if (header.Length >= 3 && header[0] == 'B' && header[1] == 'I' && header[2] == 'K')
                return new FormatMatch { Type = AssetType.Video, FormatName = "Bink", Confidence = 1.0f };

            if (MatchesMagic(header, Xbox360Constants.XMV_MAGIC))
                return new FormatMatch { Type = AssetType.Video, FormatName = "XMV/WMV", Confidence = 0.9f };

            // Audio formats
            if (MatchesMagic(header, Xbox360Constants.RIFF_MAGIC))
            {
                // RIFF can be WAV, XMA, xWMA
                if (stream.Length >= 12)
                {
                    string subFormat = System.Text.Encoding.ASCII.GetString(header, 8, 4);
                    if (subFormat == "WAVE")
                    {
                        // Check format tag at offset 20
                        if (stream.Length >= 22)
                        {
                            stream.Seek(20, SeekOrigin.Begin);
                            byte[] fmtTag = new byte[2];
                            stream.Read(fmtTag, 0, 2);
                            stream.Position = savedPos;
                            ushort tag = (ushort)(fmtTag[0] | (fmtTag[1] << 8));
                            if (tag == 0x0166 || tag == 0x0165)
                                return new FormatMatch { Type = AssetType.Audio, FormatName = "XMA", Confidence = 1.0f };
                            if (tag == 0x0161 || tag == 0x0162)
                                return new FormatMatch { Type = AssetType.Audio, FormatName = "xWMA", Confidence = 0.9f };
                        }
                        return new FormatMatch { Type = AssetType.Audio, FormatName = "WAV", Confidence = 0.8f };
                    }
                    if (subFormat == "xWMA")
                        return new FormatMatch { Type = AssetType.Audio, FormatName = "xWMA", Confidence = 1.0f };
                }
                return new FormatMatch { Type = AssetType.Audio, FormatName = "RIFF Audio", Confidence = 0.6f };
            }

            // XACT wave bank
            if ((header[0] == 'W' && header[1] == 'B' && header[2] == 'N' && header[3] == 'D') ||
                (header[0] == 'D' && header[1] == 'N' && header[2] == 'B' && header[3] == 'W'))
                return new FormatMatch { Type = AssetType.Audio, FormatName = "XACT Wave Bank", Confidence = 1.0f };

            // XACT sound bank
            if (header[0] == 'S' && header[1] == 'D' && header[2] == 'B' && header[3] == 'K')
                return new FormatMatch { Type = AssetType.Audio, FormatName = "XACT Sound Bank", Confidence = 1.0f };

            // Archive formats
            if (header[0] == 'B' && header[1] == 'I' && header[2] == 'G' && header[3] == 'F')
                return new FormatMatch { Type = AssetType.Archive, FormatName = "BIG Archive", Confidence = 0.9f };

            // Try extension-based detection
            return new FormatMatch { Type = AssetType.Unknown, FormatName = "Unknown", Confidence = 0 };
        }

        /// <summary>
        /// Identify format by file extension.
        /// </summary>
        public static FormatMatch IdentifyByExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();

            switch (ext)
            {
                // Textures
                case ".dds": return new FormatMatch { Type = AssetType.Texture, FormatName = "DDS", Confidence = 0.7f };
                case ".xpr": return new FormatMatch { Type = AssetType.Texture, FormatName = "XPR", Confidence = 0.7f };
                case ".png": return new FormatMatch { Type = AssetType.Texture, FormatName = "PNG", Confidence = 0.8f };
                case ".tga": return new FormatMatch { Type = AssetType.Texture, FormatName = "TGA", Confidence = 0.8f };

                // Models
                case ".mesh": return new FormatMatch { Type = AssetType.Model, FormatName = "Mesh", Confidence = 0.5f };
                case ".model": return new FormatMatch { Type = AssetType.Model, FormatName = "Model", Confidence = 0.5f };
                case ".geo": return new FormatMatch { Type = AssetType.Model, FormatName = "Geometry", Confidence = 0.5f };
                case ".xmodel": return new FormatMatch { Type = AssetType.Model, FormatName = "XModel", Confidence = 0.6f };

                // Audio
                case ".xma": return new FormatMatch { Type = AssetType.Audio, FormatName = "XMA", Confidence = 0.8f };
                case ".wav": return new FormatMatch { Type = AssetType.Audio, FormatName = "WAV", Confidence = 0.8f };
                case ".xwb": return new FormatMatch { Type = AssetType.Audio, FormatName = "XWB", Confidence = 0.8f };
                case ".xsb": return new FormatMatch { Type = AssetType.Audio, FormatName = "XSB", Confidence = 0.8f };
                case ".wma": return new FormatMatch { Type = AssetType.Audio, FormatName = "WMA", Confidence = 0.8f };

                // Video
                case ".bik": return new FormatMatch { Type = AssetType.Video, FormatName = "Bink", Confidence = 0.8f };
                case ".wmv": return new FormatMatch { Type = AssetType.Video, FormatName = "WMV", Confidence = 0.7f };
                case ".xmv": return new FormatMatch { Type = AssetType.Video, FormatName = "XMV", Confidence = 0.8f };

                // Animation
                case ".anim": return new FormatMatch { Type = AssetType.Animation, FormatName = "Animation", Confidence = 0.5f };
                case ".anm": return new FormatMatch { Type = AssetType.Animation, FormatName = "Animation", Confidence = 0.5f };

                // Containers
                case ".iso": return new FormatMatch { Type = AssetType.Container, FormatName = "ISO", Confidence = 0.7f };
                case ".xex": return new FormatMatch { Type = AssetType.Executable, FormatName = "XEX", Confidence = 0.8f };

                default: return new FormatMatch { Type = AssetType.Unknown, FormatName = "Unknown", Confidence = 0 };
            }
        }

        private static bool MatchesMagic(byte[] data, byte[] magic)
        {
            if (data.Length < magic.Length) return false;
            for (int i = 0; i < magic.Length; i++)
            {
                if (data[i] != magic[i]) return false;
            }
            return true;
        }
    }
}
