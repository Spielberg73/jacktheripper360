using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Reader for Xbox 360 DVD Filesystem (XDVDFS) used in game disc ISOs.
    /// The filesystem signature "MICROSOFT*XBOX*MEDIA" is located at sector 32.
    /// </summary>
    public class XdvdfsReader : IContainerReader
    {
        private Stream _stream;
        private long _baseOffset;
        private uint _rootDirSector;
        private uint _rootDirSize;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();

        public bool CanRead(Stream stream)
        {
            if (stream.Length < Xbox360Constants.XDVDFS_ROOT_SECTOR * Xbox360Constants.XDVDFS_SECTOR_SIZE + 20)
                return false;

            stream.Seek(Xbox360Constants.XDVDFS_ROOT_SECTOR * Xbox360Constants.XDVDFS_SECTOR_SIZE, SeekOrigin.Begin);
            byte[] magic = new byte[20];
            stream.Read(magic, 0, 20);

            for (int i = 0; i < 20; i++)
            {
                if (magic[i] != Xbox360Constants.XDVDFS_MAGIC[i])
                    return false;
            }
            return true;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            _stream = stream;
            _entries.Clear();

            // Read volume descriptor at sector 32
            long volumeDescOffset = Xbox360Constants.XDVDFS_ROOT_SECTOR * Xbox360Constants.XDVDFS_SECTOR_SIZE;
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(volumeDescOffset + 20); // Skip magic
                _rootDirSector = reader.ReadUInt32();
                _rootDirSize = reader.ReadUInt32();

                // Read file time
                reader.ReadInt64(); // filetime (ignored)

                // Parse the root directory tree
                ParseDirectory(reader, _rootDirSector, _rootDirSize, "");
            }

            return new ContainerInfo
            {
                Format = "XDVDFS (Xbox 360 DVD)",
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        private void ParseDirectory(EndianBinaryReader reader, uint sector, uint size, string parentPath)
        {
            long dirOffset = sector * Xbox360Constants.XDVDFS_SECTOR_SIZE;
            long dirEnd = dirOffset + size;

            ParseDirectoryEntry(reader, dirOffset, dirOffset, dirEnd, parentPath);
        }

        private void ParseDirectoryEntry(EndianBinaryReader reader, long entryOffset, long dirBase, long dirEnd, string parentPath)
        {
            if (entryOffset < dirBase || entryOffset >= dirEnd)
                return;

            reader.Seek(entryOffset);

            ushort leftOffset = reader.ReadUInt16();
            ushort rightOffset = reader.ReadUInt16();
            uint startSector = reader.ReadUInt32();
            uint fileSize = reader.ReadUInt32();
            byte attributes = reader.ReadByte();
            byte nameLength = reader.ReadByte();
            string name = reader.ReadString(nameLength);

            bool isDirectory = (attributes & 0x10) != 0;
            string fullPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";

            var entry = new ContainerEntry
            {
                Name = name,
                Path = fullPath,
                Offset = startSector * Xbox360Constants.XDVDFS_SECTOR_SIZE,
                Size = fileSize,
                IsDirectory = isDirectory,
                ParentIndex = -1
            };
            _entries.Add(entry);

            // If it's a directory, recurse into it
            if (isDirectory && fileSize > 0)
            {
                ParseDirectory(reader, startSector, fileSize, fullPath);
            }

            // Traverse left subtree
            if (leftOffset != 0 && leftOffset != 0xFFFF)
            {
                ParseDirectoryEntry(reader, dirBase + leftOffset * 4, dirBase, dirEnd, parentPath);
            }

            // Traverse right subtree
            if (rightOffset != 0 && rightOffset != 0xFFFF)
            {
                ParseDirectoryEntry(reader, dirBase + rightOffset * 4, dirBase, dirEnd, parentPath);
            }
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory entry as a stream.");

            return new SubStream(_stream, entry.Offset, entry.Size);
        }

        public void Dispose()
        {
            // Don't dispose the stream - we don't own it
        }
    }

    /// <summary>
    /// A read-only stream that represents a subsection of another stream.
    /// </summary>
    internal class SubStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _offset;
        private readonly long _length;
        private long _position;

        public SubStream(Stream baseStream, long offset, long length)
        {
            _baseStream = baseStream;
            _offset = offset;
            _length = length;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => _position = Math.Min(Math.Max(value, 0), _length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _length - _position;
            if (remaining <= 0) return 0;
            if (count > remaining) count = (int)remaining;

            _baseStream.Seek(_offset + _position, SeekOrigin.Begin);
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: _position = offset; break;
                case SeekOrigin.Current: _position += offset; break;
                case SeekOrigin.End: _position = _length + offset; break;
            }
            _position = Math.Min(Math.Max(_position, 0), _length);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
