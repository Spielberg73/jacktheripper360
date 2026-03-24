using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Generic archive reader that attempts to identify and parse
    /// common archive formats used in Xbox 360 games by scanning for
    /// file tables and known patterns.
    /// </summary>
    public class GenericArchiveReader : IContainerReader
    {
        private Stream _stream;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();

        public bool CanRead(Stream stream)
        {
            if (stream.Length < 8) return false;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            stream.Read(header, 0, 8);

            // Check for common archive signatures
            // Many games use simple header + file table layouts
            uint magic = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);

            // Check for known generic archive formats
            // "BIGF" - EA BIG archive
            if (header[0] == 'B' && header[1] == 'I' && header[2] == 'G' && header[3] == 'F')
                return true;

            // "SFAR" - BioWare SFArchive
            if (header[0] == 'S' && header[1] == 'F' && header[2] == 'A' && header[3] == 'R')
                return true;

            // "ZAR\0" - common archive format
            if (header[0] == 'Z' && header[1] == 'A' && header[2] == 'R' && header[3] == 0)
                return true;

            return false;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();

            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);
                byte[] magic = reader.ReadBytes(4);

                if (magic[0] == 'B' && magic[1] == 'I' && magic[2] == 'G' && magic[3] == 'F')
                {
                    ParseBigArchive(reader);
                }
                else
                {
                    ParseGenericFileTable(reader);
                }
            }

            return new ContainerInfo
            {
                Format = "Generic Archive",
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        private void ParseBigArchive(EndianBinaryReader reader)
        {
            reader.Seek(4);
            uint totalSize = reader.ReadUInt32();
            uint fileCount = reader.ReadUInt32();
            uint headerSize = reader.ReadUInt32();

            for (int i = 0; i < fileCount && i < 10000; i++)
            {
                uint offset = reader.ReadUInt32();
                uint size = reader.ReadUInt32();
                string name = reader.ReadNullTerminatedString();

                _entries.Add(new ContainerEntry
                {
                    Name = Path.GetFileName(name),
                    Path = name,
                    Offset = offset,
                    Size = size,
                    IsDirectory = false
                });
            }
        }

        private void ParseGenericFileTable(EndianBinaryReader reader)
        {
            // Attempt heuristic parsing: look for a file count followed by
            // offset/size pairs with valid ranges
            reader.Seek(4);
            uint potentialCount = reader.ReadUInt32();

            if (potentialCount > 0 && potentialCount < 100000)
            {
                long tableStart = reader.Position;
                bool valid = true;

                for (int i = 0; i < Math.Min(potentialCount, 5); i++)
                {
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    if (offset > _stream.Length || offset + size > _stream.Length)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    reader.Seek(tableStart);
                    for (int i = 0; i < potentialCount; i++)
                    {
                        uint offset = reader.ReadUInt32();
                        uint size = reader.ReadUInt32();

                        _entries.Add(new ContainerEntry
                        {
                            Name = $"entry_{i:D4}",
                            Path = $"entry_{i:D4}",
                            Offset = offset,
                            Size = size,
                            IsDirectory = false
                        });
                    }
                }
            }
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            return new SubStream(_stream, entry.Offset, entry.Size);
        }

        public void Dispose() { }
    }
}
