#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
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
        private readonly string[] _audioExportFormats = { "Auto", "WAV", "Raw" };
        private readonly string[] _modelExportFormats = { "Auto", "OBJ" };
        private readonly string[] _videoExportFormats = { "Auto", "Raw" };
        private int _formatIndex;

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
