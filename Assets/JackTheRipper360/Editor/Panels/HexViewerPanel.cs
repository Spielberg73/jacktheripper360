#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Hex viewer panel for inspecting raw bytes of selected assets.
    /// Shows hex dump with ASCII representation, offset navigation, and search.
    /// </summary>
    public class HexViewerPanel
    {
        private byte[] _data;
        private int _dataLength;
        private string _fileName;
        private Vector2 _scrollPosition;
        private int _bytesPerRow = 16;
        private long _gotoOffset;
        private string _gotoOffsetStr = "";
        private string _searchHex = "";
        private int _searchResultOffset = -1;
        private int _selectedOffset = -1;
        private int _maxDisplayBytes = 4096; // Limit for performance

        // Colors
        private static readonly Color HexZeroColor = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color HexNormalColor = new Color(0.8f, 0.9f, 1.0f);
        private static readonly Color HexHighColor = new Color(1.0f, 0.9f, 0.5f);
        private static readonly Color AsciiColor = new Color(0.6f, 0.8f, 0.6f);
        private static readonly Color OffsetColor = new Color(0.5f, 0.5f, 0.7f);
        private static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.9f, 0.3f);

        /// <summary>
        /// Load data from an asset entry for hex display.
        /// </summary>
        public void LoadAsset(AssetEntry entry)
        {
            _fileName = entry?.Name ?? "";
            _selectedOffset = -1;
            _searchResultOffset = -1;

            if (entry == null)
            {
                _data = null;
                _dataLength = 0;
                return;
            }

            try
            {
                string sourcePath = entry.SourcePath;
                if (sourcePath != null && !sourcePath.Contains(":") && File.Exists(sourcePath))
                {
                    using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        long offset = entry.Offset > 0 ? entry.Offset : 0;
                        int size = (int)Math.Min(
                            entry.Size > 0 ? entry.Size : stream.Length,
                            _maxDisplayBytes);

                        stream.Seek(offset, SeekOrigin.Begin);
                        _data = new byte[size];
                        _dataLength = stream.Read(_data, 0, size);
                    }
                }
            }
            catch
            {
                _data = null;
                _dataLength = 0;
            }
        }

        /// <summary>
        /// Load raw byte data directly.
        /// </summary>
        public void LoadData(byte[] data, string name)
        {
            _fileName = name;
            _selectedOffset = -1;

            if (data == null)
            {
                _data = null;
                _dataLength = 0;
                return;
            }

            int size = Math.Min(data.Length, _maxDisplayBytes);
            _data = new byte[size];
            Buffer.BlockCopy(data, 0, _data, 0, size);
            _dataLength = size;
        }

        public void Draw()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            {
                // Header
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Hex Viewer", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (_data != null)
                        EditorGUILayout.LabelField($"{_dataLength} bytes", EditorStyles.miniLabel, GUILayout.Width(80));
                }
                EditorGUILayout.EndHorizontal();

                if (_data == null || _dataLength == 0)
                {
                    EditorGUILayout.HelpBox("Select an asset to view its raw bytes.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                // Toolbar
                DrawToolbar();

                // Hex dump
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    // Column headers
                    var headerSb = new StringBuilder();
                    headerSb.Append("  Offset   ");
                    for (int i = 0; i < _bytesPerRow; i++)
                        headerSb.Append($"{i:X2} ");
                    headerSb.Append("  ASCII");

                    GUI.color = OffsetColor;
                    EditorGUILayout.LabelField(headerSb.ToString(), EditorStyles.miniLabel);
                    GUI.color = Color.white;

                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                    // Data rows
                    int rowCount = (_dataLength + _bytesPerRow - 1) / _bytesPerRow;

                    for (int row = 0; row < rowCount; row++)
                    {
                        int offset = row * _bytesPerRow;
                        DrawHexRow(offset);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                // Go to offset
                GUILayout.Label("Go to:", EditorStyles.miniLabel, GUILayout.Width(35));
                _gotoOffsetStr = EditorGUILayout.TextField(_gotoOffsetStr, GUILayout.Width(80));
                if (GUILayout.Button("Go", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    if (long.TryParse(_gotoOffsetStr, System.Globalization.NumberStyles.HexNumber,
                        null, out long offset))
                    {
                        _selectedOffset = (int)Math.Min(offset, _dataLength - 1);
                        int row = _selectedOffset / _bytesPerRow;
                        _scrollPosition.y = row * 15f;
                    }
                }

                GUILayout.Space(10);

                // Search hex
                GUILayout.Label("Find:", EditorStyles.miniLabel, GUILayout.Width(30));
                _searchHex = EditorGUILayout.TextField(_searchHex, GUILayout.Width(120));
                if (GUILayout.Button("Search", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    SearchHex();
                }

                GUILayout.FlexibleSpace();

                // Bytes per row
                GUILayout.Label("Cols:", EditorStyles.miniLabel, GUILayout.Width(30));
                if (GUILayout.Button("8", EditorStyles.toolbarButton, GUILayout.Width(20)))
                    _bytesPerRow = 8;
                if (GUILayout.Button("16", EditorStyles.toolbarButton, GUILayout.Width(25)))
                    _bytesPerRow = 16;
                if (GUILayout.Button("32", EditorStyles.toolbarButton, GUILayout.Width(25)))
                    _bytesPerRow = 32;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHexRow(int offset)
        {
            var sb = new StringBuilder();

            // Offset column
            sb.Append($"{offset:X8}  ");

            // Hex bytes
            for (int i = 0; i < _bytesPerRow; i++)
            {
                int idx = offset + i;
                if (idx < _dataLength)
                    sb.Append($"{_data[idx]:X2} ");
                else
                    sb.Append("   ");

                if (i == 7) sb.Append(" "); // Extra space at midpoint
            }

            sb.Append(" ");

            // ASCII representation
            for (int i = 0; i < _bytesPerRow; i++)
            {
                int idx = offset + i;
                if (idx < _dataLength)
                {
                    byte b = _data[idx];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                else
                {
                    sb.Append(' ');
                }
            }

            // Highlight selected/found bytes
            bool isSelected = _selectedOffset >= offset && _selectedOffset < offset + _bytesPerRow;
            bool isSearchResult = _searchResultOffset >= offset && _searchResultOffset < offset + _bytesPerRow;

            if (isSelected || isSearchResult)
                GUI.backgroundColor = SelectedColor;

            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            GUI.backgroundColor = Color.white;
        }

        private void SearchHex()
        {
            if (string.IsNullOrEmpty(_searchHex) || _data == null) return;

            // Parse hex string to bytes
            string hex = _searchHex.Replace(" ", "").Replace("0x", "");
            if (hex.Length % 2 != 0) return;

            byte[] searchBytes = new byte[hex.Length / 2];
            for (int i = 0; i < searchBytes.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber, null, out searchBytes[i]))
                    return;
            }

            // Search in data
            int startFrom = _searchResultOffset >= 0 ? _searchResultOffset + 1 : 0;
            for (int i = startFrom; i <= _dataLength - searchBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < searchBytes.Length; j++)
                {
                    if (_data[i + j] != searchBytes[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    _searchResultOffset = i;
                    _selectedOffset = i;
                    int row = i / _bytesPerRow;
                    _scrollPosition.y = row * 15f;
                    return;
                }
            }

            // Wrap around
            _searchResultOffset = -1;
        }
    }
}
#endif
