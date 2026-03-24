namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Represents a single file entry inside a container (ISO, STFS, etc.)
    /// </summary>
    public class ContainerEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public int ParentIndex { get; set; } = -1;

        public override string ToString() =>
            IsDirectory ? $"[DIR] {Path}" : $"{Path} ({Size} bytes @ 0x{Offset:X})";
    }
}
