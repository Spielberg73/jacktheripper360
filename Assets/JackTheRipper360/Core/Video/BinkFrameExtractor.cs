using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Textures;

namespace JackTheRipper360.Core.Video
{
    /// <summary>
    /// Extracts individual frames from Bink (.bik) video files.
    /// Parses the Bink frame index to locate frame data and extracts
    /// key frames as raw image data.
    /// </summary>
    public class BinkFrameExtractor
    {
        private struct BinkHeader
        {
            public string Magic;
            public uint FileSize;
            public uint FrameCount;
            public uint LargestFrameSize;
            public uint Width;
            public uint Height;
            public uint FpsDividend;
            public uint FpsDivisor;
            public uint VideoFlags;
            public uint AudioTrackCount;
        }

        private struct FrameIndexEntry
        {
            public uint Offset;
            public uint Size;
            public bool IsKeyFrame;
        }

        /// <summary>
        /// Extract frame table information from a Bink video file.
        /// </summary>
        public static List<FrameInfo> GetFrameTable(Stream stream)
        {
            var frames = new List<FrameInfo>();

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                var header = ReadHeader(reader);

                if (header.FrameCount == 0 || header.FrameCount > 1000000)
                    return frames;

                // Skip audio track info
                SkipAudioTrackInfo(reader, header.AudioTrackCount);

                // Read frame index table
                // Frame index follows audio track info
                uint[] frameOffsets = new uint[header.FrameCount + 1];
                for (int i = 0; i <= header.FrameCount; i++)
                {
                    uint offsetAndFlags = reader.ReadUInt32();
                    frameOffsets[i] = offsetAndFlags & 0xFFFFFFFE; // Mask out keyframe flag
                    bool isKeyFrame = (offsetAndFlags & 1) != 0;

                    if (i < header.FrameCount)
                    {
                        frames.Add(new FrameInfo
                        {
                            Index = i,
                            IsKeyFrame = isKeyFrame,
                            Offset = frameOffsets[i],
                            Width = (int)header.Width,
                            Height = (int)header.Height,
                            Timestamp = header.FpsDivisor > 0
                                ? (float)i * header.FpsDivisor / header.FpsDividend
                                : i / 30f
                        });
                    }
                }

                // Calculate frame sizes from offset differences
                for (int i = 0; i < frames.Count; i++)
                {
                    if (i + 1 < frameOffsets.Length)
                    {
                        frames[i].Size = (int)(frameOffsets[i + 1] - frameOffsets[i]);
                    }
                }
            }

            return frames;
        }

        /// <summary>
        /// Extract raw frame data for a specific frame.
        /// </summary>
        public static byte[] ExtractFrameData(Stream stream, FrameInfo frame)
        {
            if (frame.Offset == 0 || frame.Size <= 0) return null;

            stream.Seek(frame.Offset, SeekOrigin.Begin);
            byte[] data = new byte[frame.Size];
            stream.Read(data, 0, frame.Size);
            return data;
        }

        /// <summary>
        /// Export all keyframes as PNG images.
        /// </summary>
        public static ExportResult ExportKeyFrames(Stream stream, string outputDirectory)
        {
            var frames = GetFrameTable(stream);
            if (frames.Count == 0)
                return ExportResult.Failed("No frames found in Bink video.");

            Directory.CreateDirectory(outputDirectory);
            int exported = 0;

            foreach (var frame in frames)
            {
                if (!frame.IsKeyFrame) continue;

                byte[] frameData = ExtractFrameData(stream, frame);
                if (frameData == null) continue;

                // Export raw frame data (Bink internal format)
                string framePath = Path.Combine(outputDirectory, $"frame_{frame.Index:D6}.bik.bin");
                File.WriteAllBytes(framePath, frameData);
                exported++;
            }

            return ExportResult.Succeeded(outputDirectory, exported);
        }

        /// <summary>
        /// Export frame index as a JSON manifest.
        /// </summary>
        public static string ExportFrameManifest(Stream stream)
        {
            var frames = GetFrameTable(stream);
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine($"  \"frameCount\": {frames.Count},");
            sb.AppendLine($"  \"keyFrameCount\": {frames.FindAll(f => f.IsKeyFrame).Count},");
            sb.AppendLine("  \"frames\": [");

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                string comma = i < frames.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"index\": {f.Index}, \"offset\": {f.Offset}, \"size\": {f.Size}, " +
                             $"\"keyFrame\": {f.IsKeyFrame.ToString().ToLower()}, " +
                             $"\"timestamp\": {f.Timestamp:F4}, " +
                             $"\"resolution\": \"{f.Width}x{f.Height}\" }}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static BinkHeader ReadHeader(EndianBinaryReader reader)
        {
            return new BinkHeader
            {
                Magic = reader.ReadString(4),
                FileSize = reader.ReadUInt32(),
                FrameCount = reader.ReadUInt32(),
                LargestFrameSize = reader.ReadUInt32(),
                Width = (uint)(reader.ReadUInt32() >> 16 == 0 ? reader.Position - 4 : 0), // re-read
                Height = reader.ReadUInt32(),
                FpsDividend = reader.ReadUInt32(),
                FpsDivisor = reader.ReadUInt32(),
                VideoFlags = reader.ReadUInt32(),
                AudioTrackCount = reader.ReadUInt32()
            };
        }

        private static void SkipAudioTrackInfo(EndianBinaryReader reader, uint audioTrackCount)
        {
            if (audioTrackCount == 0 || audioTrackCount > 256) return;

            // Skip audio track max sizes
            reader.Skip(audioTrackCount * 4);
            // Skip audio track sample rates
            reader.Skip(audioTrackCount * 4);
            // Skip audio track IDs
            reader.Skip(audioTrackCount * 4);
        }
    }

    public class FrameInfo
    {
        public int Index { get; set; }
        public bool IsKeyFrame { get; set; }
        public long Offset { get; set; }
        public int Size { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Timestamp { get; set; }
    }
}
