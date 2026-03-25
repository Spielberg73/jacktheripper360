#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Runtime.Services;
using JackTheRipper360.Editor.Panels;
using AppSettingsService = JackTheRipper360.Runtime.Services.SettingsService;
using JackTheRipper360.Editor.Styles;

namespace JackTheRipper360.Editor.MainWindow
{
    /// <summary>
    /// Main EditorWindow for JackTheRipper360 - Xbox 360 Asset Extractor.
    /// Composes all UI panels and wires services together.
    /// Features: Drag & Drop, navigation history, hex viewer, 3D preview, comparison.
    /// </summary>
    public class JackTheRipperWindow : EditorWindow
    {
        // Services
        private AssetDiscoveryService _discoveryService;
        private ExportService _exportService;
        private HistoryService _historyService;

        // Panels
        private FileBrowserPanel _fileBrowserPanel;
        private AssetTreePanel _assetTreePanel;
        private PreviewPanel _previewPanel;
        private InspectorPanel _inspectorPanel;
        private HexViewerPanel _hexViewerPanel;
        private Preview3DPanel _preview3DPanel;
        private AssetComparisonPanel _comparisonPanel;

        // State
        private AssetEntry _selectedEntry;
        private string _statusMessage = "Ready. Open a folder or file to begin scanning. Drag & Drop supported.";
        private float _scanProgress;
        private bool _isScanning;

        // Layout
        private float _leftPanelWidth = 250f;
        private float _rightPanelWidth = 300f;
        private bool _resizingLeft;
        private bool _resizingRight;

        // Bottom panel tabs
        private enum BottomTab { None, HexViewer, Comparison }
        private BottomTab _bottomTab = BottomTab.None;
        private float _bottomPanelHeight = 200f;
        private bool _resizingBottom;

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
            _historyService = new HistoryService();
            _historyService.Load();

            _fileBrowserPanel = new FileBrowserPanel();
            _assetTreePanel = new AssetTreePanel();
            _previewPanel = new PreviewPanel();
            _inspectorPanel = new InspectorPanel();
            _hexViewerPanel = new HexViewerPanel();
            _preview3DPanel = new Preview3DPanel();
            _comparisonPanel = new AssetComparisonPanel();

            // Wire events
            _discoveryService.OnProgress += OnScanProgress;
            _discoveryService.OnComplete += OnScanComplete;
            _discoveryService.OnError += OnScanError;

            _fileBrowserPanel.OnPathSelected += OnPathSelected;
            _assetTreePanel.OnAssetSelected += OnAssetSelected;

            // Enable drag & drop
            wantsMouseMove = true;

            AppSettingsService.Load();
        }

        private void OnDisable()
        {
            _discoveryService?.CancelScan();
            _preview3DPanel?.Dispose();
            _historyService?.Save();
            AppSettingsService.Save();
        }

        private void OnGUI()
        {
            // Process pending results from background scan thread
            _discoveryService?.ProcessPendingCallbacks();

            // Handle Drag & Drop
            HandleDragAndDrop();

            // Handle keyboard shortcuts
            HandleKeyboardShortcuts();

            DrawToolbar();

            // Main content area
            float bottomHeight = _bottomTab != BottomTab.None ? _bottomPanelHeight : 0;

            EditorGUILayout.BeginVertical();
            {
                // Top area: 3-panel layout
                EditorGUILayout.BeginHorizontal(GUILayout.Height(position.height - 50 - bottomHeight));
                {
                    DrawLeftPanel();
                    DrawResizer(ref _leftPanelWidth, ref _resizingLeft);
                    DrawCenterPanel();
                    DrawResizer(ref _rightPanelWidth, ref _resizingRight);
                    DrawRightPanel();
                }
                EditorGUILayout.EndHorizontal();

                // Bottom panel (Hex viewer / Comparison)
                if (_bottomTab != BottomTab.None)
                {
                    DrawBottomResizer();
                    DrawBottomPanel();
                }
            }
            EditorGUILayout.EndVertical();

            DrawStatusBar();

            if (_isScanning)
                Repaint();
        }

        private void HandleDragAndDrop()
        {
            Event e = Event.current;

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (string path in DragAndDrop.paths)
                        {
                            if (System.IO.Directory.Exists(path))
                            {
                                _historyService.AddRecentPath(path);
                                StartScan(path);
                                break;
                            }
                            else if (System.IO.File.Exists(path))
                            {
                                _historyService.AddRecentPath(path);
                                StartFileScan(path);
                                break;
                            }
                        }
                    }

                    e.Use();
                }
            }
        }

        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            // Alt+Left: Navigate back
            if (e.alt && e.keyCode == KeyCode.LeftArrow && _historyService.CanGoBack)
            {
                var entry = _historyService.GoBack();
                if (entry != null)
                {
                    _selectedEntry = entry;
                    UpdatePanelsForSelection();
                }
                e.Use();
            }
            // Alt+Right: Navigate forward
            else if (e.alt && e.keyCode == KeyCode.RightArrow && _historyService.CanGoForward)
            {
                var entry = _historyService.GoForward();
                if (entry != null)
                {
                    _selectedEntry = entry;
                    UpdatePanelsForSelection();
                }
                e.Use();
            }
            // Ctrl+H: Toggle hex viewer
            else if (e.control && e.keyCode == KeyCode.H)
            {
                _bottomTab = _bottomTab == BottomTab.HexViewer ? BottomTab.None : BottomTab.HexViewer;
                e.Use();
            }
            // Ctrl+F: Toggle favorites
            else if (e.control && e.keyCode == KeyCode.F && _selectedEntry != null)
            {
                _historyService.ToggleFavorite(_selectedEntry.SourcePath ?? _selectedEntry.Name);
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("JackTheRipper360", JackTheRipperStyles.HeaderStyle, GUILayout.Height(24));

                GUILayout.FlexibleSpace();

                // Navigation buttons
                EditorGUI.BeginDisabledGroup(!_historyService.CanGoBack);
                if (GUILayout.Button("<", EditorStyles.toolbarButton, GUILayout.Width(22)))
                {
                    var entry = _historyService.GoBack();
                    if (entry != null) { _selectedEntry = entry; UpdatePanelsForSelection(); }
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!_historyService.CanGoForward);
                if (GUILayout.Button(">", EditorStyles.toolbarButton, GUILayout.Width(22)))
                {
                    var entry = _historyService.GoForward();
                    if (entry != null) { _selectedEntry = entry; UpdatePanelsForSelection(); }
                }
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(5);

                if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Xbox 360 Game Folder", AppSettingsService.Current.LastOpenedPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        AppSettingsService.Current.LastOpenedPath = path;
                        _historyService.AddRecentPath(path);
                        StartScan(path);
                    }
                }

                if (GUILayout.Button("Open File", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    string file = EditorUtility.OpenFilePanel("Select Xbox 360 File",
                        AppSettingsService.Current.LastOpenedPath,
                        "iso,xex,xwb,xsb,dds,xpr,bik,wmv,upk");
                    if (!string.IsNullOrEmpty(file))
                    {
                        AppSettingsService.Current.LastOpenedPath = System.IO.Path.GetDirectoryName(file);
                        _historyService.AddRecentPath(file);
                        StartFileScan(file);
                    }
                }

                // Recent files dropdown
                if (_historyService.RecentPaths.Count > 0)
                {
                    if (GUILayout.Button("Recent", EditorStyles.toolbarDropDown, GUILayout.Width(55)))
                    {
                        var menu = new GenericMenu();
                        foreach (var path in _historyService.RecentPaths)
                        {
                            string displayName = System.IO.Path.GetFileName(path);
                            string capturedPath = path;
                            menu.AddItem(new GUIContent(displayName), false, () =>
                            {
                                if (System.IO.Directory.Exists(capturedPath))
                                    StartScan(capturedPath);
                                else if (System.IO.File.Exists(capturedPath))
                                    StartFileScan(capturedPath);
                            });
                        }
                        menu.ShowAsContext();
                    }
                }

                GUILayout.Space(5);

                EditorGUI.BeginDisabledGroup(_selectedEntry == null);
                if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    ExportSelected();

                // Favorite toggle
                if (_selectedEntry != null)
                {
                    bool isFav = _historyService.IsFavorite(_selectedEntry.SourcePath ?? "");
                    if (GUILayout.Button(isFav ? "[*]" : "[ ]", EditorStyles.toolbarButton, GUILayout.Width(25)))
                        _historyService.ToggleFavorite(_selectedEntry.SourcePath ?? _selectedEntry.Name);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(_discoveryService?.Database?.TotalCount == 0);
                if (GUILayout.Button("Export All", EditorStyles.toolbarButton, GUILayout.Width(65)))
                    ExportAll();
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(5);

                // Bottom panel toggles
                bool hexActive = _bottomTab == BottomTab.HexViewer;
                if (GUILayout.Toggle(hexActive, "Hex", EditorStyles.toolbarButton, GUILayout.Width(30)) != hexActive)
                    _bottomTab = hexActive ? BottomTab.None : BottomTab.HexViewer;

                bool cmpActive = _bottomTab == BottomTab.Comparison;
                if (GUILayout.Toggle(cmpActive, "Cmp", EditorStyles.toolbarButton, GUILayout.Width(30)) != cmpActive)
                    _bottomTab = cmpActive ? BottomTab.None : BottomTab.Comparison;

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
                _assetTreePanel.Draw(_discoveryService?.Database);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawCenterPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                if (_selectedEntry?.Type == AssetType.Model)
                {
                    var rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    _preview3DPanel.Draw(rect);
                }
                else
                {
                    _previewPanel.Draw(_selectedEntry);
                }
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

        private void DrawBottomPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Height(_bottomPanelHeight));
            {
                switch (_bottomTab)
                {
                    case BottomTab.HexViewer:
                        _hexViewerPanel.Draw();
                        break;
                    case BottomTab.Comparison:
                        _comparisonPanel.Draw();
                        break;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawBottomResizer()
        {
            var resizeRect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
                _resizingBottom = true;

            if (_resizingBottom)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _bottomPanelHeight -= Event.current.delta.y;
                    _bottomPanelHeight = Mathf.Clamp(_bottomPanelHeight, 100, 500);
                    Repaint();
                }
                if (Event.current.type == EventType.MouseUp)
                    _resizingBottom = false;
            }
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

                int favCount = _historyService?.GetFavorites()?.Count ?? 0;
                if (favCount > 0)
                    GUILayout.Label($"Favs: {favCount}", EditorStyles.miniLabel);

                GUILayout.Label("Drop files here", EditorStyles.miniLabel);
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
            _historyService.AddRecentPath(path);
            if (System.IO.Directory.Exists(path))
                StartScan(path);
            else if (System.IO.File.Exists(path))
                StartFileScan(path);
        }

        private void OnScanProgress(ScanProgress progress)
        {
            _scanProgress = progress.Progress;
            _statusMessage = $"Scanning: {System.IO.Path.GetFileName(progress.CurrentFile)} ({progress.FilesProcessed}/{progress.TotalFiles})";
            Repaint();
        }

        private void OnScanComplete(ScanResult result)
        {
            _isScanning = false;
            _statusMessage = $"Scan complete. Found {result.AssetsFound} assets in {result.ContainersFound} containers.";
            if (result.Errors.Count > 0)
                _statusMessage += $" ({result.Errors.Count} errors)";
            Repaint();
        }

        private void OnScanError(string error)
        {
            _isScanning = false;
            _statusMessage = $"Error: {error}";
            Repaint();
        }

        private void OnAssetSelected(AssetEntry entry)
        {
            _selectedEntry = entry;
            _historyService.NavigateTo(entry);
            UpdatePanelsForSelection();
            Repaint();
        }

        private void UpdatePanelsForSelection()
        {
            // Update hex viewer
            if (_bottomTab == BottomTab.HexViewer && _selectedEntry != null)
                _hexViewerPanel.LoadAsset(_selectedEntry);

            // Update 3D preview for models
            if (_selectedEntry?.Type == AssetType.Model)
            {
                // 3D preview would be loaded here when mesh data is available
            }
        }

        private void ExportSelected()
        {
            if (_selectedEntry == null) return;

            string outputPath = EditorUtility.SaveFilePanel("Export Asset",
                AppSettingsService.Current.LastExportPath,
                _selectedEntry.Name, "");

            if (!string.IsNullOrEmpty(outputPath))
            {
                AppSettingsService.Current.LastExportPath = System.IO.Path.GetDirectoryName(outputPath);
                var result = _exportService.ExportAsset(_selectedEntry, outputPath, new ExportOptions
                {
                    OutputDirectory = System.IO.Path.GetDirectoryName(outputPath),
                    OverwriteExisting = AppSettingsService.Current.OverwriteExisting
                });

                _statusMessage = result.Success
                    ? $"Exported: {result.OutputPath} ({result.BytesWritten} bytes)"
                    : $"Export failed: {result.ErrorMessage}";
            }
        }

        private void ExportAll()
        {
            string outputDir = EditorUtility.OpenFolderPanel("Select Export Directory",
                AppSettingsService.Current.LastExportPath, "");

            if (!string.IsNullOrEmpty(outputDir))
            {
                AppSettingsService.Current.LastExportPath = outputDir;
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
                    OverwriteExisting = AppSettingsService.Current.OverwriteExisting,
                    PreserveDirectoryStructure = AppSettingsService.Current.PreserveDirectoryStructure
                });
            }
        }
    }
}
#endif
