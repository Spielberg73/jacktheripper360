using System.Globalization;
using System.Text;

namespace JackTheRipper360.Core.Animation
{
    /// <summary>
    /// Exports animation data to common interchange formats.
    /// </summary>
    public static class AnimationExporter
    {
        /// <summary>
        /// Export animation clip to a JSON format suitable for import into game engines.
        /// </summary>
        public static string ExportToJSON(AnimationClipData clip)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"name\": \"{EscapeJson(clip.Name ?? "animation")}\",");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"duration\": {0:F4},", clip.Duration));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"frameRate\": {0:F1},", clip.FrameRate));
            sb.AppendLine($"  \"channels\": [");

            for (int c = 0; c < clip.Channels.Count; c++)
            {
                var channel = clip.Channels[c];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"boneName\": \"{EscapeJson(channel.BoneName)}\",");
                sb.AppendLine($"      \"boneIndex\": {channel.BoneIndex},");

                // Position keys
                sb.AppendLine("      \"positionKeys\": [");
                for (int k = 0; k < channel.PositionKeys.Count; k++)
                {
                    var key = channel.PositionKeys[k];
                    string comma = k < channel.PositionKeys.Count - 1 ? "," : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "        {{ \"t\": {0:F4}, \"x\": {1:F6}, \"y\": {2:F6}, \"z\": {3:F6} }}{4}",
                        key.Time, key.Value.X, key.Value.Y, key.Value.Z, comma));
                }
                sb.AppendLine("      ],");

                // Rotation keys
                sb.AppendLine("      \"rotationKeys\": [");
                for (int k = 0; k < channel.RotationKeys.Count; k++)
                {
                    var key = channel.RotationKeys[k];
                    string comma = k < channel.RotationKeys.Count - 1 ? "," : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "        {{ \"t\": {0:F4}, \"x\": {1:F6}, \"y\": {2:F6}, \"z\": {3:F6}, \"w\": {4:F6} }}{5}",
                        key.Time, key.Value.X, key.Value.Y, key.Value.Z, key.Value.W, comma));
                }
                sb.AppendLine("      ],");

                // Scale keys
                sb.AppendLine("      \"scaleKeys\": [");
                for (int k = 0; k < channel.ScaleKeys.Count; k++)
                {
                    var key = channel.ScaleKeys[k];
                    string comma = k < channel.ScaleKeys.Count - 1 ? "," : "";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "        {{ \"t\": {0:F4}, \"x\": {1:F6}, \"y\": {2:F6}, \"z\": {3:F6} }}{4}",
                        key.Time, key.Value.X, key.Value.Y, key.Value.Z, comma));
                }
                sb.AppendLine("      ]");

                string channelComma = c < clip.Channels.Count - 1 ? "," : "";
                sb.AppendLine($"    }}{channelComma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }
    }
}
