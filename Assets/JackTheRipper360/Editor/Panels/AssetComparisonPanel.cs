#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Editor.Styles;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Panel for comparing assets between two sources (e.g., game versions).
    /// Shows side-by-side differences in metadata, size, and content.
    /// </summary>
    public class AssetComparisonPanel
    {
        private List<AssetEntry> _sourceA = new List<AssetEntry>();
        private List<AssetEntry> _sourceB = new List<AssetEntry>();
        private string _sourceAName = "Source A";
        private string _sourceBName = "Source B";
        private List<ComparisonResult> _results = new List<ComparisonResult>();
        private Vector2 _scrollPosition;
        private ComparisonFilter _filter = ComparisonFilter.All;

        public enum ComparisonFilter
        {
            All,
            OnlyInA,
            OnlyInB,
            Modified,
            Identical
        }

        public class ComparisonResult
        {
            public string Name;
            public AssetEntry EntryA;
            public AssetEntry EntryB;
            public ComparisonStatus Status;
            public string Details;
        }

        public enum ComparisonStatus
        {
            Identical,
            Modified,
            OnlyInA,
            OnlyInB
        }

        public void SetSources(List<AssetEntry> sourceA, string nameA,
                               List<AssetEntry> sourceB, string nameB)
        {
            _sourceA = sourceA ?? new List<AssetEntry>();
            _sourceB = sourceB ?? new List<AssetEntry>();
            _sourceAName = nameA;
            _sourceBName = nameB;

            RunComparison();
        }

        private void RunComparison()
        {
            _results.Clear();

            var mapA = new Dictionary<string, AssetEntry>();
            var mapB = new Dictionary<string, AssetEntry>();

            foreach (var entry in _sourceA)
                mapA[entry.Name ?? ""] = entry;
            foreach (var entry in _sourceB)
                mapB[entry.Name ?? ""] = entry;

            // Find entries in both, only in A, or modified
            foreach (var kvp in mapA)
            {
                if (mapB.TryGetValue(kvp.Key, out var entryB))
                {
                    bool identical = kvp.Value.Size == entryB.Size &&
                                     kvp.Value.FormatName == entryB.FormatName;

                    _results.Add(new ComparisonResult
                    {
                        Name = kvp.Key,
                        EntryA = kvp.Value,
                        EntryB = entryB,
                        Status = identical ? ComparisonStatus.Identical : ComparisonStatus.Modified,
                        Details = identical ? "Identical" :
                            $"Size: {kvp.Value.Size} vs {entryB.Size} | Format: {kvp.Value.FormatName} vs {entryB.FormatName}"
                    });
                }
                else
                {
                    _results.Add(new ComparisonResult
                    {
                        Name = kvp.Key,
                        EntryA = kvp.Value,
                        Status = ComparisonStatus.OnlyInA,
                        Details = $"Only in {_sourceAName}"
                    });
                }
            }

            // Find entries only in B
            foreach (var kvp in mapB)
            {
                if (!mapA.ContainsKey(kvp.Key))
                {
                    _results.Add(new ComparisonResult
                    {
                        Name = kvp.Key,
                        EntryB = kvp.Value,
                        Status = ComparisonStatus.OnlyInB,
                        Details = $"Only in {_sourceBName}"
                    });
                }
            }

            // Sort: modified first, then only-in-A, only-in-B, identical
            _results.Sort((a, b) => a.Status.CompareTo(b.Status));
        }

        public void Draw()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            {
                EditorGUILayout.LabelField("Asset Comparison", EditorStyles.boldLabel);

                // Summary
                int identical = 0, modified = 0, onlyA = 0, onlyB = 0;
                foreach (var r in _results)
                {
                    switch (r.Status)
                    {
                        case ComparisonStatus.Identical: identical++; break;
                        case ComparisonStatus.Modified: modified++; break;
                        case ComparisonStatus.OnlyInA: onlyA++; break;
                        case ComparisonStatus.OnlyInB: onlyB++; break;
                    }
                }

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label($"Total: {_results.Count}", EditorStyles.miniLabel);
                    GUI.color = Color.green;
                    GUILayout.Label($"Identical: {identical}", EditorStyles.miniLabel);
                    GUI.color = Color.yellow;
                    GUILayout.Label($"Modified: {modified}", EditorStyles.miniLabel);
                    GUI.color = new Color(1, 0.5f, 0.5f);
                    GUILayout.Label($"Only A: {onlyA}", EditorStyles.miniLabel);
                    GUI.color = new Color(0.5f, 0.5f, 1f);
                    GUILayout.Label($"Only B: {onlyB}", EditorStyles.miniLabel);
                    GUI.color = Color.white;
                }
                EditorGUILayout.EndHorizontal();

                // Filter
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    if (GUILayout.Toggle(_filter == ComparisonFilter.All, "All", EditorStyles.toolbarButton))
                        _filter = ComparisonFilter.All;
                    if (GUILayout.Toggle(_filter == ComparisonFilter.Modified, "Modified", EditorStyles.toolbarButton))
                        _filter = ComparisonFilter.Modified;
                    if (GUILayout.Toggle(_filter == ComparisonFilter.OnlyInA, "Only A", EditorStyles.toolbarButton))
                        _filter = ComparisonFilter.OnlyInA;
                    if (GUILayout.Toggle(_filter == ComparisonFilter.OnlyInB, "Only B", EditorStyles.toolbarButton))
                        _filter = ComparisonFilter.OnlyInB;
                    if (GUILayout.Toggle(_filter == ComparisonFilter.Identical, "Identical", EditorStyles.toolbarButton))
                        _filter = ComparisonFilter.Identical;
                }
                EditorGUILayout.EndHorizontal();

                // Results list
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    foreach (var result in _results)
                    {
                        if (_filter != ComparisonFilter.All &&
                            (int)_filter - 1 != (int)result.Status)
                            continue;

                        DrawComparisonEntry(result);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawComparisonEntry(ComparisonResult result)
        {
            Color statusColor;
            switch (result.Status)
            {
                case ComparisonStatus.Identical: statusColor = new Color(0.3f, 0.8f, 0.3f); break;
                case ComparisonStatus.Modified: statusColor = new Color(0.9f, 0.8f, 0.2f); break;
                case ComparisonStatus.OnlyInA: statusColor = new Color(0.9f, 0.4f, 0.4f); break;
                case ComparisonStatus.OnlyInB: statusColor = new Color(0.4f, 0.4f, 0.9f); break;
                default: statusColor = Color.gray; break;
            }

            GUI.color = statusColor;
            EditorGUILayout.BeginHorizontal("box");
            {
                GUILayout.Label(result.Status.ToString(), EditorStyles.miniLabel, GUILayout.Width(70));
                GUI.color = Color.white;

                AssetType type = result.EntryA?.Type ?? result.EntryB?.Type ?? AssetType.Unknown;
                string icon = JackTheRipperStyles.GetAssetTypeIcon(type);
                GUILayout.Label($"{icon} {result.Name}", GUILayout.Width(200));

                GUILayout.Label(result.Details, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
            GUI.color = Color.white;
        }
    }
}
#endif
