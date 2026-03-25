#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Editor.Styles;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Right panel: detailed metadata, format information, and export options.
    /// </summary>
    public class InspectorPanel
    {
        private Vector2 _scrollPosition;
        private bool _showMetadata = true;
        private bool _showExportOptions = true;
        private bool _showHexPreview;
        private string _exportFormat = "Auto";

        private readonly string[] _textureExportFormats = { "Auto", "PNG", "TGA", "DDS (raw)" };
        private readonly string[] _audioExportFormats = { "Auto", "WAV", "OGG", "Raw" };
        private readonly string[] _modelExportFormats = { "Auto", "OBJ", "FBX (ASCII)" };
        private readonly string[] _videoExportFormats = { "Auto", "Raw" };
        private readonly string[] _animationExportFormats = { "Auto", "JSON", "FBX (ASCII)" };
        private int _formatIndex;

        // Engine detection
        private bool _showEngineDetection;
        private EngineDetectionResult _cachedDetection;
        private string _lastDetectionPath;

        // Engine reimport options
        private bool _showReimportOptions;
        private int _targetEngineIndex; // 0=None, 1=Unity, 2=Unreal, 3=Both
        private readonly string[] _targetEngines = { "None", "Unity", "Unreal Engine", "Both" };
        private bool _generateMetaFiles = true;
        private bool _generateImportSettings = true;
        private bool _preserveStructure = true;

        public void Draw(AssetEntry entry)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            {
                EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);

                if (entry == null)
                {
                    EditorGUILayout.LabelField("No asset selected", EditorStyles.centeredGreyMiniLabel);
                    EditorGUILayout.EndVertical();
                    return;
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    // Basic info
                    var color = JackTheRipperStyles.GetAssetTypeColor(entry.Type);
                    string icon = JackTheRipperStyles.GetAssetTypeIcon(entry.Type);

                    GUI.color = color;
                    EditorGUILayout.LabelField($"{icon} {entry.Name}", EditorStyles.boldLabel);
                    GUI.color = Color.white;

                    EditorGUILayout.LabelField("Type", entry.Type.ToString());
                    EditorGUILayout.LabelField("Format", entry.FormatName ?? "Unknown");
                    EditorGUILayout.LabelField("Size", FormatSize(entry.Size));

                    if (!string.IsNullOrEmpty(entry.SourcePath))
                        EditorGUILayout.LabelField("Source", entry.SourcePath, EditorStyles.wordWrappedLabel);

                    if (entry.Offset > 0)
                        EditorGUILayout.LabelField("Offset", $"0x{entry.Offset:X8}");

                    GUILayout.Space(10);

                    // Metadata section
                    _showMetadata = EditorGUILayout.Foldout(_showMetadata, "Metadata", true);
                    if (_showMetadata && entry.Metadata != null)
                    {
                        EditorGUI.indentLevel++;
                        foreach (var kvp in entry.Metadata)
                        {
                            string valueStr = kvp.Value?.ToString() ?? "null";
                            EditorGUILayout.LabelField(kvp.Key, valueStr);
                        }
                        EditorGUI.indentLevel--;

                        if (entry.Metadata.Count == 0)
                            EditorGUILayout.LabelField("  (no metadata)");
                    }

                    GUILayout.Space(10);

                    // Export options section
                    _showExportOptions = EditorGUILayout.Foldout(_showExportOptions, "Export Options", true);
                    if (_showExportOptions)
                    {
                        EditorGUI.indentLevel++;

                        string[] formatOptions = GetFormatOptionsForType(entry.Type);
                        _formatIndex = EditorGUILayout.Popup("Format", _formatIndex, formatOptions);

                        EditorGUI.indentLevel--;

                        GUILayout.Space(5);

                        if (GUILayout.Button("Export This Asset", GUILayout.Height(25)))
                        {
                            string ext = GetDefaultExtension(entry.Type);
                            string outputPath = EditorUtility.SaveFilePanel(
                                "Export Asset", "", entry.Name, ext);

                            if (!string.IsNullOrEmpty(outputPath))
                            {
                                var exportService = new Runtime.Services.ExportService();
                                var result = exportService.ExportAsset(entry, outputPath, new ExportOptions
                                {
                                    OutputDirectory = System.IO.Path.GetDirectoryName(outputPath),
                                    PreferredFormat = formatOptions[_formatIndex]
                                });

                                if (result.Success)
                                    EditorUtility.DisplayDialog("Export Complete",
                                        $"Exported to:\n{result.OutputPath}\n({result.BytesWritten} bytes)", "OK");
                                else
                                    EditorUtility.DisplayDialog("Export Failed", result.ErrorMessage, "OK");
                            }
                        }
                    }

                    GUILayout.Space(10);

                    // Engine detection section
                    _showEngineDetection = EditorGUILayout.Foldout(_showEngineDetection, "Engine Detection", true);
                    if (_showEngineDetection)
                    {
                        EditorGUI.indentLevel++;

                        if (GUILayout.Button("Detect Game Engine", GUILayout.Height(22)))
                        {
                            string sourcePath = entry.SourcePath;
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                if (sourcePath.Contains(":"))
                                    sourcePath = sourcePath.Split(':')[0];

                                string dir = System.IO.File.Exists(sourcePath)
                                    ? System.IO.Path.GetDirectoryName(sourcePath)
                                    : sourcePath;

                                _cachedDetection = EngineDetector.DetectFromDirectory(dir);
                                _lastDetectionPath = dir;
                            }
                        }

                        if (_cachedDetection.Engine != EngineType.Unknown)
                        {
                            EditorGUILayout.LabelField("Engine", _cachedDetection.EngineName, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField("Confidence", $"{_cachedDetection.Confidence:P0}");
                            if (!string.IsNullOrEmpty(_cachedDetection.Version))
                                EditorGUILayout.LabelField("Version", _cachedDetection.Version);

                            if (_cachedDetection.Evidence.Count > 0)
                            {
                                EditorGUILayout.LabelField($"Evidence ({_cachedDetection.Evidence.Count}):");
                                foreach (var ev in _cachedDetection.Evidence)
                                    EditorGUILayout.LabelField("  " + ev, EditorStyles.miniLabel);
                            }
                        }

                        EditorGUI.indentLevel--;
                    }

                    GUILayout.Space(10);

                    // Engine reimport section
                    _showReimportOptions = EditorGUILayout.Foldout(_showReimportOptions, "Engine Reimport", true);
                    if (_showReimportOptions)
                    {
                        EditorGUI.indentLevel++;

                        _targetEngineIndex = EditorGUILayout.Popup("Target Engine", _targetEngineIndex, _targetEngines);

                        if (_targetEngineIndex == 1 || _targetEngineIndex == 3) // Unity
                        {
                            _generateMetaFiles = EditorGUILayout.Toggle("Generate .meta files", _generateMetaFiles);
                            EditorGUILayout.HelpBox(
                                "Exports assets in Unity-ready format with .meta files containing import settings (texture compression, model scale, audio settings).",
                                MessageType.Info);
                        }

                        if (_targetEngineIndex == 2 || _targetEngineIndex == 3) // Unreal
                        {
                            _generateImportSettings = EditorGUILayout.Toggle("Generate import configs", _generateImportSettings);
                            EditorGUILayout.HelpBox(
                                "Exports assets with Unreal import JSON configs (compression settings, LOD groups, collision generation).",
                                MessageType.Info);
                        }

                        if (_targetEngineIndex > 0)
                        {
                            _preserveStructure = EditorGUILayout.Toggle("Preserve folder structure", _preserveStructure);

                            GUILayout.Space(5);

                            if (GUILayout.Button("Export for Engine Reimport", GUILayout.Height(28)))
                            {
                                string outputDir = EditorUtility.SaveFolderPanel(
                                    $"Export for {_targetEngines[_targetEngineIndex]}", "", "");

                                if (!string.IsNullOrEmpty(outputDir))
                                {
                                    var converter = new Core.Plugins.EngineReimportConverter();
                                    var profile = new Core.Plugins.ConversionProfile
                                    {
                                        TargetEngine = (Core.Plugins.TargetEngine)_targetEngineIndex,
                                        GenerateMetaFiles = _generateMetaFiles,
                                        GenerateImportSettings = _generateImportSettings,
                                        PreserveDirectoryStructure = _preserveStructure
                                    };

                                    var result = converter.ConvertAsset(entry, outputDir, profile);
                                    if (result.Success)
                                        EditorUtility.DisplayDialog("Reimport Export Complete",
                                            $"Exported to:\n{result.OutputPath}\n\nFormat: {result.ConvertedFormat}" +
                                            (result.MetaFilePath != null ? $"\nMeta: {result.MetaFilePath}" : "") +
                                            (result.Warnings.Count > 0 ? $"\n\nWarnings: {result.Warnings.Count}" : ""),
                                            "OK");
                                    else
                                        EditorUtility.DisplayDialog("Export Failed",
                                            $"Error: {result.ErrorMessage}", "OK");
                                }
                            }
                        }

                        EditorGUI.indentLevel--;
                    }

                    // Children info
                    if (entry.Children.Count > 0)
                    {
                        GUILayout.Space(10);
                        EditorGUILayout.LabelField($"Children: {entry.Children.Count}", EditorStyles.boldLabel);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private string[] GetFormatOptionsForType(AssetType type)
        {
            switch (type)
            {
                case AssetType.Texture: return _textureExportFormats;
                case AssetType.Audio: return _audioExportFormats;
                case AssetType.Model: return _modelExportFormats;
                case AssetType.Video: return _videoExportFormats;
                case AssetType.Animation: return _animationExportFormats;
                default: return new[] { "Raw" };
            }
        }

        private string GetDefaultExtension(AssetType type)
        {
            switch (type)
            {
                case AssetType.Texture: return "png";
                case AssetType.Audio: return "wav";
                case AssetType.Model: return "obj";
                case AssetType.Video: return "bik";
                case AssetType.Animation: return "json";
                default: return "bin";
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }
    }
}
#endif
