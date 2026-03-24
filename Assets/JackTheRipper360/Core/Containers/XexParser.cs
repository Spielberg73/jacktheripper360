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

                // Read optional headers
                for (int i = 0; i < _header.OptionalHeaderCount; i++)
                {
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
            reader.Seek(offset);
            uint size = reader.ReadUInt32();
            uint numResources = (size - 4) / 16;

            for (int i = 0; i < numResources; i++)
            {
                string name = reader.ReadString(8);
                uint resourceOffset = reader.ReadUInt32();
                uint resourceSize = reader.ReadUInt32();

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

            return new SubStream(_stream, entry.Offset, entry.Size);
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
