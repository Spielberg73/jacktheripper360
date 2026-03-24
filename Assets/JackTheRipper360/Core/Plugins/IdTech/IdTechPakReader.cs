using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;

namespace JackTheRipper360.Core.Plugins.IdTech
{
    /// <summary>
    /// Reader for id Tech PAK archive files (Quake 1/2 era).
    /// PAK files use a simple uncompressed directory structure with a "PACK" magic header.
    /// Format: 4-byte magic + 4-byte directory offset + 4-byte directory size,
    /// followed by data, then a directory table of 64-byte entries (56-byte name + offset + size).
    /// </summary>
    public class IdTechPakReader : IContainerReader
    {
        private Stream _stream;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private readonly List<PakFileEntry> _pakEntries = new List<PakFileEntry>();

        private const int PAK_HEADER_SIZE = 12;
        private const int PAK_ENTRY_SIZE = 64;
        private const int PAK_ENTRY_NAME_LENGTH = 56;

        private static readonly byte[] PakMagic = Encoding.ASCII.GetBytes("PACK");

        private int _directoryOffset;
        private int _directorySize;

        private class PakFileEntry
        {
            public string FileName;
            public int Offset;
            public int Size;
        }

        public bool CanRead(Stream stream)
        {
            if (stream.Length < PAK_HEADER_SIZE)
                return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            return magic[0] == PakMagic[0] &&
                   magic[1] == PakMagic[1] &&
                   magic[2] == PakMagic[2] &&
                   magic[3] == PakMagic[3];
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();
            _pakEntries.Clear();

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                // Read and verify magic
                reader.Seek(0);
                string magic = reader.ReadString(4);
                if (magic != "PACK")
                    throw new InvalidDataException("Not a valid PAK file: missing PACK magic.");

                // Directory offset and size are little-endian int32
                _directoryOffset = reader.ReadInt32();
                _directorySize = reader.ReadInt32();

                if (_directoryOffset < PAK_HEADER_SIZE || _directoryOffset > stream.Length)
                    throw new InvalidDataException($"Invalid PAK directory offset: 0x{_directoryOffset:X}.");

                if (_directorySize < 0 || _directoryOffset + _directorySize > stream.Length)
                    throw new InvalidDataException($"Invalid PAK directory size: {_directorySize}.");

                if (_directorySize % PAK_ENTRY_SIZE != 0)
                    throw new InvalidDataException(
                        $"PAK directory size ({_directorySize}) is not a multiple of entry size ({PAK_ENTRY_SIZE}).");

                int entryCount = _directorySize / PAK_ENTRY_SIZE;

                // Parse the directory table
                ParseDirectory(reader, entryCount);
            }

            return new ContainerInfo
            {
                Format = "id Tech PAK",
                Title = null,
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        private void ParseDirectory(EndianBinaryReader reader, int entryCount)
        {
            reader.Seek(_directoryOffset);

            for (int i = 0; i < entryCount; i++)
            {
                long entryStart = _directoryOffset + (long)i * PAK_ENTRY_SIZE;
                reader.Seek(entryStart);

                // 56-byte null-padded filename
                string fileName = reader.ReadString(PAK_ENTRY_NAME_LENGTH);

                // File offset and size (little-endian int32)
                int fileOffset = reader.ReadInt32();
                int fileSize = reader.ReadInt32();

                if (string.IsNullOrEmpty(fileName))
                    continue;

                // Normalize path separators
                fileName = fileName.Replace('\\', '/');

                var pakEntry = new PakFileEntry
                {
                    FileName = fileName,
                    Offset = fileOffset,
                    Size = fileSize
                };
                _pakEntries.Add(pakEntry);

                // Extract the leaf name from the full path
                string name = GetLeafName(fileName);

                // Determine if this looks like a directory marker (size zero, trailing slash)
                bool isDirectory = fileName.EndsWith("/") && fileSize == 0;

                var containerEntry = new ContainerEntry
                {
                    Name = name,
                    Path = fileName.TrimEnd('/'),
                    Offset = fileOffset,
                    Size = fileSize,
                    IsDirectory = isDirectory
                };
                _entries.Add(containerEntry);
            }
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

            if (_stream == null)
                throw new InvalidOperationException("No PAK file is currently loaded. Call ReadHeader first.");

            int index = _entries.IndexOf(entry);
            if (index < 0 || index >= _pakEntries.Count)
                throw new InvalidOperationException("Entry not found in this PAK archive.");

            var pakEntry = _pakEntries[index];

            if (pakEntry.Offset + pakEntry.Size > _stream.Length)
                throw new InvalidDataException(
                    $"Entry \"{pakEntry.FileName}\" extends beyond end of PAK file " +
                    $"(offset=0x{pakEntry.Offset:X}, size={pakEntry.Size}, file length={_stream.Length}).");

            // PAK files store data uncompressed; direct read into a MemoryStream
            byte[] data = new byte[pakEntry.Size];
            _stream.Seek(pakEntry.Offset, SeekOrigin.Begin);

            int totalRead = 0;
            while (totalRead < pakEntry.Size)
            {
                int bytesRead = _stream.Read(data, totalRead, pakEntry.Size - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream reading entry \"{pakEntry.FileName}\".");
                totalRead += bytesRead;
            }

            return new MemoryStream(data, writable: false);
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
