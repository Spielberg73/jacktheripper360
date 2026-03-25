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

        // Stable snapshot - only refreshed between full OnGUI cycles (not between Layout/Repaint)
        private List<AssetEntry> _snapshot = new List<AssetEntry>();
        private string _countsSummary = "";
        private int _snapshotTotal;
        private bool _needsRefresh = true;
        private int _lastFilterIndex = -1;
        private string _lastSearch = "";

        private readonly string[] _filterOptions = {
            "All", "Textures", "Audio", "Models", "Video", "Animation", "Containers"
        };
        private int _filterIndex;

        /// <summary>
        /// Call this from outside OnGUI to trigger a cache refresh on next draw.
        /// </summary>
        public void MarkDirty() => _needsRefresh = true;

        public void Draw(CoreAssetDatabase database)
        {
            // Refresh snapshot ONLY during Layout pass to keep Layout/Repaint consistent
            if (Event.current.type == EventType.Layout)
                RefreshIfNeeded(database);

            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);

            // Search bar
            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("X", GUILayout.Width(20)))
                _searchFilter = "";
            EditorGUILayout.EndHorizontal();

            // Type filter
            _filterIndex = GUILayout.Toolbar(_filterIndex, _filterOptions, EditorStyles.toolbarButton);
            _typeFilter = IndexToType(_filterIndex);

            // Detect filter changes for next frame
            if (_filterIndex != _lastFilterIndex || _searchFilter != _lastSearch)
                _needsRefresh = true;

            GUILayout.Space(3);

            if (_snapshotTotal == 0)
            {
                EditorGUILayout.HelpBox("No assets loaded. Open a folder or file to begin.", MessageType.Info);
            }
            else
            {
                // Counts summary (single label, no dynamic foreach)
                if (!string.IsNullOrEmpty(_countsSummary))
                    EditorGUILayout.LabelField(_countsSummary, EditorStyles.miniLabel);

                GUILayout.Space(3);

                // Asset list from stable snapshot
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                for (int i = 0; i < _snapshot.Count; i++)
                    DrawAssetEntry(_snapshot[i], 0);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void RefreshIfNeeded(CoreAssetDatabase database)
        {
            if (database == null)
            {
                if (_snapshotTotal != 0)
                {
                    _snapshot.Clear();
                    _countsSummary = "";
                    _snapshotTotal = 0;
                }
                return;
            }

            int currentTotal = database.TotalCount;

            // Check if refresh needed
            bool filterChanged = _filterIndex != _lastFilterIndex || _searchFilter != _lastSearch;
            bool countChanged = currentTotal != _snapshotTotal;

            if (!_needsRefresh && !filterChanged && !countChanged)
                return;

            _lastFilterIndex = _filterIndex;
            _lastSearch = _searchFilter;
            _needsRefresh = false;

            try
            {
                // Take snapshot
                if (!string.IsNullOrEmpty(_searchFilter))
                    _snapshot = new List<AssetEntry>(database.Search(_searchFilter));
                else if (_typeFilter != AssetType.Unknown)
                    _snapshot = new List<AssetEntry>(database.GetByType(_typeFilter));
                else
                    _snapshot = new List<AssetEntry>(database.GetAllEntries());

                _snapshotTotal = currentTotal;

                // Build counts summary as a single string
                var counts = database.GetTypeCounts();
                var parts = new List<string>();
                foreach (var kvp in counts)
                    parts.Add($"{JackTheRipperStyles.GetAssetTypeIcon(kvp.Key)} {kvp.Value}");
                _countsSummary = string.Join("  ", parts.ToArray());
            }
            catch
            {
                // Collection was modified during snapshot - retry next frame
                _needsRefresh = true;
            }
        }

        private void DrawAssetEntry(AssetEntry entry, int indent)
        {
            if (entry == null) return;

            // Apply type filter
            if (_typeFilter != AssetType.Unknown && entry.Type != _typeFilter && entry.Children.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(indent * 15);

            bool isSelected = _selectedEntry == entry;
            var color = JackTheRipperStyles.GetAssetTypeColor(entry.Type);

            if (isSelected)
                GUI.backgroundColor = JackTheRipperStyles.SelectedColor;

            string icon = JackTheRipperStyles.GetAssetTypeIcon(entry.Type);
            string displayName = SanitizeDisplayName(entry.Name);

            if (entry.Children.Count > 0)
            {
                string key = entry.SourcePath ?? entry.Name;
                bool expanded = _expandedGroups.Contains(key);

                if (GUILayout.Button($"{(expanded ? "v" : ">")} {icon} {displayName} ({entry.Children.Count})",
                    EditorStyles.label))
                {
                    if (expanded) _expandedGroups.Remove(key);
                    else _expandedGroups.Add(key);

                    _selectedEntry = entry;
                    OnAssetSelected?.Invoke(entry);
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (expanded)
                {
                    // Copy children to avoid modification during iteration
                    var children = entry.Children;
                    for (int i = 0; i < children.Count; i++)
                        DrawAssetEntry(children[i], indent + 1);
                }
                return;
            }

            GUI.color = color;
            if (GUILayout.Button($"{icon} {displayName}", EditorStyles.label))
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
            EditorGUILayout.EndHorizontal();
        }

        private static string SanitizeDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "(unnamed)";
            // Replace non-printable chars with '?'
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] < 32 || chars[i] > 126)
                    chars[i] = '?';
            }
            return new string(chars);
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
