using System.Collections.Generic;

namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Universal metadata for any discovered asset within an Xbox 360 game.
    /// </summary>
    public class AssetEntry
    {
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public AssetType Type { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public string FormatName { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public List<AssetEntry> Children { get; set; } = new List<AssetEntry>();

        public AssetEntry() { }

        public AssetEntry(string name, AssetType type)
        {
            Name = name;
            Type = type;
        }

        public override string ToString() =>
            $"{Name} [{Type}] ({FormatName ?? "Unknown"}) - {Size} bytes";
    }
}
