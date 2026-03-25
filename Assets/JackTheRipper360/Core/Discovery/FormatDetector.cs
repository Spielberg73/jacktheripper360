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

            // FPG (Backbone Entertainment Graphics Package) - magic "30GF"
            if (header[0] == 0x33 && header[1] == 0x30 && header[2] == 0x47 && header[3] == 0x46)
                return new FormatMatch { Type = AssetType.Container, FormatName = "FPG (Graphics Package)", Confidence = 1.0f };

            // Archive formats
            if (header[0] == 'B' && header[1] == 'I' && header[2] == 'G' && header[3] == 'F')
                return new FormatMatch { Type = AssetType.Archive, FormatName = "BIG Archive", Confidence = 0.9f };

            // id Tech PAK format (Quake 1/2)
            if (header[0] == 'P' && header[1] == 'A' && header[2] == 'C' && header[3] == 'K')
                return new FormatMatch { Type = AssetType.Container, FormatName = "id Tech PAK", Confidence = 1.0f };

            // id Tech PK3/PK4 (ZIP format)
            if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
                return new FormatMatch { Type = AssetType.Archive, FormatName = "ZIP/PK3/PK4", Confidence = 0.8f };

            // WAD formats (Doom/Quake)
            if ((header[0] == 'I' || header[0] == 'P') && header[1] == 'W' && header[2] == 'A' && header[3] == 'D')
                return new FormatMatch { Type = AssetType.Container, FormatName = header[0] == 'I' ? "IWAD (Doom)" : "PWAD (Doom)", Confidence = 1.0f };
            if (header[0] == 'W' && header[1] == 'A' && header[2] == 'D' && (header[3] == '2' || header[3] == '3'))
                return new FormatMatch { Type = AssetType.Container, FormatName = $"WAD{(char)header[3]} (Quake)", Confidence = 1.0f };

            // id Tech BSP formats
            if (header[0] == 'I' && header[1] == 'B' && header[2] == 'S' && header[3] == 'P')
            {
                uint bspVersion = (uint)(header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24));
                if (bspVersion == 38) return new FormatMatch { Type = AssetType.Container, FormatName = "BSP (id Tech 2/Quake 2)", Confidence = 1.0f };
                if (bspVersion == 46 || bspVersion == 47) return new FormatMatch { Type = AssetType.Container, FormatName = "BSP (id Tech 3/Quake 3)", Confidence = 1.0f };
                if (bspVersion == 4) return new FormatMatch { Type = AssetType.Container, FormatName = "BSP (id Tech 4/Doom 3)", Confidence = 0.9f };
                return new FormatMatch { Type = AssetType.Container, FormatName = $"BSP (IBSP v{bspVersion})", Confidence = 0.8f };
            }
            if (header[0] == 'R' && header[1] == 'B' && header[2] == 'S' && header[3] == 'P')
                return new FormatMatch { Type = AssetType.Container, FormatName = "BSP (id Tech 4 RBSP)", Confidence = 0.9f };

            // Source Engine VBSP
            if (header[0] == 'V' && header[1] == 'B' && header[2] == 'S' && header[3] == 'P')
            {
                uint vbspVersion = (uint)(header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24));
                return new FormatMatch { Type = AssetType.Container, FormatName = $"BSP (Source Engine v{vbspVersion})", Confidence = 1.0f };
            }

            // Source VPK
            if (header.Length >= 8 && header[0] == 0x34 && header[1] == 0x12 && header[2] == 0xAA && header[3] == 0x55)
                return new FormatMatch { Type = AssetType.Container, FormatName = "VPK (Source Engine)", Confidence = 1.0f };

            // Source VTF texture
            if (header[0] == 'V' && header[1] == 'T' && header[2] == 'F' && header[3] == 0x00)
                return new FormatMatch { Type = AssetType.Texture, FormatName = "VTF (Source Engine)", Confidence = 1.0f };

            // Source MDL model
            if (header[0] == 'I' && header[1] == 'D' && header[2] == 'S' && header[3] == 'T')
                return new FormatMatch { Type = AssetType.Model, FormatName = "MDL (Source/GoldSrc)", Confidence = 0.95f };

            // id Tech 3 MD3 model
            if (header[0] == 'I' && header[1] == 'D' && header[2] == 'P' && header[3] == '3')
                return new FormatMatch { Type = AssetType.Model, FormatName = "MD3 (id Tech 3)", Confidence = 1.0f };

            // Unity Asset Bundle formats
            if (header.Length >= 7)
            {
                string headerStr = System.Text.Encoding.ASCII.GetString(header, 0, Math.Min(header.Length, 10));
                if (headerStr.StartsWith("UnityFS"))
                    return new FormatMatch { Type = AssetType.Container, FormatName = "Unity AssetBundle (UnityFS)", Confidence = 1.0f };
                if (headerStr.StartsWith("UnityWeb"))
                    return new FormatMatch { Type = AssetType.Container, FormatName = "Unity AssetBundle (UnityWeb)", Confidence = 1.0f };
                if (headerStr.StartsWith("UnityRaw"))
                    return new FormatMatch { Type = AssetType.Container, FormatName = "Unity AssetBundle (UnityRaw)", Confidence = 1.0f };
            }

            // Unreal Engine 4/5 UAsset (magic 0xC1832A9E in little-endian = 9E 2A 83 C1)
            if (header.Length >= 8 && header[0] == 0xC1 && header[1] == 0x83 && header[2] == 0x2A && header[3] == 0x9E)
                return new FormatMatch { Type = AssetType.Container, FormatName = "UAsset (UE4/UE5)", Confidence = 1.0f };
            // Also check little-endian variant
            if (header.Length >= 8 && header[0] == 0x9E && header[1] == 0x2A && header[2] == 0x83 && header[3] == 0xC1)
                return new FormatMatch { Type = AssetType.Container, FormatName = "UAsset (UE3/UE4)", Confidence = 1.0f };

            // Unreal PAK (footer-based, check if file is large enough)
            if (stream.Length > 64)
            {
                stream.Seek(stream.Length - 44, SeekOrigin.Begin);
                byte[] footer = new byte[4];
                stream.Read(footer, 0, 4);
                stream.Position = savedPos;
                if (footer[0] == 0xE1 && footer[1] == 0x12 && footer[2] == 0x6F && footer[3] == 0x5A)
                    return new FormatMatch { Type = AssetType.Container, FormatName = "Unreal PAK", Confidence = 0.95f };
                // Little-endian variant
                if (footer[0] == 0x5A && footer[1] == 0x6F && footer[2] == 0x12 && footer[3] == 0xE1)
                    return new FormatMatch { Type = AssetType.Container, FormatName = "Unreal PAK", Confidence = 0.95f };
            }

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
                case ".fpg": return new FormatMatch { Type = AssetType.Container, FormatName = "FPG (Graphics Package)", Confidence = 0.8f };

                // id Tech formats
                case ".pak": return new FormatMatch { Type = AssetType.Container, FormatName = "PAK Archive", Confidence = 0.7f };
                case ".pk3": return new FormatMatch { Type = AssetType.Archive, FormatName = "PK3 (id Tech 3)", Confidence = 0.8f };
                case ".pk4": return new FormatMatch { Type = AssetType.Archive, FormatName = "PK4 (id Tech 4)", Confidence = 0.8f };
                case ".wad": return new FormatMatch { Type = AssetType.Container, FormatName = "WAD", Confidence = 0.7f };
                case ".bsp": return new FormatMatch { Type = AssetType.Container, FormatName = "BSP Map", Confidence = 0.8f };
                case ".md3": return new FormatMatch { Type = AssetType.Model, FormatName = "MD3 Model", Confidence = 0.8f };
                case ".md5mesh": return new FormatMatch { Type = AssetType.Model, FormatName = "MD5 Mesh", Confidence = 0.8f };
                case ".md5anim": return new FormatMatch { Type = AssetType.Animation, FormatName = "MD5 Anim", Confidence = 0.8f };
                case ".mdl": return new FormatMatch { Type = AssetType.Model, FormatName = "MDL Model", Confidence = 0.7f };
                case ".mtr": return new FormatMatch { Type = AssetType.Data, FormatName = "Material (id Tech 4)", Confidence = 0.6f };
                case ".proc": return new FormatMatch { Type = AssetType.Data, FormatName = "Proc (id Tech 4)", Confidence = 0.6f };
                case ".lmp": return new FormatMatch { Type = AssetType.Data, FormatName = "Lump (Quake)", Confidence = 0.5f };
                case ".resources": return new FormatMatch { Type = AssetType.Container, FormatName = "Resources (id Tech 5+)", Confidence = 0.7f };
                case ".bimage": return new FormatMatch { Type = AssetType.Texture, FormatName = "BImage (id Tech 5+)", Confidence = 0.7f };
                case ".bmodel": return new FormatMatch { Type = AssetType.Model, FormatName = "BModel (id Tech 5+)", Confidence = 0.7f };

                // Source Engine formats
                case ".vpk": return new FormatMatch { Type = AssetType.Container, FormatName = "VPK (Source)", Confidence = 0.8f };
                case ".vtf": return new FormatMatch { Type = AssetType.Texture, FormatName = "VTF (Source)", Confidence = 0.8f };
                case ".vmt": return new FormatMatch { Type = AssetType.Data, FormatName = "VMT Material (Source)", Confidence = 0.7f };
                case ".vvd": return new FormatMatch { Type = AssetType.Model, FormatName = "VVD Vertex Data (Source)", Confidence = 0.7f };
                case ".vtx": return new FormatMatch { Type = AssetType.Model, FormatName = "VTX Strip Data (Source)", Confidence = 0.7f };
                case ".phy": return new FormatMatch { Type = AssetType.Data, FormatName = "Physics (Source)", Confidence = 0.6f };
                case ".pcf": return new FormatMatch { Type = AssetType.Data, FormatName = "Particle (Source)", Confidence = 0.6f };

                // Unity formats
                case ".assets": return new FormatMatch { Type = AssetType.Container, FormatName = "Unity Assets", Confidence = 0.7f };
                case ".unity3d": return new FormatMatch { Type = AssetType.Container, FormatName = "Unity AssetBundle", Confidence = 0.8f };
                case ".bundle": return new FormatMatch { Type = AssetType.Container, FormatName = "Unity AssetBundle", Confidence = 0.6f };
                case ".resource": return new FormatMatch { Type = AssetType.Data, FormatName = "Unity Resource", Confidence = 0.5f };
                case ".ress": return new FormatMatch { Type = AssetType.Data, FormatName = "Unity Streaming Resource", Confidence = 0.5f };

                // Unreal Engine formats (note: .pak already handled above under id Tech)
                case ".uasset": return new FormatMatch { Type = AssetType.Container, FormatName = "UAsset", Confidence = 0.8f };
                case ".umap": return new FormatMatch { Type = AssetType.Container, FormatName = "UMap", Confidence = 0.8f };
                case ".uexp": return new FormatMatch { Type = AssetType.Data, FormatName = "UExp (Export Data)", Confidence = 0.7f };
                case ".ubulk": return new FormatMatch { Type = AssetType.Data, FormatName = "UBulk (Bulk Data)", Confidence = 0.7f };
                case ".upk": return new FormatMatch { Type = AssetType.Container, FormatName = "Unreal Package (UE3)", Confidence = 0.8f };
                case ".u": return new FormatMatch { Type = AssetType.Container, FormatName = "Unreal Package", Confidence = 0.6f };

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
