using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Parser for XEX2 (Xbox 360 Executable) files.
    /// Extracts embedded resources, PE image, and metadata from XEX headers.
    /// </summary>
    public class XexParser : IContainerReader
    {
        private Stream _stream;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private XexHeader _header;
        private readonly List<XexOptionalHeader> _optionalHeaders = new List<XexOptionalHeader>();

        private struct XexHeader
        {
            public uint Magic;
            public uint ModuleFlags;
            public uint DataOffset;
            public uint Reserved;
            public uint SecurityInfoOffset;
            public uint OptionalHeaderCount;
        }

        private struct XexOptionalHeader
        {
            public uint Key;
            public uint Value;
        }

        // Optional header keys
        private const uint XEX_HEADER_RESOURCE_INFO = 0x000002FF;
        private const uint XEX_HEADER_EXECUTION_INFO = 0x00040006;
        private const uint XEX_HEADER_BASE_FILE_FORMAT = 0x000003FF;
        private const uint XEX_HEADER_ORIGINAL_PE_NAME = 0x000183FF;

        public bool CanRead(Stream stream)
        {
            if (stream.Length < 4) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            return magic[0] == Xbox360Constants.XEX2_MAGIC[0] &&
                   magic[1] == Xbox360Constants.XEX2_MAGIC[1] &&
                   magic[2] == Xbox360Constants.XEX2_MAGIC[2] &&
                   magic[3] == Xbox360Constants.XEX2_MAGIC[3];
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();
            _optionalHeaders.Clear();

            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                // Read XEX2 header
                reader.Seek(0);
                _header.Magic = reader.ReadUInt32();
                _header.ModuleFlags = reader.ReadUInt32();
                _header.DataOffset = reader.ReadUInt32();
                _header.Reserved = reader.ReadUInt32();
                _header.SecurityInfoOffset = reader.ReadUInt32();
                _header.OptionalHeaderCount = reader.ReadUInt32();

                // Read optional headers (cap at 256 to prevent loops on corrupt data)
                uint headerCount = Math.Min(_header.OptionalHeaderCount, 256);
                for (uint i = 0; i < headerCount; i++)
                {
                    if (reader.Position + 8 > reader.Length) break;
                    var optHeader = new XexOptionalHeader
                    {
                        Key = reader.ReadUInt32(),
                        Value = reader.ReadUInt32()
                    };
                    _optionalHeaders.Add(optHeader);
                }

                // Extract embedded resources
                foreach (var opt in _optionalHeaders)
                {
                    if (opt.Key == XEX_HEADER_RESOURCE_INFO)
                    {
                        ParseResourceInfo(reader, opt.Value);
                    }
                }

                // Add the PE image as an entry
                if (_header.DataOffset > 0 && _header.DataOffset < stream.Length)
                {
                    _entries.Add(new ContainerEntry
                    {
                        Name = "embedded.pe",
                        Path = "embedded.pe",
                        Offset = _header.DataOffset,
                        Size = stream.Length - _header.DataOffset,
                        IsDirectory = false
                    });
                }
            }

            return new ContainerInfo
            {
                Format = "XEX2 (Xbox 360 Executable)",
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        private void ParseResourceInfo(EndianBinaryReader reader, uint offset)
        {
            if (offset >= reader.Length) return;

            reader.Seek(offset);
            if (reader.Length - offset < 4) return;

            uint size = reader.ReadUInt32();
            if (size < 4) return;

            uint numResources = (size - 4) / 16;
            long streamLength = reader.Length;

            for (int i = 0; i < numResources; i++)
            {
                if (reader.Position + 16 > streamLength) break;

                string name = reader.ReadString(8);
                uint resourceOffset = reader.ReadUInt32();
                uint resourceSize = reader.ReadUInt32();

                // Validate bounds - skip entries that point beyond the file
                if (resourceOffset >= streamLength || resourceSize == 0)
                    continue;

                // Clamp size to available data
                if (resourceOffset + resourceSize > streamLength)
                    resourceSize = (uint)(streamLength - resourceOffset);

                _entries.Add(new ContainerEntry
                {
                    Name = name.TrimEnd('\0'),
                    Path = name.TrimEnd('\0'),
                    Offset = resourceOffset,
                    Size = resourceSize,
                    IsDirectory = false
                });
            }
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            // Validate bounds before creating SubStream
            long available = _stream.Length - entry.Offset;
            if (entry.Offset >= _stream.Length || available <= 0)
                return new MemoryStream(new byte[0]);

            long safeSize = Math.Min(entry.Size, available);
            return new SubStream(_stream, entry.Offset, safeSize);
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
