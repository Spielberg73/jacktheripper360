using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Reader for FPG (File Package Graphics) format used by Backbone Entertainment
    /// in Xbox 360 games like Sonic's Ultimate Genesis Collection.
    /// Magic: "30GF" (0x33 0x30 0x47 0x46)
    /// </summary>
    public class FpgContainerReader : IContainerReader
    {
        private Stream _stream;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        // Store original (uncompressed) sizes keyed by entry index
        private readonly List<uint> _originalSizes = new List<uint>();

        private const int ENTRY_TABLE_OFFSET = 0x800;
        private const int ENTRY_SIZE = 16;

        public bool CanRead(Stream stream)
        {
            if (stream.Length < ENTRY_TABLE_OFFSET + ENTRY_SIZE) return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            // "30GF"
            return magic[0] == 0x33 && magic[1] == 0x30 &&
                   magic[2] == 0x47 && magic[3] == 0x46;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();
            _originalSizes.Clear();

            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            stream.Read(header, 0, 8);

            uint entryCount = BitConverter.ToUInt32(header, 4);
            if (entryCount > 10000) entryCount = 10000;

            // Read entry table at offset 0x800
            stream.Seek(ENTRY_TABLE_OFFSET, SeekOrigin.Begin);
            byte[] tableData = new byte[entryCount * ENTRY_SIZE];
            int bytesRead = stream.Read(tableData, 0, tableData.Length);
            int actualEntries = bytesRead / ENTRY_SIZE;

            for (int i = 0; i < actualEntries; i++)
            {
                int off = i * ENTRY_SIZE;

                uint hash = BitConverter.ToUInt32(tableData, off);
                uint dataOffset = BitConverter.ToUInt32(tableData, off + 4);
                uint storedSize = BitConverter.ToUInt32(tableData, off + 8);
                uint originalSize = BitConverter.ToUInt32(tableData, off + 12);

                // Validate bounds
                if (dataOffset >= stream.Length || storedSize == 0)
                    continue;

                if (dataOffset + storedSize > stream.Length)
                    storedSize = (uint)(stream.Length - dataOffset);

                string name = $"texture_{_entries.Count:D3}_0x{hash:X8}";

                _entries.Add(new ContainerEntry
                {
                    Name = name,
                    Path = name,
                    Offset = dataOffset,
                    Size = storedSize,
                    IsDirectory = false,
                    ParentIndex = -1
                });
                _originalSizes.Add(originalSize);
            }

            return new ContainerInfo
            {
                Format = "FPG (Backbone Entertainment Graphics Package)",
                Title = $"FPG Archive ({_entries.Count} textures)",
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            long available = _stream.Length - entry.Offset;
            if (entry.Offset >= _stream.Length || available <= 0)
                return new MemoryStream(new byte[0]);

            long safeSize = Math.Min(entry.Size, available);

            // Find the original size for this entry
            int idx = _entries.IndexOf(entry);
            uint originalSize = (idx >= 0 && idx < _originalSizes.Count) ? _originalSizes[idx] : 0;

            // Read the stored data
            _stream.Seek(entry.Offset, SeekOrigin.Begin);
            byte[] storedData = new byte[safeSize];
            int read = _stream.Read(storedData, 0, (int)safeSize);
            if (read < safeSize)
                Array.Resize(ref storedData, read);

            // Only try decompression if stored size < original size (data is compressed)
            if (originalSize > storedSize(entry) && originalSize < 16 * 1024 * 1024)
            {
                byte[] decompressed = TryZlibDecompress(storedData);
                if (decompressed != null)
                    return new MemoryStream(decompressed);
            }

            // Return raw data
            return new MemoryStream(storedData);
        }

        private uint storedSize(ContainerEntry entry) => (uint)entry.Size;

        /// <summary>
        /// Try zlib decompression only. No raw deflate fallback (too slow/risky).
        /// </summary>
        private byte[] TryZlibDecompress(byte[] data)
        {
            if (data.Length < 6) return null;

            // Check for zlib header (0x78 0x01, 0x78 0x5E, 0x78 0x9C, 0x78 0xDA)
            if (data[0] != 0x78) return null;
            if (data[1] != 0x01 && data[1] != 0x5E && data[1] != 0x9C && data[1] != 0xDA)
                return null;

            try
            {
                // Skip 2-byte zlib header
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
