#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace JackTheRipper360.Editor.Styles
{
    /// <summary>
    /// GUI style definitions and color constants for the JackTheRipper360 Editor UI.
    /// </summary>
    public static class JackTheRipperStyles
    {
        // Colors
        public static readonly Color HeaderColor = new Color(0.15f, 0.15f, 0.15f);
        public static readonly Color PanelColor = new Color(0.22f, 0.22f, 0.22f);
        public static readonly Color SelectedColor = new Color(0.24f, 0.48f, 0.90f);
        public static readonly Color TextureColor = new Color(0.4f, 0.8f, 0.4f);
        public static readonly Color AudioColor = new Color(0.4f, 0.6f, 0.9f);
        public static readonly Color ModelColor = new Color(0.9f, 0.6f, 0.3f);
        public static readonly Color VideoColor = new Color(0.9f, 0.4f, 0.4f);
        public static readonly Color AnimationColor = new Color(0.8f, 0.8f, 0.3f);
        public static readonly Color ContainerColor = new Color(0.6f, 0.6f, 0.6f);

        private static GUIStyle _headerStyle;
        private static GUIStyle _panelTitleStyle;
        private static GUIStyle _assetLabelStyle;
        private static GUIStyle _statusBarStyle;
        private static GUIStyle _buttonStyle;
        private static GUIStyle _searchFieldStyle;

        public static GUIStyle HeaderStyle
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 16,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(10, 10, 5, 5)
                    };
                    _headerStyle.normal.textColor = Color.white;
                }
                return _headerStyle;
            }
        }

        public static GUIStyle PanelTitleStyle
        {
            get
            {
                if (_panelTitleStyle == null)
                {
                    _panelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(5, 5, 3, 3)
                    };
                }
                return _panelTitleStyle;
            }
        }

        public static GUIStyle AssetLabelStyle
        {
            get
            {
                if (_assetLabelStyle == null)
                {
                    _assetLabelStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 11,
                        padding = new RectOffset(5, 5, 2, 2)
                    };
                }
                return _assetLabelStyle;
            }
        }

        public static GUIStyle StatusBarStyle
        {
            get
            {
                if (_statusBarStyle == null)
                {
                    _statusBarStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 10,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(10, 10, 2, 2)
                    };
                }
                return _statusBarStyle;
            }
        }

        /// <summary>
        /// Get the color associated with an asset type.
        /// </summary>
        public static Color GetAssetTypeColor(Core.Common.AssetType type)
        {
            switch (type)
            {
                case Core.Common.AssetType.Texture: return TextureColor;
                case Core.Common.AssetType.Audio: return AudioColor;
                case Core.Common.AssetType.Model: return ModelColor;
                case Core.Common.AssetType.Video: return VideoColor;
                case Core.Common.AssetType.Animation: return AnimationColor;
                case Core.Common.AssetType.Container: return ContainerColor;
                default: return Color.gray;
            }
        }

        /// <summary>
        /// Get a unicode icon character for an asset type.
        /// </summary>
        public static string GetAssetTypeIcon(Core.Common.AssetType type)
        {
            switch (type)
            {
                case Core.Common.AssetType.Texture: return "[TEX]";
                case Core.Common.AssetType.Audio: return "[SND]";
                case Core.Common.AssetType.Model: return "[MDL]";
                case Core.Common.AssetType.Video: return "[VID]";
                case Core.Common.AssetType.Animation: return "[ANM]";
                case Core.Common.AssetType.Container: return "[PKG]";
                case Core.Common.AssetType.Executable: return "[EXE]";
                case Core.Common.AssetType.Archive: return "[ARC]";
                default: return "[???]";
            }
        }
    }
}
#endif
