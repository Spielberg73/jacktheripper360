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

        private const uint FPG_MAGIC = 0x46473033; // "30GF" as LE uint32
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

            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            stream.Read(header, 0, 8);

            uint entryCount = BitConverter.ToUInt32(header, 4);

            // Sanity check
            if (entryCount > 10000) entryCount = 10000;

            // Read entry table at offset 0x800
            stream.Seek(ENTRY_TABLE_OFFSET, SeekOrigin.Begin);
            byte[] tableData = new byte[entryCount * ENTRY_SIZE];
            int bytesRead = stream.Read(tableData, 0, tableData.Length);
            int actualEntries = bytesRead / ENTRY_SIZE;

            for (int i = 0; i < actualEntries; i++)
            {
                int offset = i * ENTRY_SIZE;

                uint hash = BitConverter.ToUInt32(tableData, offset);
                uint dataOffset = BitConverter.ToUInt32(tableData, offset + 4);
                uint storedSize = BitConverter.ToUInt32(tableData, offset + 8);
                uint originalSize = BitConverter.ToUInt32(tableData, offset + 12);

                // Validate bounds
                if (dataOffset >= stream.Length || storedSize == 0)
                    continue;

                if (dataOffset + storedSize > stream.Length)
                    storedSize = (uint)(stream.Length - dataOffset);

                string name = $"texture_{i:D3}_0x{hash:X8}";

                // Try to determine if it's a texture by size
                string formatGuess = GuessTextureFormat(originalSize > 0 ? originalSize : storedSize);

                _entries.Add(new ContainerEntry
                {
                    Name = $"{name}.dds",
                    Path = $"{name}.dds",
                    Offset = dataOffset,
                    Size = storedSize,
                    IsDirectory = false,
                    ParentIndex = -1
                });
            }

            return new ContainerInfo
            {
                Format = "FPG (Backbone Entertainment Graphics Package)",
                Title = "FPG Archive",
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

            // Read the stored data
            _stream.Seek(entry.Offset, SeekOrigin.Begin);
            byte[] storedData = new byte[safeSize];
            int read = _stream.Read(storedData, 0, (int)safeSize);
            if (read < safeSize)
                Array.Resize(ref storedData, read);

            // Try to decompress (zlib/deflate)
            byte[] decompressed = TryDecompress(storedData);
            if (decompressed != null)
                return new MemoryStream(decompressed);

            // Return raw data if decompression failed
            return new MemoryStream(storedData);
        }

        /// <summary>
        /// Try zlib/deflate decompression. Returns null if data is not compressed.
        /// </summary>
        private byte[] TryDecompress(byte[] data)
        {
            if (data.Length < 2) return null;

            // Check for zlib header (0x78 0x01, 0x78 0x5E, 0x78 0x9C, 0x78 0xDA)
            bool hasZlibHeader = data[0] == 0x78 &&
                (data[1] == 0x01 || data[1] == 0x5E || data[1] == 0x9C || data[1] == 0xDA);

            if (hasZlibHeader)
            {
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
                catch { /* not zlib compressed */ }
            }

            // Try raw deflate (no header)
            try
            {
                using (var input = new MemoryStream(data))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    byte[] result = output.ToArray();
                    // Only accept if decompressed data is larger
                    if (result.Length > data.Length)
                        return result;
                }
            }
            catch { /* not deflate compressed */ }

            return null;
        }

        private string GuessTextureFormat(uint size)
        {
            // Common Xbox 360 texture sizes for DXT5 (1 byte per pixel)
            switch (size)
            {
                case 1048576: return "DXT5 1024x1024";
                case 524288: return "DXT5 512x1024 or 1024x512";
                case 262144: return "DXT5 512x512";
                case 131072: return "DXT5 256x512 or 512x256";
                case 65536: return "DXT5 256x256";
                case 32768: return "DXT1 256x256 or DXT5 128x256";
                case 16384: return "DXT1 128x128 or DXT5 128x128";
                default: return "Unknown";
            }
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
