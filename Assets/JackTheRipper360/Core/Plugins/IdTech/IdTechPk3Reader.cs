using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;

namespace JackTheRipper360.Core.Plugins.IdTech
{
    /// <summary>
    /// Reader for id Tech 3/4 PK3 and PK4 archive files (Quake 3, Doom 3, etc.).
    /// PK3/PK4 files are standard ZIP archives used by id Tech engines to package game assets.
    /// Supported extensions: .pk3, .pk4, .zip (when found inside game directories).
    /// </summary>
    public class IdTechPk3Reader : IContainerReader
    {
        private Stream _stream;
        private ZipArchive _zipArchive;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private readonly List<Pk3EntryInfo> _pk3Entries = new List<Pk3EntryInfo>();

        /// <summary>
        /// ZIP local file header magic: PK\x03\x04 (0x504B0304 little-endian).
        /// </summary>
        private const uint ZipMagic = 0x04034B50;

        private class Pk3EntryInfo
        {
            public string FullName;
            public long CompressedSize;
            public long UncompressedSize;
            public CompressionMethodType CompressionMethod;
            public uint Crc32;
            public bool IsDirectory;
        }

        /// <summary>
        /// Compression methods relevant to PK3/PK4 ZIP entries.
        /// </summary>
        public enum CompressionMethodType
        {
            /// <summary>No compression (stored).</summary>
            Stored = 0,

            /// <summary>Deflate compression.</summary>
            Deflated = 8,

            /// <summary>Unknown or unsupported method.</summary>
            Unknown = -1
        }

        public bool CanRead(Stream stream)
        {
            if (stream.Length < 4)
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            // ZIP magic is stored little-endian: 50 4B 03 04
            uint value = (uint)(magic[0] | (magic[1] << 8) | (magic[2] << 16) | (magic[3] << 24));
            return value == ZipMagic;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();
            _pk3Entries.Clear();

            // Dispose any previously opened archive
            if (_zipArchive != null)
            {
                _zipArchive.Dispose();
                _zipArchive = null;
            }

            stream.Seek(0, SeekOrigin.Begin);

            // Open as a ZipArchive in read mode, leaving the underlying stream open
            _zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (ZipArchiveEntry zipEntry in _zipArchive.Entries)
            {
                bool isDirectory = string.IsNullOrEmpty(zipEntry.Name) ||
                                   zipEntry.FullName.EndsWith("/");

                // Map the .NET CompressionLevel/method heuristic
                CompressionMethodType compressionMethod = DetermineCompressionMethod(zipEntry);

                var pk3Entry = new Pk3EntryInfo
                {
                    FullName = zipEntry.FullName,
                    CompressedSize = zipEntry.CompressedLength,
                    UncompressedSize = zipEntry.Length,
                    CompressionMethod = compressionMethod,
                    Crc32 = ExtractCrc32(zipEntry),
                    IsDirectory = isDirectory
                };
                _pk3Entries.Add(pk3Entry);

                string name = isDirectory
                    ? GetLeafName(zipEntry.FullName)
                    : zipEntry.Name;

                var containerEntry = new ContainerEntry
                {
                    Name = name,
                    Path = zipEntry.FullName.TrimEnd('/'),
                    Offset = 0, // ZIP entries are not at a fixed stream offset
                    Size = zipEntry.Length,
                    IsDirectory = isDirectory
                };
                _entries.Add(containerEntry);
            }

            return new ContainerInfo
            {
                Format = "id Tech PK3/PK4 (ZIP)",
                Title = null,
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        /// <summary>
        /// Determines the compression method of a ZIP entry by comparing compressed and
        /// uncompressed sizes. The .NET ZipArchive API does not directly expose the
        /// compression method field, so we infer it from the size relationship.
        /// </summary>
        private static CompressionMethodType DetermineCompressionMethod(ZipArchiveEntry entry)
        {
            // Directories have zero size
            if (entry.Length == 0 && entry.CompressedLength == 0)
                return CompressionMethodType.Stored;

            // If compressed and uncompressed sizes match, likely stored
            if (entry.CompressedLength == entry.Length)
                return CompressionMethodType.Stored;

            return CompressionMethodType.Deflated;
        }

        /// <summary>
        /// Extracts the CRC-32 value from a ZipArchiveEntry using reflection, as the
        /// .NET API does not expose it directly. Returns 0 if extraction fails.
        /// </summary>
        private static uint ExtractCrc32(ZipArchiveEntry entry)
        {
            try
            {
                // The Crc32 property is internal in .NET's ZipArchiveEntry
                var property = entry.GetType().GetProperty("Crc32",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (property != null)
                {
                    object value = property.GetValue(entry);
                    if (value is uint crc)
                        return crc;
                }
            }
            catch
            {
                // Reflection may fail in restricted environments; CRC is non-critical
            }

            return 0;
        }

        private static string GetLeafName(string path)
        {
            string trimmed = path.TrimEnd('/');
            int lastSlash = trimmed.LastIndexOf('/');
            return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            if (_zipArchive == null)
                throw new InvalidOperationException("No PK3/PK4 archive is currently loaded. Call ReadHeader first.");

            int index = _entries.IndexOf(entry);
            if (index < 0 || index >= _pk3Entries.Count)
                throw new InvalidOperationException("Entry not found in this PK3/PK4 archive.");

            var pk3Entry = _pk3Entries[index];
            ZipArchiveEntry zipEntry = _zipArchive.GetEntry(pk3Entry.FullName);

            if (zipEntry == null)
                throw new InvalidOperationException(
                    $"Entry \"{pk3Entry.FullName}\" was not found in the ZIP archive.");

            // Extract into a MemoryStream so the caller can seek freely
            var memoryStream = new MemoryStream();
            using (Stream zipStream = zipEntry.Open())
            {
                zipStream.CopyTo(memoryStream);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }

        public void Dispose()
        {
            if (_zipArchive != null)
            {
                _zipArchive.Dispose();
                _zipArchive = null;
            }
            // Don't dispose the underlying stream - we don't own it
        }
    }
}
