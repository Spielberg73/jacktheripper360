#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Models;
using JackTheRipper360.Runtime.Preview;

namespace JackTheRipper360.Editor.Panels
{
    /// <summary>
    /// Interactive 3D viewport for previewing extracted meshes.
    /// Uses PreviewRenderUtility for rendering without a scene object.
    /// </summary>
    public class Preview3DPanel
    {
        private PreviewRenderUtility _previewUtility;
        private Mesh _previewMesh;
        private Material _previewMaterial;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private GameObject _previewObject;

        // Camera control
        private float _cameraDistance = 5f;
        private float _rotationX = 30f;
        private float _rotationY = -45f;
        private Vector3 _pivotPoint = Vector3.zero;
        private Vector2 _lastMousePos;
        private bool _isDragging;

        // Lighting
        private float _lightIntensity = 1.0f;
        private Color _lightColor = Color.white;
        private bool _showWireframe;
        private bool _showGrid = true;

        public void SetMesh(MeshData meshData)
        {
            CleanupPreview();

            if (meshData == null || meshData.Vertices.Count == 0) return;

            _previewMesh = ModelPreviewRenderer.CreateMesh(meshData);
            if (_previewMesh == null) return;

            InitializePreview();

            // Auto-frame the mesh
            Bounds bounds = _previewMesh.bounds;
            _pivotPoint = bounds.center;
            _cameraDistance = bounds.extents.magnitude * 2.5f;
        }

        private void InitializePreview()
        {
            if (_previewUtility == null)
            {
                _previewUtility = new PreviewRenderUtility();
                _previewUtility.cameraFieldOfView = 30f;
                _previewUtility.camera.nearClipPlane = 0.01f;
                _previewUtility.camera.farClipPlane = 1000f;
                _previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
                _previewUtility.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            }

            if (_previewMaterial == null)
            {
                _previewMaterial = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.7f, 0.7f, 0.7f)
                };
            }

            if (_previewObject != null)
                Object.DestroyImmediate(_previewObject);

            _previewObject = new GameObject("Preview");
            _meshFilter = _previewObject.AddComponent<MeshFilter>();
            _meshRenderer = _previewObject.AddComponent<MeshRenderer>();
            _meshFilter.sharedMesh = _previewMesh;
            _meshRenderer.sharedMaterial = _previewMaterial;

            _previewUtility.AddSingleGO(_previewObject);
        }

        public void Draw(Rect rect)
        {
            if (_previewUtility == null || _previewMesh == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "No mesh loaded", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Handle input
            HandleInput(rect);

            // Draw toolbar
            DrawToolbar(ref rect);

            // Update camera position
            UpdateCamera();

            // Render
            _previewUtility.BeginPreview(rect, GUIStyle.none);

            // Draw grid
            if (_showGrid)
                DrawGrid();

            _previewUtility.Render();

            // Draw wireframe overlay
            if (_showWireframe && _previewMesh != null)
            {
                GL.wireframe = true;
                _previewUtility.Render();
                GL.wireframe = false;
            }

            var previewTexture = _previewUtility.EndPreview();
            GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);

            // Overlay info
            var infoRect = new Rect(rect.x + 5, rect.y + 5, 200, 60);
            GUI.Label(infoRect, $"Vertices: {_previewMesh.vertexCount}\n" +
                               $"Triangles: {_previewMesh.triangles.Length / 3}\n" +
                               $"SubMeshes: {_previewMesh.subMeshCount}",
                EditorStyles.whiteMiniLabel);
        }

        private void DrawToolbar(ref Rect rect)
        {
            var toolbarRect = new Rect(rect.x, rect.y, rect.width, 20);
            rect = new Rect(rect.x, rect.y + 20, rect.width, rect.height - 20);

            GUILayout.BeginArea(toolbarRect);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                _showWireframe = GUILayout.Toggle(_showWireframe, "Wireframe", EditorStyles.toolbarButton);
                _showGrid = GUILayout.Toggle(_showGrid, "Grid", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton))
                {
                    _rotationX = 30f;
                    _rotationY = -45f;
                    if (_previewMesh != null)
                    {
                        _pivotPoint = _previewMesh.bounds.center;
                        _cameraDistance = _previewMesh.bounds.extents.magnitude * 2.5f;
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void HandleInput(Rect rect)
        {
            Event e = Event.current;

            if (!rect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 || e.button == 1)
                    {
                        _isDragging = true;
                        _lastMousePos = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        Vector2 delta = e.mousePosition - _lastMousePos;
                        _lastMousePos = e.mousePosition;

                        if (e.button == 0) // Left click: rotate
                        {
                            _rotationY += delta.x * 0.5f;
                            _rotationX += delta.y * 0.5f;
                            _rotationX = Mathf.Clamp(_rotationX, -89f, 89f);
                        }
                        else if (e.button == 1) // Right click: pan
                        {
                            float panSpeed = _cameraDistance * 0.002f;
                            Vector3 right = _previewUtility.camera.transform.right;
                            Vector3 up = _previewUtility.camera.transform.up;
                            _pivotPoint -= right * delta.x * panSpeed;
                            _pivotPoint += up * delta.y * panSpeed;
                        }

                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    _isDragging = false;
                    break;

                case EventType.ScrollWheel:
                    _cameraDistance *= (1f + e.delta.y * 0.05f);
                    _cameraDistance = Mathf.Clamp(_cameraDistance, 0.1f, 500f);
                    e.Use();
                    break;
            }
        }

        private void UpdateCamera()
        {
            float radX = _rotationX * Mathf.Deg2Rad;
            float radY = _rotationY * Mathf.Deg2Rad;

            Vector3 cameraOffset = new Vector3(
                Mathf.Sin(radY) * Mathf.Cos(radX),
                Mathf.Sin(radX),
                Mathf.Cos(radY) * Mathf.Cos(radX)
            ) * _cameraDistance;

            _previewUtility.camera.transform.position = _pivotPoint + cameraOffset;
            _previewUtility.camera.transform.LookAt(_pivotPoint);

            // Update lights
            var lights = _previewUtility.lights;
            if (lights != null && lights.Length > 0)
            {
                lights[0].intensity = _lightIntensity;
                lights[0].color = _lightColor;
                lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private void DrawGrid()
        {
            // Grid is rendered by the preview utility background
        }

        public void CleanupPreview()
        {
            if (_previewObject != null)
            {
                Object.DestroyImmediate(_previewObject);
                _previewObject = null;
            }

            if (_previewMesh != null)
            {
                Object.DestroyImmediate(_previewMesh);
                _previewMesh = null;
            }

            if (_previewMaterial != null)
            {
                Object.DestroyImmediate(_previewMaterial);
                _previewMaterial = null;
            }
        }

        public void Dispose()
        {
            CleanupPreview();
            _previewUtility?.Cleanup();
            _previewUtility = null;
        }
    }
}
#endif
