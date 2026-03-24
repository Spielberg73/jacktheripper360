using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Reader for STFS (Secure Transacted File System) packages.
    /// Supports CON, LIVE, and PIRS signed packages used for XBLA, DLC, saves, etc.
    /// </summary>
    public class StfsReader : IContainerReader
    {
        private Stream _stream;
        private StfsPackageType _packageType;
        private StfsVolumeDescriptor _volumeDesc;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private readonly List<StfsFileEntry> _stfsEntries = new List<StfsFileEntry>();
        private string _displayName;
        private string _titleName;

        private const int BLOCK_SIZE = 0x1000;
        private const int HASH_BLOCK_SIZE = 0x1000;

        public enum StfsPackageType
        {
            CON,
            LIVE,
            PIRS,
            Unknown
        }

        private struct StfsVolumeDescriptor
        {
            public byte BlockSeparation;
            public ushort FileTableBlockCount;
            public int FileTableBlockNumber;
            public int TotalAllocatedBlockCount;
            public int TotalUnallocatedBlockCount;
        }

        private class StfsFileEntry
        {
            public string Name;
            public byte Flags;
            public int BlocksForFile;
            public int StartingBlockNum;
            public short PathIndicator;
            public int FileSize;
            public int CreatedTimeStamp;
            public int AccessTimeStamp;
        }

        public bool CanRead(Stream stream)
        {
            if (stream.Length < 4) return false;
            stream.Seek(0, SeekOrigin.Begin);
            byte[] magic = new byte[4];
            stream.Read(magic, 0, 4);

            uint magicUint = (uint)((magic[0] << 24) | (magic[1] << 16) | (magic[2] << 8) | magic[3]);
            return magicUint == Xbox360Constants.STFS_MAGIC_CON ||
                   magicUint == Xbox360Constants.STFS_MAGIC_LIVE ||
                   magicUint == Xbox360Constants.STFS_MAGIC_PIRS;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();
            _stfsEntries.Clear();

            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                // Read package type
                reader.Seek(0);
                uint magic = reader.ReadUInt32();
                switch (magic)
                {
                    case Xbox360Constants.STFS_MAGIC_CON: _packageType = StfsPackageType.CON; break;
                    case Xbox360Constants.STFS_MAGIC_LIVE: _packageType = StfsPackageType.LIVE; break;
                    case Xbox360Constants.STFS_MAGIC_PIRS: _packageType = StfsPackageType.PIRS; break;
                    default: _packageType = StfsPackageType.Unknown; break;
                }

                // Read display name (Unicode, at offset 0x411)
                reader.Seek(0x411);
                byte[] nameBytes = reader.ReadBytes(0x80);
                _displayName = System.Text.Encoding.BigEndianUnicode.GetString(nameBytes).TrimEnd('\0');

                // Read title name (at offset 0x1691)
                reader.Seek(0x1691);
                byte[] titleBytes = reader.ReadBytes(0x80);
                _titleName = System.Text.Encoding.BigEndianUnicode.GetString(titleBytes).TrimEnd('\0');

                // Read volume descriptor (at offset 0x379)
                reader.Seek(0x379);
                ReadVolumeDescriptor(reader);

                // Parse file table
                ParseFileTable(reader);
            }

            return new ContainerInfo
            {
                Format = $"STFS ({_packageType})",
                Title = string.IsNullOrEmpty(_titleName) ? _displayName : _titleName,
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        private void ReadVolumeDescriptor(EndianBinaryReader reader)
        {
            reader.Skip(1); // descriptor size
            reader.Skip(1); // reserved
            _volumeDesc.BlockSeparation = reader.ReadByte();
            _volumeDesc.FileTableBlockCount = reader.ReadUInt16();
            reader.Skip(3); // file table block number (24-bit)
            reader.Seek(reader.Position - 3);
            _volumeDesc.FileTableBlockNumber = (int)reader.ReadUInt24();
            reader.Skip(1); // hash table offset
            _volumeDesc.TotalAllocatedBlockCount = (int)reader.ReadUInt24();
            _volumeDesc.TotalUnallocatedBlockCount = (int)reader.ReadUInt24();
        }

        private void ParseFileTable(EndianBinaryReader reader)
        {
            int currentBlock = _volumeDesc.FileTableBlockNumber;

            for (int i = 0; i < _volumeDesc.FileTableBlockCount; i++)
            {
                long blockOffset = BlockToOffset(currentBlock);
                reader.Seek(blockOffset);

                // Each file entry is 0x40 bytes, 64 entries per block
                for (int j = 0; j < 64; j++)
                {
                    long entryOffset = blockOffset + j * 0x40;
                    reader.Seek(entryOffset);

                    string name = reader.ReadString(0x28);
                    if (string.IsNullOrEmpty(name)) continue;

                    byte flags = reader.ReadByte();
                    if ((flags & 0x40) != 0) continue; // entry is not in use

                    int blocksForFile = (int)reader.ReadUInt24();
                    reader.Skip(3); // padding
                    int startingBlock = (int)reader.ReadUInt24();
                    short pathIndicator = reader.ReadInt16();
                    int fileSize = reader.ReadInt32();

                    int createdTs = reader.ReadInt32();
                    int accessTs = reader.ReadInt32();

                    bool isDirectory = (flags & 0x80) != 0;

                    var stfsEntry = new StfsFileEntry
                    {
                        Name = name,
                        Flags = flags,
                        BlocksForFile = blocksForFile,
                        StartingBlockNum = startingBlock,
                        PathIndicator = pathIndicator,
                        FileSize = fileSize,
                        CreatedTimeStamp = createdTs,
                        AccessTimeStamp = accessTs
                    };
                    _stfsEntries.Add(stfsEntry);

                    // Build path
                    string path = BuildPath(stfsEntry, _stfsEntries.Count - 1);

                    var containerEntry = new ContainerEntry
                    {
                        Name = name,
                        Path = path,
                        Offset = BlockToOffset(startingBlock),
                        Size = fileSize,
                        IsDirectory = isDirectory
                    };
                    _entries.Add(containerEntry);
                }

                currentBlock++;
            }
        }

        private string BuildPath(StfsFileEntry entry, int index)
        {
            if (entry.PathIndicator == -1 || entry.PathIndicator == 0xFFFF)
                return entry.Name;

            if (entry.PathIndicator >= 0 && entry.PathIndicator < _stfsEntries.Count)
            {
                var parent = _stfsEntries[entry.PathIndicator];
                string parentPath = BuildPath(parent, entry.PathIndicator);
                return $"{parentPath}/{entry.Name}";
            }

            return entry.Name;
        }

        private long BlockToOffset(int blockNumber)
        {
            // STFS block offset calculation
            // Accounts for hash tables interspersed with data blocks
            int adjustedBlock = blockNumber;

            if (blockNumber >= 0xAA)
                adjustedBlock += ((blockNumber / 0xAA) + 1);
            if (blockNumber >= 0x70E4)
                adjustedBlock += ((blockNumber / 0x70E4) + 1);

            // Base data offset starts after the header
            long headerSize = 0xA000; // Typical STFS header size
            if (_volumeDesc.BlockSeparation == 1)
                headerSize = 0xB000;

            return headerSize + (long)adjustedBlock * BLOCK_SIZE;
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            // For STFS, we need to reconstruct the file from potentially non-contiguous blocks
            // For simplicity, read into a MemoryStream
            byte[] data = new byte[entry.Size];
            int stfsIndex = _entries.IndexOf(entry);
            if (stfsIndex < 0 || stfsIndex >= _stfsEntries.Count)
                throw new InvalidOperationException("Entry not found.");

            var stfsEntry = _stfsEntries[stfsIndex];
            int currentBlock = stfsEntry.StartingBlockNum;
            int bytesRemaining = stfsEntry.FileSize;
            int offset = 0;

            for (int i = 0; i < stfsEntry.BlocksForFile && bytesRemaining > 0; i++)
            {
                long blockOffset = BlockToOffset(currentBlock);
                int toRead = Math.Min(BLOCK_SIZE, bytesRemaining);

                _stream.Seek(blockOffset, SeekOrigin.Begin);
                _stream.Read(data, offset, toRead);

                offset += toRead;
                bytesRemaining -= toRead;
                currentBlock++;
            }

            return new MemoryStream(data);
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }
}
