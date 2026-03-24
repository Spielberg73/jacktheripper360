using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins.Unreal
{
    /// <summary>
    /// Exports Unreal Engine asset data to reimportable standard formats.
    /// Handles Texture2D->DDS, StaticMesh->OBJ/FBX, SoundWave->OGG/WAV,
    /// AnimSequence->JSON, and generic raw export with JSON metadata sidecars.
    /// </summary>
    public static class UnrealAssetExporter
    {
        #region Pixel Format Constants

        private const string PF_DXT1 = "PF_DXT1";
        private const string PF_DXT5 = "PF_DXT5";
        private const string PF_BC4 = "PF_BC4";
        private const string PF_BC5 = "PF_BC5";
        private const string PF_BC7 = "PF_BC7";
        private const string PF_B8G8R8A8 = "PF_B8G8R8A8";
        private const string PF_R8G8B8A8 = "PF_R8G8B8A8";
        private const string PF_FloatRGBA = "PF_FloatRGBA";
        private const string PF_G8 = "PF_G8";
        private const string PF_A8 = "PF_A8";

        #endregion

        #region Texture2D Export

        /// <summary>
        /// Export a Texture2D asset as DDS with proper format headers.
        /// </summary>
        public static ExportResult ExportTexture2D(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Extract texture metadata from entry
                int width = GetMetadataInt(entry, "SizeX", 256);
                int height = GetMetadataInt(entry, "SizeY", 256);
                string pixelFormat = GetMetadataString(entry, "PixelFormat", PF_DXT1);
                int mipCount = GetMetadataInt(entry, "MipCount", 1);
                long dataSize = entry.Size > 0 ? entry.Size : source.Length - source.Position;

                // Read texture data
                byte[] textureData = new byte[dataSize];
                int bytesRead = source.Read(textureData, 0, (int)dataSize);

                // Determine DDS format parameters
                uint fourCC;
                int bitsPerPixel;
                uint flags;
                uint rgbBitCount;
                uint rMask, gMask, bMask, aMask;
                bool usesDx10Header;

                GetDdsFormatParams(pixelFormat, out fourCC, out bitsPerPixel, out flags,
                    out rgbBitCount, out rMask, out gMask, out bMask, out aMask, out usesDx10Header);

                // Ensure output path has .dds extension
                if (!outputPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    outputPath = Path.ChangeExtension(outputPath, ".dds");

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // DDS Magic
                    writer.Write(0x20534444); // "DDS "

                    // DDS_HEADER (124 bytes)
                    writer.Write(124); // dwSize
                    uint headerFlags = 0x1 | 0x2 | 0x4 | 0x1000; // CAPS | HEIGHT | WIDTH | PIXELFORMAT
                    if (mipCount > 1) headerFlags |= 0x20000; // MIPMAPCOUNT
                    if (fourCC != 0) headerFlags |= 0x80000; // LINEARSIZE
                    writer.Write(headerFlags); // dwFlags
                    writer.Write(height); // dwHeight
                    writer.Write(width); // dwWidth

                    // Pitch or linear size
                    int linearSize = CalculateLinearSize(width, height, pixelFormat);
                    writer.Write(linearSize); // dwPitchOrLinearSize

                    writer.Write(0); // dwDepth
                    writer.Write(mipCount); // dwMipMapCount

                    // Reserved (11 DWORDs)
                    for (int i = 0; i < 11; i++)
                        writer.Write(0);

                    // DDS_PIXELFORMAT (32 bytes)
                    writer.Write(32); // dwSize
                    writer.Write(flags); // dwFlags (DDPF_FOURCC or DDPF_RGB etc.)

                    if (fourCC != 0)
                        writer.Write(fourCC); // dwFourCC
                    else
                        writer.Write(0);

                    writer.Write(rgbBitCount); // dwRGBBitCount
                    writer.Write(rMask); // dwRBitMask
                    writer.Write(gMask); // dwGBitMask
                    writer.Write(bMask); // dwBBitMask
                    writer.Write(aMask); // dwABitMask

                    // Caps
                    uint caps = 0x1000; // DDSCAPS_TEXTURE
                    if (mipCount > 1) caps |= 0x8 | 0x400000; // COMPLEX | MIPMAP
                    writer.Write(caps); // dwCaps
                    writer.Write(0); // dwCaps2
                    writer.Write(0); // dwCaps3
                    writer.Write(0); // dwCaps4
                    writer.Write(0); // dwReserved2

                    // DX10 extended header for BC7
                    if (usesDx10Header)
                    {
                        writer.Write(98); // DXGI_FORMAT_BC7_UNORM
                        writer.Write(3); // D3D10_RESOURCE_DIMENSION_TEXTURE2D
                        writer.Write(0u); // miscFlag
                        writer.Write(1u); // arraySize
                        writer.Write(0u); // miscFlags2
                    }

                    // Write texture data
                    writer.Write(textureData, 0, bytesRead);
                }

                return ExportResult.Succeeded(outputPath, bytesRead + 128);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Texture2D export failed: {ex.Message}");
            }
        }

        private static void GetDdsFormatParams(string pixelFormat, out uint fourCC, out int bpp,
            out uint flags, out uint rgbBitCount, out uint rMask, out uint gMask, out uint bMask,
            out uint aMask, out bool dx10)
        {
            fourCC = 0;
            bpp = 32;
            flags = 0;
            rgbBitCount = 0;
            rMask = gMask = bMask = aMask = 0;
            dx10 = false;

            switch (pixelFormat)
            {
                case PF_DXT1:
                    fourCC = 0x31545844; // "DXT1"
                    flags = 0x4; // DDPF_FOURCC
                    bpp = 4;
                    break;

                case PF_DXT5:
                    fourCC = 0x35545844; // "DXT5"
                    flags = 0x4;
                    bpp = 8;
                    break;

                case PF_BC4:
                    fourCC = 0x31495441; // "ATI1"
                    flags = 0x4;
                    bpp = 4;
                    break;

                case PF_BC5:
                    fourCC = 0x32495441; // "ATI2"
                    flags = 0x4;
                    bpp = 8;
                    break;

                case PF_BC7:
                    fourCC = 0x30315844; // "DX10"
                    flags = 0x4;
                    bpp = 8;
                    dx10 = true;
                    break;

                case PF_B8G8R8A8:
                    flags = 0x41; // DDPF_RGB | DDPF_ALPHAPIXELS
                    rgbBitCount = 32;
                    bMask = 0x000000FF;
                    gMask = 0x0000FF00;
                    rMask = 0x00FF0000;
                    aMask = 0xFF000000;
                    bpp = 32;
                    break;

                case PF_R8G8B8A8:
                    flags = 0x41;
                    rgbBitCount = 32;
                    rMask = 0x000000FF;
                    gMask = 0x0000FF00;
                    bMask = 0x00FF0000;
                    aMask = 0xFF000000;
                    bpp = 32;
                    break;

                case PF_G8:
                case PF_A8:
                    flags = 0x20000; // DDPF_LUMINANCE
                    rgbBitCount = 8;
                    rMask = 0xFF;
                    bpp = 8;
                    break;

                case PF_FloatRGBA:
                    fourCC = 0x71; // D3DFMT_A16B16G16R16F
                    flags = 0x4;
                    bpp = 64;
                    break;

                default:
                    // Fallback to DXT1
                    fourCC = 0x31545844;
                    flags = 0x4;
                    bpp = 4;
                    break;
            }
        }

        private static int CalculateLinearSize(int width, int height, string format)
        {
            switch (format)
            {
                case PF_DXT1:
                case PF_BC4:
                    return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;
                case PF_DXT5:
                case PF_BC5:
                case PF_BC7:
                    return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16;
                case PF_B8G8R8A8:
                case PF_R8G8B8A8:
                    return width * height * 4;
                case PF_G8:
                case PF_A8:
                    return width * height;
                case PF_FloatRGBA:
                    return width * height * 8;
                default:
                    return width * height * 4;
            }
        }

        #endregion

        #region StaticMesh Export

        /// <summary>
        /// Export a StaticMesh or SkeletalMesh as OBJ format.
        /// </summary>
        public static ExportResult ExportStaticMesh(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (!outputPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    outputPath = Path.ChangeExtension(outputPath, ".obj");

                // Try to parse mesh data from stream
                int vertexCount = GetMetadataInt(entry, "VertexCount", 0);
                int indexCount = GetMetadataInt(entry, "IndexCount", 0);
                int sectionCount = GetMetadataInt(entry, "SectionCount", 1);
                bool hasNormals = GetMetadataBool(entry, "HasNormals", false);
                bool hasUVs = GetMetadataBool(entry, "HasUVs", false);

                byte[] data = new byte[entry.Size > 0 ? entry.Size : source.Length - source.Position];
                int bytesRead = source.Read(data, 0, data.Length);

                using (var sw = new StreamWriter(outputPath, false, Encoding.ASCII))
                {
                    sw.WriteLine($"# Unreal Engine StaticMesh exported by JackTheRipper360");
                    sw.WriteLine($"# Source: {entry.Name}");
                    sw.WriteLine($"# Vertices: {vertexCount}, Indices: {indexCount}");
                    sw.WriteLine();

                    if (vertexCount > 0 && data.Length >= vertexCount * 12)
                    {
                        // Parse vertex buffer (assume float3 position at start)
                        int offset = 0;
                        int stride = hasNormals ? 24 : 12;
                        if (hasUVs) stride += 8;

                        // Adjust if data doesn't fit expected stride
                        if (data.Length < vertexCount * stride)
                            stride = 12; // Fallback to position only

                        // Vertices
                        for (int i = 0; i < vertexCount && offset + 12 <= data.Length; i++)
                        {
                            float x = BitConverter.ToSingle(data, offset);
                            float y = BitConverter.ToSingle(data, offset + 4);
                            float z = BitConverter.ToSingle(data, offset + 8);
                            sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0:F6} {1:F6} {2:F6}", x, y, z));
                            offset += stride;
                        }

                        // Normals
                        if (hasNormals && stride >= 24)
                        {
                            sw.WriteLine();
                            offset = 12;
                            for (int i = 0; i < vertexCount && offset + 12 <= data.Length; i++)
                            {
                                float nx = BitConverter.ToSingle(data, offset);
                                float ny = BitConverter.ToSingle(data, offset + 4);
                                float nz = BitConverter.ToSingle(data, offset + 8);
                                sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "vn {0:F6} {1:F6} {2:F6}", nx, ny, nz));
                                offset += stride;
                            }
                        }

                        // UVs
                        if (hasUVs)
                        {
                            sw.WriteLine();
                            int uvStart = hasNormals ? 24 : 12;
                            offset = uvStart;
                            for (int i = 0; i < vertexCount && offset + 8 <= data.Length; i++)
                            {
                                float u = BitConverter.ToSingle(data, offset);
                                float v = 1.0f - BitConverter.ToSingle(data, offset + 4); // Flip V
                                sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "vt {0:F6} {1:F6}", u, v));
                                offset += stride;
                            }
                        }

                        // Faces from index buffer
                        if (indexCount > 0)
                        {
                            sw.WriteLine();
                            int indexOffset = vertexCount * stride;
                            bool use32Bit = GetMetadataBool(entry, "Use32BitIndices", indexCount > 65535);
                            int indexSize = use32Bit ? 4 : 2;

                            sw.WriteLine($"g {SanitizeName(entry.Name)}");
                            sw.WriteLine($"usemtl Material_0");

                            for (int i = 0; i + 2 < indexCount && indexOffset + (i + 3) * indexSize <= data.Length; i += 3)
                            {
                                int i0, i1, i2;
                                if (use32Bit)
                                {
                                    i0 = BitConverter.ToInt32(data, indexOffset + i * 4) + 1;
                                    i1 = BitConverter.ToInt32(data, indexOffset + (i + 1) * 4) + 1;
                                    i2 = BitConverter.ToInt32(data, indexOffset + (i + 2) * 4) + 1;
                                }
                                else
                                {
                                    i0 = BitConverter.ToUInt16(data, indexOffset + i * 2) + 1;
                                    i1 = BitConverter.ToUInt16(data, indexOffset + (i + 1) * 2) + 1;
                                    i2 = BitConverter.ToUInt16(data, indexOffset + (i + 2) * 2) + 1;
                                }

                                if (hasNormals && hasUVs)
                                    sw.WriteLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
                                else if (hasNormals)
                                    sw.WriteLine($"f {i0}//{i0} {i1}//{i1} {i2}//{i2}");
                                else
                                    sw.WriteLine($"f {i0} {i1} {i2}");
                            }
                        }
                    }
                    else
                    {
                        // Raw data dump with comment
                        sw.WriteLine("# Could not parse structured mesh data");
                        sw.WriteLine($"# Raw data size: {bytesRead} bytes");
                        sw.WriteLine("# Use a hex editor or UModel for advanced extraction");
                    }
                }

                // Write JSON metadata sidecar
                WriteMetadataSidecar(entry, outputPath);

                return ExportResult.Succeeded(outputPath, bytesRead);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"StaticMesh export failed: {ex.Message}");
            }
        }

        #endregion

        #region SoundWave Export

        /// <summary>
        /// Export SoundWave as OGG or WAV.
        /// </summary>
        public static ExportResult ExportSoundWave(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                int sampleRate = GetMetadataInt(entry, "SampleRate", 44100);
                int channels = GetMetadataInt(entry, "NumChannels", 2);
                string audioFormat = GetMetadataString(entry, "AudioFormat", "OGG");
                long dataSize = entry.Size > 0 ? entry.Size : source.Length - source.Position;

                byte[] audioData = new byte[dataSize];
                int bytesRead = source.Read(audioData, 0, (int)dataSize);

                // Check if data starts with OGG magic
                bool isOgg = bytesRead >= 4 && audioData[0] == 'O' && audioData[1] == 'g' &&
                             audioData[2] == 'g' && audioData[3] == 'S';

                if (isOgg)
                {
                    // Export as OGG directly
                    outputPath = Path.ChangeExtension(outputPath, ".ogg");
                    File.WriteAllBytes(outputPath, audioData);
                }
                else
                {
                    // Export as WAV with header
                    outputPath = Path.ChangeExtension(outputPath, ".wav");
                    int bitsPerSample = 16;
                    int byteRate = sampleRate * channels * (bitsPerSample / 8);
                    int blockAlign = channels * (bitsPerSample / 8);

                    using (var fs = new FileStream(outputPath, FileMode.Create))
                    using (var writer = new BinaryWriter(fs))
                    {
                        // RIFF header
                        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                        writer.Write(36 + bytesRead); // chunk size
                        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                        // fmt sub-chunk
                        writer.Write(Encoding.ASCII.GetBytes("fmt "));
                        writer.Write(16); // sub-chunk size
                        writer.Write((short)1); // PCM format
                        writer.Write((short)channels);
                        writer.Write(sampleRate);
                        writer.Write(byteRate);
                        writer.Write((short)blockAlign);
                        writer.Write((short)bitsPerSample);

                        // data sub-chunk
                        writer.Write(Encoding.ASCII.GetBytes("data"));
                        writer.Write(bytesRead);
                        writer.Write(audioData, 0, bytesRead);
                    }
                }

                WriteMetadataSidecar(entry, outputPath);
                return ExportResult.Succeeded(outputPath, bytesRead);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"SoundWave export failed: {ex.Message}");
            }
        }

        #endregion

        #region AnimSequence Export

        /// <summary>
        /// Export AnimSequence as JSON animation data.
        /// </summary>
        public static ExportResult ExportAnimSequence(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                outputPath = Path.ChangeExtension(outputPath, ".json");

                float duration = GetMetadataFloat(entry, "SequenceLength", 0);
                int numFrames = GetMetadataInt(entry, "NumFrames", 0);
                float rateScale = GetMetadataFloat(entry, "RateScale", 1.0f);
                string animName = GetMetadataString(entry, "AnimationName", entry.Name);

                // Read raw animation data
                long dataSize = entry.Size > 0 ? entry.Size : source.Length - source.Position;
                byte[] rawData = new byte[dataSize];
                int bytesRead = source.Read(rawData, 0, (int)dataSize);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": \"{EscapeJson(animName)}\",");
                sb.AppendLine($"  \"duration\": {duration.ToString(CultureInfo.InvariantCulture)},");
                sb.AppendLine($"  \"numFrames\": {numFrames},");
                sb.AppendLine($"  \"rateScale\": {rateScale.ToString(CultureInfo.InvariantCulture)},");
                sb.AppendLine($"  \"frameRate\": {(numFrames > 0 && duration > 0 ? (numFrames / duration).ToString("F2", CultureInfo.InvariantCulture) : "30.0")},");
                sb.AppendLine($"  \"dataSize\": {bytesRead},");
                sb.AppendLine($"  \"source\": \"{EscapeJson(entry.SourcePath ?? "")}\",");

                // Export metadata
                sb.AppendLine("  \"metadata\": {");
                if (entry.Metadata != null)
                {
                    var metaEntries = new List<string>();
                    foreach (var kvp in entry.Metadata)
                    {
                        metaEntries.Add($"    \"{EscapeJson(kvp.Key)}\": \"{EscapeJson(kvp.Value?.ToString() ?? "")}\"");
                    }
                    sb.AppendLine(string.Join(",\n", metaEntries));
                }
                sb.AppendLine("  },");

                // Raw data as base64 for potential further processing
                sb.AppendLine($"  \"rawDataBase64\": \"{Convert.ToBase64String(rawData, 0, Math.Min(bytesRead, 65536))}\"");

                if (bytesRead > 65536)
                {
                    sb.AppendLine($"  ,\"rawDataTruncated\": true");
                    sb.AppendLine($"  ,\"fullDataSize\": {bytesRead}");
                }

                sb.AppendLine("}");

                File.WriteAllText(outputPath, sb.ToString());

                return ExportResult.Succeeded(outputPath, bytesRead);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"AnimSequence export failed: {ex.Message}");
            }
        }

        #endregion

        #region Generic Raw Export

        /// <summary>
        /// Export unknown asset types as raw data with JSON metadata sidecar.
        /// </summary>
        public static ExportResult ExportRaw(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string rawPath = outputPath + ".bin";
                long dataSize = entry.Size > 0 ? entry.Size : source.Length - source.Position;
                byte[] data = new byte[dataSize];
                int bytesRead = source.Read(data, 0, (int)dataSize);

                File.WriteAllBytes(rawPath, data);
                WriteMetadataSidecar(entry, rawPath);

                return ExportResult.Succeeded(rawPath, bytesRead);
            }
            catch (Exception ex)
            {
                return ExportResult.Failed($"Raw export failed: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static void WriteMetadataSidecar(AssetEntry entry, string assetPath)
        {
            try
            {
                string jsonPath = Path.ChangeExtension(assetPath, ".meta.json");
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": \"{EscapeJson(entry.Name ?? "")}\",");
                sb.AppendLine($"  \"type\": \"{entry.Type}\",");
                sb.AppendLine($"  \"format\": \"{EscapeJson(entry.FormatName ?? "")}\",");
                sb.AppendLine($"  \"size\": {entry.Size},");
                sb.AppendLine($"  \"offset\": {entry.Offset},");
                sb.AppendLine($"  \"source\": \"{EscapeJson(entry.SourcePath ?? "")}\",");
                sb.AppendLine("  \"metadata\": {");

                if (entry.Metadata != null && entry.Metadata.Count > 0)
                {
                    var metaEntries = new List<string>();
                    foreach (var kvp in entry.Metadata)
                    {
                        metaEntries.Add($"    \"{EscapeJson(kvp.Key)}\": \"{EscapeJson(kvp.Value?.ToString() ?? "")}\"");
                    }
                    sb.AppendLine(string.Join(",\n", metaEntries));
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                File.WriteAllText(jsonPath, sb.ToString());
            }
            catch
            {
                // Metadata sidecar is optional - don't fail the export
            }
        }

        private static int GetMetadataInt(AssetEntry entry, string key, int defaultValue)
        {
            if (entry.Metadata != null && entry.Metadata.TryGetValue(key, out object value))
            {
                if (value is int intVal) return intVal;
                if (int.TryParse(value?.ToString(), out int parsed)) return parsed;
            }
            return defaultValue;
        }

        private static float GetMetadataFloat(AssetEntry entry, string key, float defaultValue)
        {
            if (entry.Metadata != null && entry.Metadata.TryGetValue(key, out object value))
            {
                if (value is float fVal) return fVal;
                if (value is double dVal) return (float)dVal;
                if (float.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    return parsed;
            }
            return defaultValue;
        }

        private static string GetMetadataString(AssetEntry entry, string key, string defaultValue)
        {
            if (entry.Metadata != null && entry.Metadata.TryGetValue(key, out object value))
                return value?.ToString() ?? defaultValue;
            return defaultValue;
        }

        private static bool GetMetadataBool(AssetEntry entry, string key, bool defaultValue)
        {
            if (entry.Metadata != null && entry.Metadata.TryGetValue(key, out object value))
            {
                if (value is bool bVal) return bVal;
                if (bool.TryParse(value?.ToString(), out bool parsed)) return parsed;
            }
            return defaultValue;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        #endregion
    }
}
