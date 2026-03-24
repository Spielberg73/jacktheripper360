using System.Collections.Generic;
using System.Linq;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Discovery
{
    /// <summary>
    /// In-memory index of all discovered assets.
    /// Provides querying, filtering, and grouping capabilities.
    /// </summary>
    public class AssetDatabase
    {
        private readonly List<AssetEntry> _entries = new List<AssetEntry>();
        private readonly object _lock = new object();

        public int TotalCount
        {
            get { lock (_lock) return _entries.Count; }
        }

        public void AddEntry(AssetEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        public IReadOnlyList<AssetEntry> GetAllEntries()
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }

        public IReadOnlyList<AssetEntry> GetByType(AssetType type)
        {
            lock (_lock)
            {
                return _entries.Where(e => e.Type == type).ToList().AsReadOnly();
            }
        }

        public IReadOnlyList<AssetEntry> Search(string query)
        {
            lock (_lock)
            {
                string lowerQuery = query.ToLowerInvariant();
                return _entries.Where(e =>
                    (e.Name != null && e.Name.ToLowerInvariant().Contains(lowerQuery)) ||
                    (e.FormatName != null && e.FormatName.ToLowerInvariant().Contains(lowerQuery)) ||
                    (e.SourcePath != null && e.SourcePath.ToLowerInvariant().Contains(lowerQuery))
                ).ToList().AsReadOnly();
            }
        }

        public Dictionary<AssetType, int> GetTypeCounts()
        {
            lock (_lock)
            {
                return _entries.GroupBy(e => e.Type)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }

        /// <summary>
        /// Get all entries flattened (including children from containers).
        /// </summary>
        public IReadOnlyList<AssetEntry> GetAllEntriesFlat()
        {
            lock (_lock)
            {
                var flat = new List<AssetEntry>();
                foreach (var entry in _entries)
                {
                    flat.Add(entry);
                    FlattenChildren(entry, flat);
                }
                return flat.AsReadOnly();
            }
        }

        private void FlattenChildren(AssetEntry parent, List<AssetEntry> flat)
        {
            foreach (var child in parent.Children)
            {
                flat.Add(child);
                FlattenChildren(child, flat);
            }
        }
    }
}
