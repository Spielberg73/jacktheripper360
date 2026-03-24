#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Runtime.Services;
using JackTheRipper360.Editor.Panels;
using JackTheRipper360.Editor.Styles;

namespace JackTheRipper360.Editor.MainWindow
{
    /// <summary>
    /// Main EditorWindow for JackTheRipper360 - Xbox 360 Asset Extractor.
    /// Composes all UI panels and wires services together.
    /// </summary>
    public class JackTheRipperWindow : EditorWindow
    {
        // Services
        private AssetDiscoveryService _discoveryService;
        private ExportService _exportService;

        // Panels
        private FileBrowserPanel _fileBrowserPanel;
        private AssetTreePanel _assetTreePanel;
        private PreviewPanel _previewPanel;
        private InspectorPanel _inspectorPanel;

        // State
        private AssetEntry _selectedEntry;
        private string _statusMessage = "Ready. Open a folder or file to begin scanning.";
        private float _scanProgress;
        private bool _isScanning;
        private Vector2 _scrollPosition;

        // Layout
        private float _leftPanelWidth = 250f;
        private float _centerPanelWidth = 300f;
        private float _rightPanelWidth = 300f;
        private bool _resizingLeft;
        private bool _resizingRight;

        [MenuItem("Tools/JackTheRipper360 - Xbox 360 Asset Extractor")]
        public static void ShowWindow()
        {
            var window = GetWindow<JackTheRipperWindow>();
            window.titleContent = new GUIContent("JackTheRipper360");
            window.minSize = new Vector2(900, 500);
            window.Show();
        }

        private void OnEnable()
        {
            _discoveryService = new AssetDiscoveryService();
            _exportService = new ExportService();

            _fileBrowserPanel = new FileBrowserPanel();
            _assetTreePanel = new AssetTreePanel();
            _previewPanel = new PreviewPanel();
            _inspectorPanel = new InspectorPanel();

            // Wire events
            _discoveryService.OnProgress += OnScanProgress;
            _discoveryService.OnComplete += OnScanComplete;
            _discoveryService.OnError += OnScanError;

            _fileBrowserPanel.OnPathSelected += OnPathSelected;
            _assetTreePanel.OnAssetSelected += OnAssetSelected;

            // Load settings
            SettingsService.Load();
        }

        private void OnDisable()
        {
            _discoveryService?.CancelScan();
            SettingsService.Save();
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            {
                // Left panel: File browser + Asset tree
                DrawLeftPanel();

                // Resizer
                DrawResizer(ref _leftPanelWidth, ref _resizingLeft);

                // Center panel: Preview
                DrawCenterPanel();

                // Resizer
                DrawResizer(ref _rightPanelWidth, ref _resizingRight);

                // Right panel: Inspector
                DrawRightPanel();
            }
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();

            // Repaint while scanning to show progress
            if (_isScanning)
                Repaint();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("JackTheRipper360", JackTheRipperStyles.HeaderStyle, GUILayout.Height(24));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Xbox 360 Game Folder", SettingsService.Current.LastOpenedPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        SettingsService.Current.LastOpenedPath = path;
                        StartScan(path);
                    }
                }

                if (GUILayout.Button("Open File", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    string file = EditorUtility.OpenFilePanel("Select Xbox 360 File",
                        SettingsService.Current.LastOpenedPath,
                        "iso,xex,xwb,xsb,dds,xpr,bik,wmv");
                    if (!string.IsNullOrEmpty(file))
                    {
                        SettingsService.Current.LastOpenedPath = System.IO.Path.GetDirectoryName(file);
                        StartFileScan(file);
                    }
                }

                EditorGUI.BeginDisabledGroup(_selectedEntry == null);
                if (GUILayout.Button("Export Selected", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    ExportSelected();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(_discoveryService?.Database?.TotalCount == 0);
                if (GUILayout.Button("Export All", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    ExportAll();
                }
                EditorGUI.EndDisabledGroup();

                if (_isScanning)
                {
                    if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    {
                        _discoveryService.CancelScan();
                        _isScanning = false;
                        _statusMessage = "Scan cancelled.";
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));
            {
                _fileBrowserPanel.Draw();

                GUILayout.Space(5);

                // Asset tree
                _assetTreePanel.Draw(_discoveryService?.Database);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCenterPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                _previewPanel.Draw(_selectedEntry);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth));
            {
                _inspectorPanel.Draw(_selectedEntry);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(JackTheRipperStyles.StatusBarStyle, GUILayout.Height(22));
            {
                GUILayout.Label(_statusMessage, EditorStyles.miniLabel);

                if (_isScanning)
                {
                    var rect = GUILayoutUtility.GetRect(150, 16);
                    EditorGUI.ProgressBar(rect, _scanProgress, $"{(_scanProgress * 100):F0}%");
                }

                GUILayout.FlexibleSpace();

                int total = _discoveryService?.Database?.TotalCount ?? 0;
                GUILayout.Label($"Assets: {total}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResizer(ref float panelWidth, ref bool resizing)
        {
            var resizeRect = GUILayoutUtility.GetRect(4, 4, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
                resizing = true;

            if (resizing)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    panelWidth += Event.current.delta.x;
                    panelWidth = Mathf.Clamp(panelWidth, 150, 600);
                    Repaint();
                }
                if (Event.current.type == EventType.MouseUp)
                    resizing = false;
            }
        }

        // Event handlers
        private void StartScan(string path)
        {
            _isScanning = true;
            _scanProgress = 0;
            _statusMessage = $"Scanning: {path}";
            _discoveryService.StartScan(path);
        }

        private void StartFileScan(string filePath)
        {
            _statusMessage = $"Scanning: {filePath}";
            var result = _discoveryService.ScanFile(filePath);
            _statusMessage = $"Found {result.AssetsFound} assets.";
            Repaint();
        }

        private void OnPathSelected(string path)
        {
            StartScan(path);
        }

        private void OnScanProgress(ScanProgress progress)
        {
            _scanProgress = progress.Progress;
            _statusMessage = $"Scanning: {System.IO.Path.GetFileName(progress.CurrentFile)} ({progress.FilesProcessed}/{progress.TotalFiles})";
        }

        private void OnScanComplete(ScanResult result)
        {
            _isScanning = false;
            _statusMessage = $"Scan complete. Found {result.AssetsFound} assets in {result.ContainersFound} containers.";
            if (result.Errors.Count > 0)
                _statusMessage += $" ({result.Errors.Count} errors)";
        }

        private void OnScanError(string error)
        {
            _isScanning = false;
            _statusMessage = $"Error: {error}";
        }

        private void OnAssetSelected(AssetEntry entry)
        {
            _selectedEntry = entry;
            Repaint();
        }

        private void ExportSelected()
        {
            if (_selectedEntry == null) return;

            string outputPath = EditorUtility.SaveFilePanel("Export Asset",
                SettingsService.Current.LastExportPath,
                _selectedEntry.Name, "");

            if (!string.IsNullOrEmpty(outputPath))
            {
                SettingsService.Current.LastExportPath = System.IO.Path.GetDirectoryName(outputPath);
                var result = _exportService.ExportAsset(_selectedEntry, outputPath, new ExportOptions
                {
                    OutputDirectory = System.IO.Path.GetDirectoryName(outputPath),
                    OverwriteExisting = SettingsService.Current.OverwriteExisting
                });

                _statusMessage = result.Success
                    ? $"Exported: {result.OutputPath} ({result.BytesWritten} bytes)"
                    : $"Export failed: {result.ErrorMessage}";
            }
        }

        private void ExportAll()
        {
            string outputDir = EditorUtility.OpenFolderPanel("Select Export Directory",
                SettingsService.Current.LastExportPath, "");

            if (!string.IsNullOrEmpty(outputDir))
            {
                SettingsService.Current.LastExportPath = outputDir;
                var entries = _discoveryService.Database.GetAllEntriesFlat();

                _exportService.OnProgress += p =>
                {
                    _statusMessage = $"Exporting: {p.CurrentAsset} ({p.AssetsProcessed}/{p.TotalAssets})";
                    Repaint();
                };

                _exportService.OnBatchComplete += r =>
                {
                    _statusMessage = $"Export complete. {r.Succeeded} succeeded, {r.Failed} failed.";
                    Repaint();
                };

                _exportService.BatchExport(entries, outputDir, new ExportOptions
                {
                    OutputDirectory = outputDir,
                    OverwriteExisting = SettingsService.Current.OverwriteExisting,
                    PreserveDirectoryStructure = SettingsService.Current.PreserveDirectoryStructure
                });
            }
        }
    }
}
#endif
