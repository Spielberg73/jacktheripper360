using System;
using System.IO;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Models;

namespace JackTheRipper360.Core.Animation
{
    /// <summary>
    /// Parser for animation data from Xbox 360 game formats.
    /// Supports common animation container structures.
    /// </summary>
    public class AnimationParser : IAssetParser
    {
        public AssetType Type => AssetType.Animation;

        public bool CanParse(Stream stream, string fileName)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext == ".anim" || ext == ".anm" || ext == ".xan" || ext == ".motion";
        }

        public AssetEntry Parse(Stream stream, string fileName)
        {
            var entry = new AssetEntry(fileName, AssetType.Animation)
            {
                SourcePath = fileName,
                Size = stream.Length,
                FormatName = "Xbox 360 Animation"
            };

            try
            {
                using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
                {
                    var clip = TryParseAnimation(reader, stream.Length);
                    if (clip != null)
                    {
                        entry.Metadata["Duration"] = clip.Duration;
                        entry.Metadata["FrameRate"] = clip.FrameRate;
                        entry.Metadata["ChannelCount"] = clip.Channels.Count;
                        entry.Metadata["ClipName"] = clip.Name;
                    }
                }
            }
            catch { }

            return entry;
        }

        public AnimationClipData TryParseAnimation(EndianBinaryReader reader, long streamLength)
        {
            try
            {
                // Common animation header: bone count, frame count, frame rate
                uint boneCount = reader.ReadUInt32();
                uint frameCount = reader.ReadUInt32();
                float frameRate = reader.ReadSingle();

                if (boneCount == 0 || boneCount > 1000) return null;
                if (frameCount == 0 || frameCount > 100000) return null;
                if (frameRate <= 0 || frameRate > 120) return null;

                var clip = new AnimationClipData
                {
                    Name = "animation",
                    Duration = frameCount / frameRate,
                    FrameRate = frameRate
                };

                for (int bone = 0; bone < boneCount; bone++)
                {
                    var channel = new BoneChannel
                    {
                        BoneIndex = bone,
                        BoneName = $"bone_{bone:D3}"
                    };

                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        if (reader.Position + 28 > streamLength) break;

                        float time = frame / frameRate;

                        // Position (3 floats)
                        float px = reader.ReadSingle();
                        float py = reader.ReadSingle();
                        float pz = reader.ReadSingle();
                        channel.PositionKeys.Add(new Keyframe<Vector3f>(time, new Vector3f(px, py, pz)));

                        // Rotation as quaternion (4 floats)
                        float rx = reader.ReadSingle();
                        float ry = reader.ReadSingle();
                        float rz = reader.ReadSingle();
                        float rw = reader.ReadSingle();
                        channel.RotationKeys.Add(new Keyframe<Vector4f>(time, new Vector4f(rx, ry, rz, rw)));
                    }

                    clip.Channels.Add(channel);
                }

                return clip;
            }
            catch
            {
                return null;
            }
        }

        public ExportResult Export(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            using (var reader = new EndianBinaryReader(source, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);
                var clip = TryParseAnimation(reader, source.Length);

                if (clip == null || clip.Channels.Count == 0)
                    return ExportResult.Failed("Could not parse animation data.");

                string jsonPath = Path.ChangeExtension(outputPath, ".anim.json");
                string json = AnimationExporter.ExportToJSON(clip);
                File.WriteAllText(jsonPath, json);
                return ExportResult.Succeeded(jsonPath, json.Length);
            }
        }
    }
}
