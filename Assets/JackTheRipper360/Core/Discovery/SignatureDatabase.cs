using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// Expandable format signature database that can be loaded from JSON files.
    /// Users can add custom signatures for game-specific formats.
    /// </summary>
    public class SignatureDatabase
    {
        private readonly List<FormatSignature> _signatures = new List<FormatSignature>();

        public IReadOnlyList<FormatSignature> Signatures => _signatures.AsReadOnly();

        /// <summary>
        /// Load signatures from a JSON file.
        /// </summary>
        public void LoadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;

            string json = File.ReadAllText(jsonPath);
            ParseSignaturesJson(json);
        }

        /// <summary>
        /// Load signatures from a JSON string.
        /// </summary>
        public void LoadFromJsonString(string json)
        {
            ParseSignaturesJson(json);
        }

        /// <summary>
        /// Add a custom signature at runtime.
        /// </summary>
        public void AddSignature(FormatSignature signature)
        {
            _signatures.Add(signature);
        }

        /// <summary>
        /// Remove a signature by name.
        /// </summary>
        public bool RemoveSignature(string name)
        {
            return _signatures.RemoveAll(s => s.Name == name) > 0;
        }

        /// <summary>
        /// Try to match a stream against all known signatures.
        /// </summary>
        public FormatSignature Match(Stream stream)
        {
            FormatSignature bestMatch = null;
            float bestConfidence = 0;

            foreach (var sig in _signatures)
            {
                if (sig.Offset + sig.MagicBytes.Length > stream.Length)
                    continue;

                stream.Seek(sig.Offset, SeekOrigin.Begin);
                byte[] data = new byte[sig.MagicBytes.Length];
                int bytesRead = stream.Read(data, 0, data.Length);

                if (bytesRead < sig.MagicBytes.Length)
                    continue;

                bool matches = true;
                for (int i = 0; i < sig.MagicBytes.Length; i++)
                {
                    if (sig.Mask != null && i < sig.Mask.Length && sig.Mask[i] == 0x00)
                        continue; // Wildcard byte

                    if (data[i] != sig.MagicBytes[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches && sig.Confidence > bestConfidence)
                {
                    bestMatch = sig;
                    bestConfidence = sig.Confidence;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Save all signatures to a JSON file.
        /// </summary>
        public void SaveToJson(string jsonPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"signatures\": [");

            for (int i = 0; i < _signatures.Count; i++)
            {
                var sig = _signatures[i];
                string magicHex = BitConverter.ToString(sig.MagicBytes).Replace("-", "");
                string comma = i < _signatures.Count - 1 ? "," : "";

                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": \"{sig.Name}\",");
                sb.AppendLine($"      \"magic\": \"{magicHex}\",");
                sb.AppendLine($"      \"offset\": {sig.Offset},");
                sb.AppendLine($"      \"type\": \"{sig.Type}\",");
                sb.AppendLine($"      \"description\": \"{sig.Description}\",");
                sb.AppendLine($"      \"confidence\": {sig.Confidence:F2}");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(jsonPath, sb.ToString());
        }

        private void ParseSignaturesJson(string json)
        {
            // Find signatures array
            int sigArrayStart = json.IndexOf("\"signatures\"");
            if (sigArrayStart < 0) return;

            int arrayStart = json.IndexOf('[', sigArrayStart);
            if (arrayStart < 0) return;

            // Parse individual signature objects
            int pos = arrayStart + 1;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;

                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string objJson = json.Substring(objStart, objEnd - objStart + 1);

                var sig = ParseSingleSignature(objJson);
                if (sig != null)
                    _signatures.Add(sig);

                pos = objEnd + 1;

                // Check for end of array
                int nextBracket = json.IndexOfAny(new[] { '{', ']' }, pos);
                if (nextBracket < 0 || json[nextBracket] == ']')
                    break;
            }
        }

        private FormatSignature ParseSingleSignature(string json)
        {
            string name = ExtractJsonString(json, "name");
            string magicHex = ExtractJsonString(json, "magic");
            string typeStr = ExtractJsonString(json, "type");
            string description = ExtractJsonString(json, "description");
            long offset = ExtractJsonLong(json, "offset");
            float confidence = ExtractJsonFloat(json, "confidence", 0.9f);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(magicHex))
                return null;

            byte[] magicBytes = HexStringToBytes(magicHex);
            if (magicBytes == null) return null;

            AssetType type = AssetType.Unknown;
            if (Enum.TryParse(typeStr, true, out AssetType parsed))
                type = parsed;

            return new FormatSignature
            {
                Name = name,
                MagicBytes = magicBytes,
                Offset = offset,
                Type = type,
                Description = description ?? "",
                Confidence = confidence
            };
        }

        private static string ExtractJsonString(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int startQuote = json.IndexOf('"', colon + 1);
            if (startQuote < 0) return null;
            int endQuote = json.IndexOf('"', startQuote + 1);
            if (endQuote < 0) return null;
            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        private static long ExtractJsonLong(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return 0;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return 0;
            string rest = json.Substring(colon + 1).Trim();
            string numStr = "";
            foreach (char c in rest)
            {
                if (char.IsDigit(c) || c == '-') numStr += c;
                else break;
            }
            return long.TryParse(numStr, out long val) ? val : 0;
        }

        private static float ExtractJsonFloat(string json, string key, float defaultValue)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return defaultValue;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return defaultValue;
            string rest = json.Substring(colon + 1).Trim();
            string numStr = "";
            foreach (char c in rest)
            {
                if (char.IsDigit(c) || c == '.' || c == '-') numStr += c;
                else break;
            }
            return float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val) ? val : defaultValue;
        }

        private static byte[] HexStringToBytes(string hex)
        {
            if (hex.Length % 2 != 0) return null;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return null;
            }
            return bytes;
        }
    }

    public class FormatSignature
    {
        public string Name { get; set; }
        public byte[] MagicBytes { get; set; }
        public byte[] Mask { get; set; } // Optional: wildcard mask (0x00 = wildcard)
        public long Offset { get; set; }
        public AssetType Type { get; set; }
        public string Description { get; set; }
        public float Confidence { get; set; } = 0.9f;
    }
}
