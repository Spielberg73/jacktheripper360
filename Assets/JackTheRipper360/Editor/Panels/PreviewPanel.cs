#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Runtime.Preview;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Center panel: preview of selected asset (texture, 3D model, audio waveform).
    /// </summary>
    public class PreviewPanel
    {
        private Texture2D _currentTexture;
        private Mesh _currentMesh;
        private AssetEntry _lastEntry;
        private UnityEditor.Editor _meshPreviewEditor;
        private float _previewRotation;

        public void Draw(AssetEntry entry)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

                if (entry == null)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Select an asset to preview", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndVertical();
                    return;
                }

                // Update preview if entry changed
                if (entry != _lastEntry)
                {
                    _lastEntry = entry;
                    UpdatePreview(entry);
                }

                GUILayout.Space(5);

                switch (entry.Type)
                {
                    case AssetType.Texture:
                        DrawTexturePreview();
                        break;
                    case AssetType.Model:
                        DrawModelInfo(entry);
                        break;
                    case AssetType.Audio:
                        DrawAudioPreview(entry);
                        break;
                    case AssetType.Video:
                        DrawVideoInfo(entry);
                        break;
                    case AssetType.Animation:
                        DrawAnimationInfo(entry);
                        break;
                    default:
                        DrawGenericInfo(entry);
                        break;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void UpdatePreview(AssetEntry entry)
        {
            _currentTexture = null;
            _currentMesh = null;

            // For textures, try to create a preview
            if (entry.Type == AssetType.Texture && entry.Metadata.ContainsKey("Width"))
            {
                try
                {
                    string sourcePath = entry.SourcePath;
                    if (sourcePath != null && !sourcePath.Contains(":") && System.IO.File.Exists(sourcePath))
                    {
                        using (var stream = new System.IO.FileStream(sourcePath,
                            System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                        {
                            _currentTexture = TexturePreviewRenderer.CreatePreview(entry, stream);
                        }
                    }
                }
                catch { }
            }
        }

        private void DrawTexturePreview()
        {
            if (_currentTexture != null)
            {
                var rect = GUILayoutUtility.GetAspectRect((float)_currentTexture.width / _currentTexture.height);
                GUI.DrawTexture(rect, _currentTexture, ScaleMode.ScaleToFit);

                EditorGUILayout.LabelField($"Resolution: {_currentTexture.width} x {_currentTexture.height}",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Texture preview not available", EditorStyles.centeredGreyMiniLabel);
                if (_lastEntry?.Metadata != null)
                {
                    if (_lastEntry.Metadata.ContainsKey("Width") && _lastEntry.Metadata.ContainsKey("Height"))
                    {
                        EditorGUILayout.LabelField(
                            $"Dimensions: {_lastEntry.Metadata["Width"]} x {_lastEntry.Metadata["Height"]}",
                            EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawModelInfo(AssetEntry entry)
        {
            EditorGUILayout.LabelField("3D Model", EditorStyles.boldLabel);

            if (entry.Metadata.ContainsKey("VertexCount"))
                EditorGUILayout.LabelField($"Vertices: {entry.Metadata["VertexCount"]}");
            if (entry.Metadata.ContainsKey("IndexCount"))
                EditorGUILayout.LabelField($"Indices: {entry.Metadata["IndexCount"]}");
            if (entry.Metadata.ContainsKey("SubMeshCount"))
                EditorGUILayout.LabelField($"Sub-meshes: {entry.Metadata["SubMeshCount"]}");
            if (entry.Metadata.ContainsKey("HasSkeleton"))
                EditorGUILayout.LabelField($"Has Skeleton: {entry.Metadata["HasSkeleton"]}");

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("Export as OBJ to view in a 3D editor.", MessageType.Info);
        }

        private void DrawAudioPreview(AssetEntry entry)
        {
            EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);

            if (entry.Metadata.ContainsKey("Channels"))
                EditorGUILayout.LabelField($"Channels: {entry.Metadata["Channels"]}");
            if (entry.Metadata.ContainsKey("SampleRate"))
                EditorGUILayout.LabelField($"Sample Rate: {entry.Metadata["SampleRate"]} Hz");
            if (entry.Metadata.ContainsKey("BitsPerSample"))
                EditorGUILayout.LabelField($"Bits/Sample: {entry.Metadata["BitsPerSample"]}");

            EditorGUILayout.LabelField($"Format: {entry.FormatName}");

            if (entry.Children.Count > 0)
            {
                EditorGUILayout.LabelField($"Contains {entry.Children.Count} audio entries");
            }

            GUILayout.FlexibleSpace();

            var player = AudioPreviewPlayer.Instance;
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginDisabledGroup(entry.FormatName?.Contains("XMA") == true);
                if (GUILayout.Button(player.IsPlaying ? "Stop" : "Play", GUILayout.Height(30)))
                {
                    if (player.IsPlaying)
                        player.Stop();
                    // Note: actual playback requires decoded PCM data
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawVideoInfo(AssetEntry entry)
        {
            EditorGUILayout.LabelField("Video", EditorStyles.boldLabel);

            if (entry.Metadata.ContainsKey("Width"))
                EditorGUILayout.LabelField($"Resolution: {entry.Metadata["Width"]} x {entry.Metadata["Height"]}");
            if (entry.Metadata.ContainsKey("FrameCount"))
                EditorGUILayout.LabelField($"Frames: {entry.Metadata["FrameCount"]}");
            if (entry.Metadata.ContainsKey("FPS"))
                EditorGUILayout.LabelField($"FPS: {entry.Metadata["FPS"]:F1}");
            if (entry.Metadata.ContainsKey("Duration"))
                EditorGUILayout.LabelField($"Duration: {entry.Metadata["Duration"]:F1}s");
            if (entry.Metadata.ContainsKey("AudioTracks"))
                EditorGUILayout.LabelField($"Audio Tracks: {entry.Metadata["AudioTracks"]}");

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("Export video to play in an external player.", MessageType.Info);
        }

        private void DrawAnimationInfo(AssetEntry entry)
        {
            EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);

            if (entry.Metadata.ContainsKey("Duration"))
                EditorGUILayout.LabelField($"Duration: {entry.Metadata["Duration"]:F2}s");
            if (entry.Metadata.ContainsKey("FrameRate"))
                EditorGUILayout.LabelField($"Frame Rate: {entry.Metadata["FrameRate"]:F0} FPS");
            if (entry.Metadata.ContainsKey("ChannelCount"))
                EditorGUILayout.LabelField($"Bone Channels: {entry.Metadata["ChannelCount"]}");
        }

        private void DrawGenericInfo(AssetEntry entry)
        {
            EditorGUILayout.LabelField(entry.Type.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Format: {entry.FormatName}");
            EditorGUILayout.LabelField($"Size: {entry.Size} bytes");

            if (entry.Metadata != null)
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Metadata:", EditorStyles.boldLabel);
                foreach (var kvp in entry.Metadata)
                {
                    EditorGUILayout.LabelField($"  {kvp.Key}: {kvp.Value}");
                }
            }
        }
    }
}
#endif
