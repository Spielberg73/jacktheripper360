#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Runtime.Services;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Left panel: folder picker and recent paths.
    /// </summary>
    public class FileBrowserPanel
    {
        public event Action<string> OnPathSelected;

        private string _currentPath = "";
        private Vector2 _scrollPosition;
        private string[] _recentPaths = new string[0];

        public void Draw()
        {
            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("File Browser", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                {
                    _currentPath = EditorGUILayout.TextField(_currentPath);
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        string path = EditorUtility.OpenFolderPanel(
                            "Select Xbox 360 Game Folder",
                            SettingsService.Current.LastOpenedPath, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            _currentPath = path;
                            SettingsService.Current.LastOpenedPath = path;
                            OnPathSelected?.Invoke(path);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_currentPath) && GUILayout.Button("Scan"))
                {
                    OnPathSelected?.Invoke(_currentPath);
                }

                // Quick access buttons
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Quick Open:", EditorStyles.miniLabel);

                if (GUILayout.Button("Open ISO/GOD File", EditorStyles.miniButton))
                {
                    string file = EditorUtility.OpenFilePanel("Select ISO/GOD",
                        SettingsService.Current.LastOpenedPath, "iso");
                    if (!string.IsNullOrEmpty(file))
                    {
                        _currentPath = file;
                        OnPathSelected?.Invoke(file);
                    }
                }

                if (GUILayout.Button("Open STFS Package", EditorStyles.miniButton))
                {
                    string file = EditorUtility.OpenFilePanel("Select STFS Package",
                        SettingsService.Current.LastOpenedPath, "");
                    if (!string.IsNullOrEmpty(file))
                    {
                        _currentPath = file;
                        OnPathSelected?.Invoke(file);
                    }
                }

                if (GUILayout.Button("Open XEX Executable", EditorStyles.miniButton))
                {
                    string file = EditorUtility.OpenFilePanel("Select XEX",
                        SettingsService.Current.LastOpenedPath, "xex");
                    if (!string.IsNullOrEmpty(file))
                    {
                        _currentPath = file;
                        OnPathSelected?.Invoke(file);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
