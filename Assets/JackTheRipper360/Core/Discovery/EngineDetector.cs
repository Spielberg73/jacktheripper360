using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// Supported game engine types for detection.
    /// </summary>
    public enum EngineType
    {
        Unknown,
        UnrealEngine3,
        UnrealEngine4,
        UnrealEngine5,
        Unity,
        idTech2,
        idTech3,
        idTech4,
        idTech5,
        idTech6,
        idTech7,
        Source,
        Source2,
        CryEngine,
        Frostbite,
        Custom
    }

    /// <summary>
    /// Result of an engine detection pass, including confidence scoring and evidence trail.
    /// </summary>
    public struct EngineDetectionResult
    {
        public EngineType Engine;
        public string EngineName;
        public float Confidence;
        public string Version;
        public List<string> Evidence;
        public Dictionary<string, string> Properties;

        public override string ToString() =>
            $"{EngineName} (v{Version ?? "?"}) - Confidence: {Confidence:P0}";
    }

    /// <summary>
    /// Automatic game engine detector that identifies engines by file fingerprinting.
    /// Uses weighted scoring across file extensions, magic bytes, and known marker files
    /// to produce a ranked detection result with confidence levels.
    /// </summary>
    public static class EngineDetector
    {
        private const int MagicReadSize = 64;

        // Detection rule: maps a condition to an engine type and weight
        private struct DetectionRule
        {
            public EngineType Engine;
            public int Weight;
            public string Description;

            public DetectionRule(EngineType engine, int weight, string description)
            {
                Engine = engine;
                Weight = weight;
                Description = description;
            }
        }

        // Extension-based rules
        private static readonly Dictionary<string, DetectionRule[]> ExtensionRules = BuildExtensionRules();

        // Marker file rules (full file names or directory-relative paths)
        private static readonly Dictionary<string, DetectionRule> MarkerFileRules = BuildMarkerFileRules();

        /// <summary>
        /// Scans a directory tree for known engine file patterns and returns the best detection result.
        /// </summary>
        public static EngineDetectionResult DetectFromDirectory(string path)
        {
            if (!Directory.Exists(path))
                return CreateUnknownResult("Directory does not exist");

            var files = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Partial enumeration is acceptable
            }

            if (files.Count == 0)
                return CreateUnknownResult("No files found in directory");

            var relativeNames = files.Select(f => GetRelativePath(path, f)).ToList();
            var result = DetectFromFiles(relativeNames);

            // Attempt stream-based magic detection on a sample of candidate files
            var topEngine = result.Engine;
            if (topEngine != EngineType.Unknown)
            {
                foreach (string file in files.Take(500))
                {
                    try
                    {
                        using (var stream = File.OpenRead(file))
                        {
                            var streamResult = DetectFromStream(stream, Path.GetFileName(file));
                            if (streamResult.Engine != EngineType.Unknown)
                            {
                                foreach (string evidence in streamResult.Evidence)
                                {
                                    if (!result.Evidence.Contains(evidence))
                                        result.Evidence.Add(evidence);
                                }
                            }
                        }
                    }
                    catch (IOException)
                    {
                        // Skip unreadable files
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Analyzes a list of file names (or relative paths) to determine the most likely engine.
        /// </summary>
        public static EngineDetectionResult DetectFromFiles(IEnumerable<string> fileNames)
        {
            var scores = new Dictionary<EngineType, int>();
            var evidence = new Dictionary<EngineType, List<string>>();
            var properties = new Dictionary<EngineType, Dictionary<string, string>>();

            foreach (EngineType engine in Enum.GetValues(typeof(EngineType)))
            {
                if (engine == EngineType.Unknown) continue;
                scores[engine] = 0;
                evidence[engine] = new List<string>();
                properties[engine] = new Dictionary<string, string>();
            }

            foreach (string filePath in fileNames)
            {
                string fileName = Path.GetFileName(filePath).ToLowerInvariant();
                string ext = Path.GetExtension(fileName).ToLowerInvariant();

                // Check extension rules
                if (ExtensionRules.TryGetValue(ext, out DetectionRule[] rules))
                {
                    foreach (var rule in rules)
                    {
                        scores[rule.Engine] += rule.Weight;
                        evidence[rule.Engine].Add($"File extension '{ext}': {rule.Description}");
                    }
                }

                // Check marker file rules
                if (MarkerFileRules.TryGetValue(fileName, out DetectionRule markerRule))
                {
                    scores[markerRule.Engine] += markerRule.Weight;
                    evidence[markerRule.Engine].Add($"Marker file '{fileName}': {markerRule.Description}");
                }

                // Check partial path markers
                string lowerPath = filePath.ToLowerInvariant().Replace('\\', '/');
                CheckPathMarkers(lowerPath, scores, evidence, properties);
            }

            return BuildResult(scores, evidence, properties);
        }

        /// <summary>
        /// Checks a single file's magic bytes and name to identify its engine origin.
        /// </summary>
        public static EngineDetectionResult DetectFromStream(Stream stream, string fileName)
        {
            var scores = new Dictionary<EngineType, int>();
            var evidence = new Dictionary<EngineType, List<string>>();
            var properties = new Dictionary<EngineType, Dictionary<string, string>>();

            foreach (EngineType engine in Enum.GetValues(typeof(EngineType)))
            {
                if (engine == EngineType.Unknown) continue;
                scores[engine] = 0;
                evidence[engine] = new List<string>();
                properties[engine] = new Dictionary<string, string>();
            }

            // Read magic bytes
            byte[] magic = new byte[MagicReadSize];
            int bytesRead = 0;
            long originalPos = 0;

            if (stream.CanSeek)
            {
                originalPos = stream.Position;
                stream.Seek(0, SeekOrigin.Begin);
            }

            bytesRead = stream.Read(magic, 0, MagicReadSize);

            if (stream.CanSeek)
                stream.Seek(originalPos, SeekOrigin.Begin);

            if (bytesRead >= 4)
            {
                CheckMagicBytes(magic, bytesRead, stream, scores, evidence, properties);
            }

            // Also check extension
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ExtensionRules.TryGetValue(ext, out DetectionRule[] rules))
            {
                foreach (var rule in rules)
                {
                    scores[rule.Engine] += rule.Weight;
                    evidence[rule.Engine].Add($"File extension '{ext}': {rule.Description}");
                }
            }

            return BuildResult(scores, evidence, properties);
        }

        #region Magic Byte Detection

        private static void CheckMagicBytes(byte[] magic, int bytesRead, Stream stream,
            Dictionary<EngineType, int> scores,
            Dictionary<EngineType, List<string>> evidence,
            Dictionary<EngineType, Dictionary<string, string>> properties)
        {
            uint magic32 = (uint)(magic[0] | (magic[1] << 8) | (magic[2] << 16) | (magic[3] << 24));
            uint magic32Be = (uint)((magic[0] << 24) | (magic[1] << 16) | (magic[2] << 8) | magic[3]);
            string magic4 = Encoding.ASCII.GetString(magic, 0, Math.Min(4, bytesRead));

            // Unity: UnityFS magic
            if (bytesRead >= 7)
            {
                string magic7 = Encoding.ASCII.GetString(magic, 0, 7);
                if (magic7 == "UnityFS")
                {
                    scores[EngineType.Unity] += 5;
                    evidence[EngineType.Unity].Add("UnityFS magic bytes detected");
                    properties[EngineType.Unity]["BundleFormat"] = "UnityFS";
                }
            }

            // Unity: UnityWeb/UnityRaw
            if (bytesRead >= 8)
            {
                string magic8 = Encoding.ASCII.GetString(magic, 0, 8);
                if (magic8 == "UnityWeb" || magic8 == "UnityRaw")
                {
                    scores[EngineType.Unity] += 5;
                    evidence[EngineType.Unity].Add($"{magic8} magic bytes detected");
                    properties[EngineType.Unity]["BundleFormat"] = magic8;
                }
            }

            // Unreal Engine 3: package magic 0x9E2A83C1 (little-endian)
            if (magic32 == 0x9E2A83C1)
            {
                scores[EngineType.UnrealEngine3] += 5;
                evidence[EngineType.UnrealEngine3].Add("Unreal package magic 0x9E2A83C1 detected");
            }
            // Xbox 360 big-endian variant
            if (magic32Be == 0x9E2A83C1)
            {
                scores[EngineType.UnrealEngine3] += 5;
                evidence[EngineType.UnrealEngine3].Add("Unreal package magic 0x9E2A83C1 (big-endian) detected");
                properties[EngineType.UnrealEngine3]["Endian"] = "Big";
            }

            // id Tech 2: PACK magic
            if (magic4 == "PACK")
            {
                scores[EngineType.idTech2] += 5;
                evidence[EngineType.idTech2].Add("PACK archive magic detected");
            }

            // id Tech 2/3/4: IBSP magic with version discrimination
            if (magic4 == "IBSP" && bytesRead >= 8)
            {
                int bspVersion = magic[4] | (magic[5] << 8) | (magic[6] << 16) | (magic[7] << 24);
                switch (bspVersion)
                {
                    case 29:
                        scores[EngineType.idTech2] += 5;
                        evidence[EngineType.idTech2].Add($"IBSP v{bspVersion} (Quake II) detected");
                        properties[EngineType.idTech2]["BSPVersion"] = bspVersion.ToString();
                        break;
                    case 46:
                    case 47:
                        scores[EngineType.idTech3] += 5;
                        evidence[EngineType.idTech3].Add($"IBSP v{bspVersion} (Quake III) detected");
                        properties[EngineType.idTech3]["BSPVersion"] = bspVersion.ToString();
                        break;
                    case 4:
                        scores[EngineType.idTech4] += 5;
                        evidence[EngineType.idTech4].Add($"IBSP v{bspVersion} (Doom 3) detected");
                        properties[EngineType.idTech4]["BSPVersion"] = bspVersion.ToString();
                        break;
                }
            }

            // id Tech 4: RBSP magic
            if (magic4 == "RBSP")
            {
                scores[EngineType.idTech4] += 5;
                evidence[EngineType.idTech4].Add("RBSP magic detected (Quake 4 / Prey)");
            }

            // Source engine: VPK magic 0x55AA1234
            if (magic32 == 0x55AA1234)
            {
                scores[EngineType.Source] += 5;
                evidence[EngineType.Source].Add("VPK magic 0x55AA1234 detected");
                if (bytesRead >= 8)
                {
                    int vpkVersion = magic[4] | (magic[5] << 8) | (magic[6] << 16) | (magic[7] << 24);
                    properties[EngineType.Source]["VPKVersion"] = vpkVersion.ToString();
                }
            }

            // Source engine: VBSP magic
            if (magic4 == "VBSP")
            {
                scores[EngineType.Source] += 5;
                evidence[EngineType.Source].Add("VBSP map format detected");
            }

            // Source engine: IDST magic (StudioModel)
            if (magic4 == "IDST")
            {
                scores[EngineType.Source] += 4;
                evidence[EngineType.Source].Add("IDST StudioModel magic detected");
                if (bytesRead >= 8)
                {
                    int mdlVersion = magic[4] | (magic[5] << 8) | (magic[6] << 16) | (magic[7] << 24);
                    if (mdlVersion >= 44 && mdlVersion <= 49)
                    {
                        properties[EngineType.Source]["MDLVersion"] = mdlVersion.ToString();
                    }
                }
            }

            // Source engine: VTF magic
            if (magic4 == "VTF\0")
            {
                scores[EngineType.Source] += 5;
                evidence[EngineType.Source].Add("VTF texture magic detected");
            }

            // UE4/5: .pak footer detection requires seeking to end; only do header check
            // UE4 .uasset has a magic of 0xC1832A9E at start (reverse of UE3)
            if (magic32 == 0xC1832A9E)
            {
                scores[EngineType.UnrealEngine4] += 4;
                evidence[EngineType.UnrealEngine4].Add("Unreal Engine 4/5 asset magic detected");
            }
        }

        #endregion

        #region Path-Based Markers

        private static void CheckPathMarkers(string lowerPath,
            Dictionary<EngineType, int> scores,
            Dictionary<EngineType, List<string>> evidence,
            Dictionary<EngineType, Dictionary<string, string>> properties)
        {
            // Unity-specific directories/files
            if (lowerPath.Contains("/managed/assembly-csharp.dll"))
            {
                scores[EngineType.Unity] += 4;
                evidence[EngineType.Unity].Add("Assembly-CSharp.dll found (Unity managed assembly)");
            }
            if (lowerPath.Contains("/resources.assets"))
            {
                scores[EngineType.Unity] += 3;
                evidence[EngineType.Unity].Add("resources.assets found (Unity resources)");
            }

            // UE4/5: Content/Paks directory
            if (lowerPath.Contains("/content/paks/"))
            {
                scores[EngineType.UnrealEngine4] += 3;
                evidence[EngineType.UnrealEngine4].Add("Content/Paks directory structure detected");
            }

            // CryEngine: game.cfg marker
            if (lowerPath.EndsWith("/game.cfg"))
            {
                scores[EngineType.CryEngine] += 3;
                evidence[EngineType.CryEngine].Add("game.cfg found (CryEngine configuration)");
            }
        }

        #endregion

        #region Rule Builders

        private static Dictionary<string, DetectionRule[]> BuildExtensionRules()
        {
            return new Dictionary<string, DetectionRule[]>(StringComparer.OrdinalIgnoreCase)
            {
                // Unity
                [".assets"] = new[] { new DetectionRule(EngineType.Unity, 3, "Unity asset bundle") },
                [".unity3d"] = new[] { new DetectionRule(EngineType.Unity, 5, "Unity web player bundle") },

                // Unreal Engine 3
                [".upk"] = new[] { new DetectionRule(EngineType.UnrealEngine3, 4, "Unreal package") },
                [".u"] = new[] { new DetectionRule(EngineType.UnrealEngine3, 3, "Unreal script package") },
                [".umap"] = new[] { new DetectionRule(EngineType.UnrealEngine3, 4, "Unreal map file") },

                // Unreal Engine 4/5
                [".pak"] = new[]
                {
                    new DetectionRule(EngineType.UnrealEngine4, 3, "Potential UE4/5 pak archive"),
                    new DetectionRule(EngineType.idTech2, 2, "Potential id Tech 2 pak archive")
                },
                [".uasset"] = new[] { new DetectionRule(EngineType.UnrealEngine4, 5, "Unreal Engine 4/5 asset") },
                [".uexp"] = new[] { new DetectionRule(EngineType.UnrealEngine4, 4, "Unreal Engine 4/5 export data") },
                [".ubulk"] = new[] { new DetectionRule(EngineType.UnrealEngine4, 4, "Unreal Engine 4/5 bulk data") },

                // id Tech 2
                [".wad"] = new[] { new DetectionRule(EngineType.idTech2, 2, "WAD archive") },
                [".mdl"] = new[]
                {
                    new DetectionRule(EngineType.idTech2, 2, "Quake model"),
                    new DetectionRule(EngineType.Source, 3, "Source StudioModel")
                },
                [".lmp"] = new[] { new DetectionRule(EngineType.idTech2, 2, "Lump data file") },
                [".bsp"] = new[]
                {
                    new DetectionRule(EngineType.idTech2, 2, "BSP level (generic)"),
                    new DetectionRule(EngineType.idTech3, 2, "BSP level (generic)"),
                    new DetectionRule(EngineType.Source, 2, "BSP level (generic)")
                },

                // id Tech 3
                [".pk3"] = new[] { new DetectionRule(EngineType.idTech3, 5, "Quake III pk3 archive") },
                [".md3"] = new[] { new DetectionRule(EngineType.idTech3, 4, "Quake III model") },
                [".shader"] = new[] { new DetectionRule(EngineType.idTech3, 3, "Quake III shader script") },

                // id Tech 4
                [".pk4"] = new[] { new DetectionRule(EngineType.idTech4, 5, "Doom 3 pk4 archive") },
                [".md5mesh"] = new[] { new DetectionRule(EngineType.idTech4, 5, "id Tech 4 skeletal mesh") },
                [".md5anim"] = new[] { new DetectionRule(EngineType.idTech4, 5, "id Tech 4 skeletal animation") },
                [".proc"] = new[] { new DetectionRule(EngineType.idTech4, 3, "id Tech 4 precompiled map") },
                [".mtr"] = new[] { new DetectionRule(EngineType.idTech4, 4, "id Tech 4 material definition") },

                // id Tech 5/6/7
                [".resources"] = new[] { new DetectionRule(EngineType.idTech5, 3, "id Tech 5+ resource container") },
                [".bimage"] = new[] { new DetectionRule(EngineType.idTech5, 4, "id Tech 5+ binary image") },
                [".bmodel"] = new[] { new DetectionRule(EngineType.idTech5, 4, "id Tech 5+ binary model") },
                [".mega"] = new[] { new DetectionRule(EngineType.idTech5, 5, "id Tech 6/7 mega-texture") },

                // Source
                [".vpk"] = new[] { new DetectionRule(EngineType.Source, 4, "Source VPK archive") },
                [".vtf"] = new[] { new DetectionRule(EngineType.Source, 5, "Source VTF texture") },
                [".vmt"] = new[] { new DetectionRule(EngineType.Source, 4, "Source VMT material") },

                // CryEngine
                [".cgf"] = new[] { new DetectionRule(EngineType.CryEngine, 3, "CryEngine geometry") },
                [".chr"] = new[] { new DetectionRule(EngineType.CryEngine, 3, "CryEngine character model") },
                [".cga"] = new[] { new DetectionRule(EngineType.CryEngine, 3, "CryEngine animated geometry") },
                [".cry"] = new[] { new DetectionRule(EngineType.CryEngine, 4, "CryEngine resource") },
                [".caf"] = new[] { new DetectionRule(EngineType.CryEngine, 3, "CryEngine animation file") },

                // Frostbite
                [".cas"] = new[] { new DetectionRule(EngineType.Frostbite, 4, "Frostbite CAS archive") },
                [".cat"] = new[] { new DetectionRule(EngineType.Frostbite, 3, "Frostbite catalog") },
                [".toc"] = new[] { new DetectionRule(EngineType.Frostbite, 3, "Frostbite table of contents") },
                [".sb"] = new[] { new DetectionRule(EngineType.Frostbite, 4, "Frostbite superbundle") },
                [".res"] = new[] { new DetectionRule(EngineType.Frostbite, 3, "Frostbite resource") },
                [".dbx"] = new[] { new DetectionRule(EngineType.Frostbite, 5, "Frostbite data block") },
            };
        }

        private static Dictionary<string, DetectionRule> BuildMarkerFileRules()
        {
            return new Dictionary<string, DetectionRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["globalgamemanagers"] = new DetectionRule(EngineType.Unity, 5, "Unity global game managers"),
                ["globalgamemanagers.assets"] = new DetectionRule(EngineType.Unity, 5, "Unity global game managers assets"),
                ["assembly-csharp.dll"] = new DetectionRule(EngineType.Unity, 4, "Unity managed assembly"),
                ["level0"] = new DetectionRule(EngineType.Unity, 3, "Unity level data"),
                ["sharedassets0.assets"] = new DetectionRule(EngineType.Unity, 4, "Unity shared assets"),
                ["gameinfo.txt"] = new DetectionRule(EngineType.Source, 5, "Source engine game info"),
                ["game.cfg"] = new DetectionRule(EngineType.CryEngine, 3, "CryEngine game config"),
            };
        }

        #endregion

        #region Result Building

        private static EngineDetectionResult BuildResult(
            Dictionary<EngineType, int> scores,
            Dictionary<EngineType, List<string>> evidence,
            Dictionary<EngineType, Dictionary<string, string>> properties)
        {
            // Find the engine with the highest score
            int maxScore = 0;
            EngineType bestEngine = EngineType.Unknown;

            foreach (var kvp in scores)
            {
                if (kvp.Value > maxScore)
                {
                    maxScore = kvp.Value;
                    bestEngine = kvp.Key;
                }
            }

            if (maxScore == 0)
                return CreateUnknownResult("No engine-specific files detected");

            // Calculate total score across all engines for normalization
            int totalScore = scores.Values.Sum();

            // Confidence is the proportion of evidence pointing to the winning engine,
            // scaled so that even a clear winner with few files doesn't exceed 1.0
            float rawConfidence = (float)maxScore / totalScore;
            float scaledConfidence = Math.Min(1.0f, rawConfidence * Math.Min(1.0f, maxScore / 10.0f) + rawConfidence);
            scaledConfidence = Math.Min(1.0f, scaledConfidence);

            // Deduplicate evidence strings
            var uniqueEvidence = evidence[bestEngine]
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string version = DetermineVersion(bestEngine, properties[bestEngine]);

            return new EngineDetectionResult
            {
                Engine = bestEngine,
                EngineName = GetEngineName(bestEngine),
                Confidence = scaledConfidence,
                Version = version,
                Evidence = uniqueEvidence,
                Properties = properties[bestEngine]
            };
        }

        private static EngineDetectionResult CreateUnknownResult(string reason)
        {
            return new EngineDetectionResult
            {
                Engine = EngineType.Unknown,
                EngineName = "Unknown",
                Confidence = 0f,
                Version = null,
                Evidence = new List<string> { reason },
                Properties = new Dictionary<string, string>()
            };
        }

        private static string GetEngineName(EngineType engine)
        {
            switch (engine)
            {
                case EngineType.UnrealEngine3: return "Unreal Engine 3";
                case EngineType.UnrealEngine4: return "Unreal Engine 4";
                case EngineType.UnrealEngine5: return "Unreal Engine 5";
                case EngineType.Unity: return "Unity";
                case EngineType.idTech2: return "id Tech 2";
                case EngineType.idTech3: return "id Tech 3";
                case EngineType.idTech4: return "id Tech 4";
                case EngineType.idTech5: return "id Tech 5";
                case EngineType.idTech6: return "id Tech 6";
                case EngineType.idTech7: return "id Tech 7";
                case EngineType.Source: return "Source Engine";
                case EngineType.Source2: return "Source 2";
                case EngineType.CryEngine: return "CryEngine";
                case EngineType.Frostbite: return "Frostbite";
                case EngineType.Custom: return "Custom Engine";
                default: return "Unknown";
            }
        }

        private static string DetermineVersion(EngineType engine, Dictionary<string, string> properties)
        {
            switch (engine)
            {
                case EngineType.idTech2:
                    if (properties.TryGetValue("BSPVersion", out string bsp2Ver))
                        return bsp2Ver == "29" ? "Quake II" : "Quake";
                    return null;

                case EngineType.idTech3:
                    if (properties.TryGetValue("BSPVersion", out string bsp3Ver))
                        return bsp3Ver == "46" ? "Quake III Arena" : "Return to Castle Wolfenstein";
                    return null;

                case EngineType.idTech4:
                    if (properties.TryGetValue("BSPVersion", out string bsp4Ver))
                        return bsp4Ver == "4" ? "Doom 3" : "Quake 4";
                    return null;

                case EngineType.Source:
                    if (properties.TryGetValue("VPKVersion", out string vpkVer))
                        return $"VPK v{vpkVer}";
                    if (properties.TryGetValue("MDLVersion", out string mdlVer))
                        return $"MDL v{mdlVer}";
                    return null;

                default:
                    return null;
            }
        }

        #endregion

        #region Utility

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length);

            return fullPath;
        }

        #endregion
    }
}
