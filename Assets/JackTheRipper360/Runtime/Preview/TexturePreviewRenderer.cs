#if UNITY_EDITOR || UNITY_STANDALONE
using System.IO;
using UnityEngine;
using JackTheRipper360.Core.Common;
using CoreTextureFormat = JackTheRipper360.Core.Textures.TextureFormat;
using JackTheRipper360.Core.Textures;

namespace JackTheRipper360.Runtime.Preview
{
    /// <summary>
    /// Converts decoded texture data into Unity Texture2D for preview display.
    /// </summary>
    public static class TexturePreviewRenderer
    {
        /// <summary>
        /// Create a Unity Texture2D from an AssetEntry with texture metadata.
        /// </summary>
        public static Texture2D CreatePreview(AssetEntry entry, Stream source)
        {
            if (!entry.Metadata.ContainsKey("Width") || !entry.Metadata.ContainsKey("Height"))
                return null;

            int width = (int)entry.Metadata["Width"];
            int height = (int)entry.Metadata["Height"];
            CoreTextureFormat format = (CoreTextureFormat)entry.Metadata["Format"];
            int dataOffset = entry.Metadata.ContainsKey("DataOffset") ? (int)entry.Metadata["DataOffset"] : 0;

            if (width <= 0 || height <= 0) return null;

            source.Seek(dataOffset, SeekOrigin.Begin);
            var formatInfo = TextureFormatInfo.Get(format);
            int dataSize = formatInfo.CalculateDataSize(width, height);
            byte[] textureData = new byte[dataSize];
            source.Read(textureData, 0, dataSize);

            byte[] untiledData = Xbox360TextureDecoder.Untile(textureData, width, height, formatInfo);
            byte[] rgbaData = Xbox360TextureDecoder.DecodeToRGBA(untiledData, width, height, format);

            return CreateTexture2D(rgbaData, width, height);
        }

        /// <summary>
        /// Create a Texture2D from raw RGBA pixel data.
        /// </summary>
        public static Texture2D CreateTexture2D(byte[] rgbaData, int width, int height)
        {
            var texture = new Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);

            Color32[] colors = new Color32[width * height];
            for (int i = 0; i < colors.Length && i * 4 + 3 < rgbaData.Length; i++)
            {
                colors[i] = new Color32(
                    rgbaData[i * 4],
                    rgbaData[i * 4 + 1],
                    rgbaData[i * 4 + 2],
                    rgbaData[i * 4 + 3]
                );
            }

            // Flip vertically (Unity textures are bottom-up)
            Color32[] flipped = new Color32[colors.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    flipped[(height - 1 - y) * width + x] = colors[y * width + x];
                }
            }

            texture.SetPixels32(flipped);
            texture.Apply();
            return texture;
        }
    }
}
#endif
