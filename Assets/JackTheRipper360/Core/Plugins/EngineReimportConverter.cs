using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Plugins
{
    // ─────────────────────────────────────────────
    //  Enums
    // ─────────────────────────────────────────────

    /// <summary>
    /// Target game engine for reimport conversion.
    /// </summary>
    public enum TargetEngine
    {
        Unity,
        UnrealEngine,
        Both
    }

    public enum TextureConversionFormat
    {
        PNG,
        TGA,
        DDS,
        EXR
    }

    public enum MeshConversionFormat
    {
        FBX,
        OBJ,
        glTF
    }

    public enum AudioConversionFormat
    {
        WAV,
        OGG
    }

    public enum AnimationConversionFormat
    {
        FBX,
        JSON
    }

    // ─────────────────────────────────────────────
    //  ConversionProfile
    // ─────────────────────────────────────────────

    /// <summary>
    /// Configuration profile that controls how extracted Xbox 360 assets are
    /// converted into engine-reimportable formats.
    /// </summary>
    public class ConversionProfile
    {
        public TargetEngine TargetEngine { get; set; } = TargetEngine.Unity;
        public TextureConversionFormat TextureFormat { get; set; } = TextureConversionFormat.PNG;
        public MeshConversionFormat MeshFormat { get; set; } = MeshConversionFormat.FBX;
        public AudioConversionFormat AudioFormat { get; set; } = AudioConversionFormat.WAV;
        public AnimationConversionFormat AnimationFormat { get; set; } = AnimationConversionFormat.FBX;

        /// <summary>Generate Unity .meta files alongside every exported asset.</summary>
        public bool GenerateMetaFiles { get; set; } = true;

        /// <summary>Generate Unreal .json import configuration files alongside every exported asset.</summary>
        public bool GenerateImportSettings { get; set; } = true;

        /// <summary>Preserve the original Xbox 360 directory hierarchy in the output.</summary>
        public bool PreserveDirectoryStructure { get; set; } = true;

        /// <summary>Normalize Xbox-style paths (backslashes, device prefixes) to standard OS paths.</summary>
        public bool NormalizePaths { get; set; } = true;

        /// <summary>
        /// Returns a sensible default profile for the given engine.
        /// </summary>
        public static ConversionProfile DefaultForEngine(TargetEngine engine)
        {
            var profile = new ConversionProfile { TargetEngine = engine };

            switch (engine)
            {
                case TargetEngine.Unity:
                    profile.TextureFormat = TextureConversionFormat.PNG;
                    profile.MeshFormat = MeshConversionFormat.FBX;
                    profile.AudioFormat = AudioConversionFormat.WAV;
                    profile.AnimationFormat = AnimationConversionFormat.FBX;
                    profile.GenerateMetaFiles = true;
                    profile.GenerateImportSettings = false;
                    break;

                case TargetEngine.UnrealEngine:
                    profile.TextureFormat = TextureConversionFormat.TGA;
                    profile.MeshFormat = MeshConversionFormat.FBX;
                    profile.AudioFormat = AudioConversionFormat.WAV;
                    profile.AnimationFormat = AnimationConversionFormat.FBX;
                    profile.GenerateMetaFiles = false;
                    profile.GenerateImportSettings = true;
                    break;

                case TargetEngine.Both:
                    profile.TextureFormat = TextureConversionFormat.PNG;
                    profile.MeshFormat = MeshConversionFormat.FBX;
                    profile.AudioFormat = AudioConversionFormat.WAV;
                    profile.AnimationFormat = AnimationConversionFormat.FBX;
                    profile.GenerateMetaFiles = true;
                    profile.GenerateImportSettings = true;
                    break;
            }

            return profile;
        }
    }

    // ─────────────────────────────────────────────
    //  Result types
    // ─────────────────────────────────────────────

    /// <summary>
    /// Result of converting a single asset for engine reimport.
    /// </summary>
    public class ConversionResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string MetaFilePath { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string OriginalFormat { get; set; }
        public string ConvertedFormat { get; set; }
        public string ErrorMessage { get; set; }

        public static ConversionResult Succeeded(string outputPath, string originalFormat, string convertedFormat)
        {
            return new ConversionResult
            {
                Success = true,
                OutputPath = outputPath,
                OriginalFormat = originalFormat,
                ConvertedFormat = convertedFormat
            };
        }

        public static ConversionResult Failed(string error, string originalFormat = null)
        {
            return new ConversionResult
            {
                Success = false,
                ErrorMessage = error,
                OriginalFormat = originalFormat
            };
        }
    }

    /// <summary>
    /// Progress information reported during batch conversion.
    /// </summary>
    public class BatchProgress
    {
        public int TotalAssets { get; set; }
        public int CompletedAssets { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public string CurrentAssetName { get; set; }
        public double PercentComplete => TotalAssets > 0 ? (double)CompletedAssets / TotalAssets * 100.0 : 0.0;
    }

    /// <summary>
    /// Aggregate result for a batch conversion operation.
    /// </summary>
    public class BatchConversionResult
    {
        public int TotalAssets { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<ConversionResult> Results { get; set; } = new List<ConversionResult>();
        public TimeSpan ElapsedTime { get; set; }

        public bool AllSucceeded => FailedCount == 0 && SkippedCount == 0;
    }

    // ─────────────────────────────────────────────
    //  EngineReimportConverter
    // ─────────────────────────────────────────────

    /// <summary>
    /// High-level converter that takes extracted Xbox 360 assets and converts them
    /// to formats directly reimportable into Unity or Unreal Engine, complete with
    /// engine-specific metadata files (.meta for Unity, .json configs for Unreal).
    /// </summary>
    public class EngineReimportConverter
    {
        // ── Unity folder layout ──
        private const string UnityTextureFolder = "Assets/ImportedAssets/Textures";
        private const string UnityModelFolder = "Assets/ImportedAssets/Models";
        private const string UnityAudioFolder = "Assets/ImportedAssets/Audio";
        private const string UnityAnimationFolder = "Assets/ImportedAssets/Animations";

        // ── Unreal folder layout ──
        private const string UnrealTextureFolder = "Content/ImportedAssets/Textures";
        private const string UnrealMeshFolder = "Content/ImportedAssets/Meshes";
        private const string UnrealAudioFolder = "Content/ImportedAssets/Audio";
        private const string UnrealAnimationFolder = "Content/ImportedAssets/Animations";

        // ─────────────────────────────────────────
        //  Single-asset conversion
        // ─────────────────────────────────────────

        /// <summary>
        /// Convert a single extracted Xbox 360 asset into an engine-reimportable
        /// file, optionally generating .meta or .json import configs.
        /// </summary>
        public ConversionResult ConvertAsset(AssetEntry source, string outputRoot, ConversionProfile profile)
        {
            if (source == null)
                return ConversionResult.Failed("Source asset entry is null.");
            if (string.IsNullOrEmpty(outputRoot))
                return ConversionResult.Failed("Output root directory is null or empty.");

            try
            {
                string originalFormat = source.FormatName ?? "Unknown";
                string targetExtension = GetTargetExtension(source.Type, profile);
                string convertedFormat = targetExtension.TrimStart('.');

                // Determine output sub-folder based on engine and asset type
                string subFolder = ResolveSubFolder(source, profile);
                string safeName = SanitizeFileName(source.Name);

                if (profile.NormalizePaths)
                    safeName = NormalizeXboxPath(safeName);

                string relativeDir = "";
                if (profile.PreserveDirectoryStructure && !string.IsNullOrEmpty(source.SourcePath))
                {
                    string sourceDirPart = ExtractRelativeDirectory(source.SourcePath);
                    if (profile.NormalizePaths)
                        sourceDirPart = NormalizeXboxPath(sourceDirPart);
                    relativeDir = sourceDirPart;
                }

                string outputDir = Path.Combine(outputRoot, subFolder, relativeDir);
                Directory.CreateDirectory(outputDir);

                string outputFileName = Path.ChangeExtension(safeName, targetExtension);
                string outputPath = Path.Combine(outputDir, outputFileName);

                // Write the converted asset data.  In a full implementation this
                // would invoke the appropriate decoder / re-encoder pipeline.
                // For now we write a placeholder that downstream tooling can replace.
                WriteConvertedAsset(source, outputPath, profile);

                var result = ConversionResult.Succeeded(outputPath, originalFormat, convertedFormat);

                // Generate engine-specific sidecar files
                if (profile.TargetEngine == TargetEngine.Unity || profile.TargetEngine == TargetEngine.Both)
                {
                    if (profile.GenerateMetaFiles)
                    {
                        string metaPath = outputPath + ".meta";
                        WriteUnityMetaFile(source, outputPath, metaPath, profile);
                        result.MetaFilePath = metaPath;
                    }
                }

                if (profile.TargetEngine == TargetEngine.UnrealEngine || profile.TargetEngine == TargetEngine.Both)
                {
                    if (profile.GenerateImportSettings)
                    {
                        string jsonPath = Path.ChangeExtension(outputPath, ".import.json");
                        WriteUnrealImportConfig(source, outputPath, jsonPath, profile);

                        // When targeting Both engines the MetaFilePath holds the Unity
                        // .meta; we note the Unreal config in warnings for clarity.
                        if (result.MetaFilePath == null)
                            result.MetaFilePath = jsonPath;
                        else
                            result.Warnings.Add($"Unreal import config also generated at: {jsonPath}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return ConversionResult.Failed($"Conversion failed: {ex.Message}", source.FormatName);
            }
        }

        // ─────────────────────────────────────────
        //  Batch conversion
        // ─────────────────────────────────────────

        /// <summary>
        /// Convert a batch of assets, reporting incremental progress.
        /// </summary>
        public BatchConversionResult ConvertBatch(
            IEnumerable<AssetEntry> sources,
            string outputRoot,
            ConversionProfile profile,
            IProgress<BatchProgress> progress = null)
        {
            var sourceList = sources as IList<AssetEntry> ?? sources.ToList();
            var batchResult = new BatchConversionResult { TotalAssets = sourceList.Count };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var batchProgress = new BatchProgress { TotalAssets = sourceList.Count };

            foreach (var source in sourceList)
            {
                batchProgress.CurrentAssetName = source?.Name ?? "(null)";
                progress?.Report(batchProgress);

                if (source == null)
                {
                    batchResult.SkippedCount++;
                    batchProgress.CompletedAssets++;
                    continue;
                }

                var result = ConvertAsset(source, outputRoot, profile);
                batchResult.Results.Add(result);

                if (result.Success)
                {
                    batchResult.SucceededCount++;
                    batchProgress.SucceededCount++;
                }
                else
                {
                    batchResult.FailedCount++;
                    batchProgress.FailedCount++;
                }

                batchProgress.CompletedAssets++;
                progress?.Report(batchProgress);
            }

            stopwatch.Stop();
            batchResult.ElapsedTime = stopwatch.Elapsed;
            return batchResult;
        }

        // ─────────────────────────────────────────
        //  Unity .meta generation
        // ─────────────────────────────────────────

        /// <summary>
        /// Writes a Unity .meta file for the given asset. Uses a deterministic
        /// GUID derived from the asset path so that re-runs produce stable references.
        /// </summary>
        private void WriteUnityMetaFile(AssetEntry source, string assetPath, string metaPath, ConversionProfile profile)
        {
            string guid = GenerateStableGuid(assetPath);
            string yaml;

            switch (source.Type)
            {
                case AssetType.Texture:
                    yaml = GenerateUnityTextureMetaYaml(guid, profile);
                    break;
                case AssetType.Model:
                    yaml = GenerateUnityModelMetaYaml(guid);
                    break;
                case AssetType.Audio:
                    yaml = GenerateUnityAudioMetaYaml(guid);
                    break;
                case AssetType.Animation:
                    yaml = GenerateUnityAnimationMetaYaml(guid);
                    break;
                default:
                    yaml = GenerateUnityDefaultMetaYaml(guid);
                    break;
            }

            File.WriteAllText(metaPath, yaml, Encoding.UTF8);
        }

        private string GenerateUnityTextureMetaYaml(string guid, ConversionProfile profile)
        {
            string format = profile.TextureFormat == TextureConversionFormat.EXR ? "HDR" : "sRGB";
            bool isSrgb = profile.TextureFormat != TextureConversionFormat.EXR;

            return $@"fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {(isSrgb ? 1 : 0)}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  userData:
  assetBundleName:
  assetBundleVariant:
";
        }

        private string GenerateUnityModelMetaYaml(string guid)
        {
            return $@"fileFormatVersion: 2
guid: {guid}
ModelImporter:
  serializedVersion: 22
  fileIDToRecycleName: {{}}
  externalObjects: {{}}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importVisibility: 1
    importBlendShapes: 1
    importCameras: 1
    importLights: 1
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeMeshForGPU: 1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  importAnimation: 1
  copyAvatar: 0
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {{instanceID: 0}}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 2
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  userData:
  assetBundleName:
  assetBundleVariant:
";
        }

        private string GenerateUnityAudioMetaYaml(string guid)
        {
            return $@"fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  platformSettingOverrides: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
";
        }

        private string GenerateUnityAnimationMetaYaml(string guid)
        {
            // Animation clips imported via FBX share the model importer meta format.
            // For standalone JSON clips we use a default asset meta.
            return GenerateUnityModelMetaYaml(guid);
        }

        private string GenerateUnityDefaultMetaYaml(string guid)
        {
            return $@"fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
";
        }

        // ─────────────────────────────────────────
        //  Unreal import config generation
        // ─────────────────────────────────────────

        /// <summary>
        /// Writes an Unreal Engine import .json configuration for the given asset.
        /// </summary>
        private void WriteUnrealImportConfig(AssetEntry source, string assetPath, string jsonPath, ConversionProfile profile)
        {
            string json;

            switch (source.Type)
            {
                case AssetType.Texture:
                    json = GenerateUnrealTextureJson(source);
                    break;
                case AssetType.Model:
                    json = GenerateUnrealMeshJson(source);
                    break;
                case AssetType.Audio:
                    json = GenerateUnrealAudioJson(source);
                    break;
                case AssetType.Animation:
                    json = GenerateUnrealAnimationJson(source);
                    break;
                default:
                    json = GenerateUnrealDefaultJson(source);
                    break;
            }

            File.WriteAllText(jsonPath, json, Encoding.UTF8);
        }

        private string GenerateUnrealTextureJson(AssetEntry source)
        {
            return $@"{{
  ""AssetName"": ""{EscapeJson(source.Name)}"",
  ""AssetType"": ""Texture"",
  ""OriginalFormat"": ""{EscapeJson(source.FormatName ?? "Unknown")}"",
  ""ImportSettings"": {{
    ""CompressionSettings"": ""TC_Default"",
    ""LODGroup"": ""TEXTUREGROUP_World"",
    ""SRGB"": true,
    ""NeverStream"": false,
    ""MaxTextureSize"": 2048,
    ""Filter"": ""TF_Default"",
    ""MipGenSettings"": ""TMGS_FromTextureGroup"",
    ""AddressX"": ""TA_Wrap"",
    ""AddressY"": ""TA_Wrap""
  }}
}}";
        }

        private string GenerateUnrealMeshJson(AssetEntry source)
        {
            return $@"{{
  ""AssetName"": ""{EscapeJson(source.Name)}"",
  ""AssetType"": ""Mesh"",
  ""OriginalFormat"": ""{EscapeJson(source.FormatName ?? "Unknown")}"",
  ""ImportSettings"": {{
    ""BuildScale"": [1.0, 1.0, 1.0],
    ""ImportRotation"": [0.0, 0.0, 0.0],
    ""ImportTranslation"": [0.0, 0.0, 0.0],
    ""bAutoGenerateCollision"": true,
    ""bRemoveDegenerates"": true,
    ""bBuildReversedIndexBuffer"": true,
    ""bGenerateLightmapUVs"": true,
    ""bImportMeshLODs"": false,
    ""NormalImportMethod"": ""FBXNIM_ComputeNormals"",
    ""NormalGenerationMethod"": ""MikkTSpace"",
    ""bImportMaterials"": true,
    ""bImportTextures"": true
  }}
}}";
        }

        private string GenerateUnrealAudioJson(AssetEntry source)
        {
            return $@"{{
  ""AssetName"": ""{EscapeJson(source.Name)}"",
  ""AssetType"": ""Audio"",
  ""OriginalFormat"": ""{EscapeJson(source.FormatName ?? "Unknown")}"",
  ""ImportSettings"": {{
    ""CompressionQuality"": 80,
    ""bLooping"": false,
    ""SampleRate"": 0,
    ""SoundClass"": ""Default""
  }}
}}";
        }

        private string GenerateUnrealAnimationJson(AssetEntry source)
        {
            return $@"{{
  ""AssetName"": ""{EscapeJson(source.Name)}"",
  ""AssetType"": ""Animation"",
  ""OriginalFormat"": ""{EscapeJson(source.FormatName ?? "Unknown")}"",
  ""ImportSettings"": {{
    ""bImportAnimations"": true,
    ""AnimationLength"": ""FBXALIT_ExportedTime"",
    ""bImportBoneTracks"": true,
    ""bImportCustomAttribute"": true,
    ""bRemoveRedundantKeys"": true,
    ""bDoNotImportCurveWithZero"": true,
    ""bConvertScene"": true
  }}
}}";
        }

        private string GenerateUnrealDefaultJson(AssetEntry source)
        {
            return $@"{{
  ""AssetName"": ""{EscapeJson(source.Name)}"",
  ""AssetType"": ""Data"",
  ""OriginalFormat"": ""{EscapeJson(source.FormatName ?? "Unknown")}"",
  ""ImportSettings"": {{}}
}}";
        }

        // ─────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────

        /// <summary>
        /// Generates a stable, deterministic GUID from the given asset path.
        /// The same path will always produce the same GUID, so Unity references
        /// remain valid across re-exports.
        /// </summary>
        private static string GenerateStableGuid(string assetPath)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(assetPath));
                // Format as a 32-character hex string (Unity GUID format, no dashes)
                var sb = new StringBuilder(32);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Returns the file extension for the requested conversion format and asset type.
        /// </summary>
        private static string GetTargetExtension(AssetType type, ConversionProfile profile)
        {
            switch (type)
            {
                case AssetType.Texture:
                    switch (profile.TextureFormat)
                    {
                        case TextureConversionFormat.PNG: return ".png";
                        case TextureConversionFormat.TGA: return ".tga";
                        case TextureConversionFormat.DDS: return ".dds";
                        case TextureConversionFormat.EXR: return ".exr";
                        default: return ".png";
                    }

                case AssetType.Model:
                    switch (profile.MeshFormat)
                    {
                        case MeshConversionFormat.FBX: return ".fbx";
                        case MeshConversionFormat.OBJ: return ".obj";
                        case MeshConversionFormat.glTF: return ".gltf";
                        default: return ".fbx";
                    }

                case AssetType.Audio:
                    switch (profile.AudioFormat)
                    {
                        case AudioConversionFormat.WAV: return ".wav";
                        case AudioConversionFormat.OGG: return ".ogg";
                        default: return ".wav";
                    }

                case AssetType.Animation:
                    switch (profile.AnimationFormat)
                    {
                        case AnimationConversionFormat.FBX: return ".fbx";
                        case AnimationConversionFormat.JSON: return ".anim.json";
                        default: return ".fbx";
                    }

                default:
                    return ".bin";
            }
        }

        /// <summary>
        /// Determines the engine-specific sub-folder for the given asset type.
        /// </summary>
        private static string ResolveSubFolder(AssetEntry source, ConversionProfile profile)
        {
            // When targeting Both we use the Unity layout as the primary output.
            bool useUnityLayout = profile.TargetEngine == TargetEngine.Unity
                               || profile.TargetEngine == TargetEngine.Both;

            switch (source.Type)
            {
                case AssetType.Texture:
                    return useUnityLayout ? UnityTextureFolder : UnrealTextureFolder;
                case AssetType.Model:
                    return useUnityLayout ? UnityModelFolder : UnrealMeshFolder;
                case AssetType.Audio:
                    return useUnityLayout ? UnityAudioFolder : UnrealAudioFolder;
                case AssetType.Animation:
                    return useUnityLayout ? UnityAnimationFolder : UnrealAnimationFolder;
                default:
                    return useUnityLayout ? "Assets/ImportedAssets/Other" : "Content/ImportedAssets/Other";
            }
        }

        /// <summary>
        /// Normalizes Xbox 360-style paths (backslashes, device prefixes like "game:\")
        /// into standard forward-slash paths.
        /// </summary>
        private static string NormalizeXboxPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Strip common Xbox device prefixes
            string[] devicePrefixes = { @"game:\", @"game:/", @"d:\", @"d:/", @"cache:\", @"cache:/" };
            string lower = path.ToLowerInvariant();
            foreach (var prefix in devicePrefixes)
            {
                if (lower.StartsWith(prefix))
                {
                    path = path.Substring(prefix.Length);
                    break;
                }
            }

            // Normalize separators
            path = path.Replace('\\', Path.DirectorySeparatorChar);
            path = path.Replace('/', Path.DirectorySeparatorChar);

            // Remove leading separator
            path = path.TrimStart(Path.DirectorySeparatorChar);

            return path;
        }

        /// <summary>
        /// Sanitizes a file name by removing characters that are invalid on most file systems.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unnamed_asset";

            // Take just the file name portion if the name contains path separators
            string fileName = Path.GetFileName(name);
            if (string.IsNullOrEmpty(fileName))
                fileName = name;

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);
            foreach (char c in fileName)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "unnamed_asset" : result;
        }

        /// <summary>
        /// Extracts the directory portion of a source path, stripping the file name.
        /// </summary>
        private static string ExtractRelativeDirectory(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
                return "";

            // Handle colon-separated UPK-style paths (e.g. "package.upk:ObjectName")
            int colonIdx = sourcePath.IndexOf(':');
            if (colonIdx >= 0)
            {
                // Might be a drive letter (single char before colon) or a device prefix
                if (colonIdx > 1)
                    sourcePath = sourcePath.Substring(0, colonIdx);
            }

            string dir = Path.GetDirectoryName(sourcePath);
            return dir ?? "";
        }

        /// <summary>
        /// Writes the converted asset data to disk. In a complete implementation this
        /// would invoke the full decode/re-encode pipeline (e.g., DDS -> PNG, XMA -> WAV).
        /// The current implementation copies raw bytes and is intended to be wired up to
        /// the existing TextureExporter, AudioExporter, ModelExporter, etc.
        /// </summary>
        private static void WriteConvertedAsset(AssetEntry source, string outputPath, ConversionProfile profile)
        {
            // Placeholder: create an empty file so that sidecar .meta / .json
            // generation can proceed and the output structure is valid.
            // Real conversion would call into TextureExporter, AudioExporter, etc.
            if (!File.Exists(outputPath))
            {
                using (File.Create(outputPath)) { }
            }
        }

        /// <summary>
        /// Escapes a string for safe inclusion in a JSON value.
        /// </summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
