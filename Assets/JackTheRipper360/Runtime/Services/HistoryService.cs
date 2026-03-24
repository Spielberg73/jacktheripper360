using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Runtime.Services
{
    /// <summary>
    /// Manages navigation history, recent files, and favorite assets.
    /// </summary>
    public class HistoryService
    {
        private readonly List<string> _recentPaths = new List<string>();
        private readonly HashSet<string> _favorites = new HashSet<string>();
        private readonly List<AssetEntry> _navigationHistory = new List<AssetEntry>();
        private int _historyIndex = -1;

        private const int MAX_RECENT = 20;
        private const int MAX_HISTORY = 100;

        private static readonly string DataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JackTheRipper360");

        // Navigation History (Undo/Redo)

        public void NavigateTo(AssetEntry entry)
        {
            if (entry == null) return;

            // Remove forward history when navigating to new entry
            if (_historyIndex < _navigationHistory.Count - 1)
                _navigationHistory.RemoveRange(_historyIndex + 1, _navigationHistory.Count - _historyIndex - 1);

            _navigationHistory.Add(entry);
            if (_navigationHistory.Count > MAX_HISTORY)
                _navigationHistory.RemoveAt(0);

            _historyIndex = _navigationHistory.Count - 1;
        }

        public AssetEntry GoBack()
        {
            if (_historyIndex <= 0) return null;
            _historyIndex--;
            return _navigationHistory[_historyIndex];
        }

        public AssetEntry GoForward()
        {
            if (_historyIndex >= _navigationHistory.Count - 1) return null;
            _historyIndex++;
            return _navigationHistory[_historyIndex];
        }

        public bool CanGoBack => _historyIndex > 0;
        public bool CanGoForward => _historyIndex < _navigationHistory.Count - 1;
        public AssetEntry CurrentEntry => _historyIndex >= 0 && _historyIndex < _navigationHistory.Count
            ? _navigationHistory[_historyIndex] : null;

        // Recent Files

        public IReadOnlyList<string> RecentPaths => _recentPaths.AsReadOnly();

        public void AddRecentPath(string path)
        {
            _recentPaths.Remove(path);
            _recentPaths.Insert(0, path);
            if (_recentPaths.Count > MAX_RECENT)
                _recentPaths.RemoveAt(_recentPaths.Count - 1);
        }

        // Favorites

        public bool IsFavorite(string assetPath) => _favorites.Contains(assetPath);

        public void ToggleFavorite(string assetPath)
        {
            if (!_favorites.Remove(assetPath))
                _favorites.Add(assetPath);
        }

        public void AddFavorite(string assetPath) => _favorites.Add(assetPath);
        public void RemoveFavorite(string assetPath) => _favorites.Remove(assetPath);
        public IReadOnlyCollection<string> GetFavorites() => _favorites;

        // Persistence

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataPath);

                // Save recent paths
                File.WriteAllLines(Path.Combine(DataPath, "recent.txt"), _recentPaths);

                // Save favorites
                File.WriteAllLines(Path.Combine(DataPath, "favorites.txt"), _favorites);
            }
            catch { }
        }

        public void Load()
        {
            try
            {
                string recentFile = Path.Combine(DataPath, "recent.txt");
                if (File.Exists(recentFile))
                {
                    _recentPaths.Clear();
                    _recentPaths.AddRange(File.ReadAllLines(recentFile));
                }

                string favFile = Path.Combine(DataPath, "favorites.txt");
                if (File.Exists(favFile))
                {
                    _favorites.Clear();
                    foreach (string line in File.ReadAllLines(favFile))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            _favorites.Add(line);
                    }
                }
            }
            catch { }
        }
    }
}
