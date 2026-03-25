#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Editor.Styles;
using CoreAssetDatabase = JackTheRipper360.Core.Discovery.AssetDatabase;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Panel showing discovered assets in a filterable, scrollable list grouped by type.
    /// </summary>
    public class AssetTreePanel
    {
        public event Action<AssetEntry> OnAssetSelected;

        private Vector2 _scrollPosition;
        private string _searchFilter = "";
        private AssetType _typeFilter = AssetType.Unknown; // Unknown = show all
        private AssetEntry _selectedEntry;
        private readonly HashSet<string> _expandedGroups = new HashSet<string>();

        // Cached snapshot to avoid collection modification during OnGUI
        private List<AssetEntry> _cachedEntries = new List<AssetEntry>();
        private Dictionary<AssetType, int> _cachedCounts = new Dictionary<AssetType, int>();
        private int _lastKnownCount;

        private readonly string[] _filterOptions = {
            "All", "Textures", "Audio", "Models", "Video", "Animation", "Containers"
        };
        private int _filterIndex;

        public void Draw(CoreAssetDatabase database)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            {
                EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);

                // Search bar
                EditorGUILayout.BeginHorizontal();
                {
                    _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                        _searchFilter = "";
                }
                EditorGUILayout.EndHorizontal();

                // Type filter
                _filterIndex = GUILayout.Toolbar(_filterIndex, _filterOptions, EditorStyles.toolbarButton);
                _typeFilter = IndexToType(_filterIndex);

                GUILayout.Space(3);

                int totalCount = database != null ? database.TotalCount : 0;

                if (database == null || totalCount == 0)
                {
                    EditorGUILayout.HelpBox("No assets loaded. Open a folder or file to begin.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                // Refresh cached snapshot when database changes
                if (totalCount != _lastKnownCount)
                {
                    _lastKnownCount = totalCount;
                    try
                    {
                        if (!string.IsNullOrEmpty(_searchFilter))
                            _cachedEntries = new List<AssetEntry>(database.Search(_searchFilter));
                        else if (_typeFilter != AssetType.Unknown)
                            _cachedEntries = new List<AssetEntry>(database.GetByType(_typeFilter));
                        else
                            _cachedEntries = new List<AssetEntry>(database.GetAllEntries());

                        _cachedCounts = database.GetTypeCounts();
                    }
                    catch { /* collection modified - will retry next frame */ }
                }
                // Also refresh on filter change
                else if (Event.current.type == EventType.Layout)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(_searchFilter))
                            _cachedEntries = new List<AssetEntry>(database.Search(_searchFilter));
                        else if (_typeFilter != AssetType.Unknown)
                            _cachedEntries = new List<AssetEntry>(database.GetByType(_typeFilter));
                        else
                            _cachedEntries = new List<AssetEntry>(database.GetAllEntries());

                        _cachedCounts = database.GetTypeCounts();
                    }
                    catch { /* collection modified - will retry next frame */ }
                }

                // Asset counts
                EditorGUILayout.BeginHorizontal();
                {
                    foreach (var kvp in _cachedCounts)
                    {
                        var color = JackTheRipperStyles.GetAssetTypeColor(kvp.Key);
                        GUI.color = color;
                        GUILayout.Label($"{JackTheRipperStyles.GetAssetTypeIcon(kvp.Key)} {kvp.Value}",
                            EditorStyles.miniLabel);
                    }
                    GUI.color = Color.white;
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(3);

                // Asset list from cached snapshot (safe to iterate)
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    for (int i = 0; i < _cachedEntries.Count; i++)
                    {
                        DrawAssetEntry(_cachedEntries[i], 0);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAssetEntry(AssetEntry entry, int indent)
        {
            // Apply type filter
            if (_typeFilter != AssetType.Unknown && entry.Type != _typeFilter && entry.Children.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(indent * 15);

                bool isSelected = _selectedEntry == entry;
                var color = JackTheRipperStyles.GetAssetTypeColor(entry.Type);

                if (isSelected)
                    GUI.backgroundColor = JackTheRipperStyles.SelectedColor;

                string icon = JackTheRipperStyles.GetAssetTypeIcon(entry.Type);
                string label = $"{icon} {entry.Name}";

                if (entry.Children.Count > 0)
                {
                    string key = entry.SourcePath ?? entry.Name;
                    bool expanded = _expandedGroups.Contains(key);

                    // Foldout
                    EditorGUILayout.BeginVertical();
                    if (GUILayout.Button($"{(expanded ? "v" : ">")} {label} ({entry.Children.Count})",
                        EditorStyles.label))
                    {
                        if (expanded) _expandedGroups.Remove(key);
                        else _expandedGroups.Add(key);

                        _selectedEntry = entry;
                        OnAssetSelected?.Invoke(entry);
                    }
                    EditorGUILayout.EndVertical();

                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();

                    if (expanded)
                    {
                        foreach (var child in entry.Children)
                            DrawAssetEntry(child, indent + 1);
                    }
                    return;
                }

                GUI.color = color;
                if (GUILayout.Button(label, EditorStyles.label))
                {
                    _selectedEntry = entry;
                    OnAssetSelected?.Invoke(entry);
                }
                GUI.color = Color.white;

                // Size info
                GUILayout.FlexibleSpace();
                if (entry.Size > 0)
                {
                    string sizeStr = FormatSize(entry.Size);
                    GUILayout.Label(sizeStr, EditorStyles.miniLabel, GUILayout.Width(60));
                }

                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        private AssetType IndexToType(int index)
        {
            switch (index)
            {
                case 1: return AssetType.Texture;
                case 2: return AssetType.Audio;
                case 3: return AssetType.Model;
                case 4: return AssetType.Video;
                case 5: return AssetType.Animation;
                case 6: return AssetType.Container;
                default: return AssetType.Unknown;
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
#endif
