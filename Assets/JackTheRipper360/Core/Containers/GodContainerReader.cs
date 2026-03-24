using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Containers
{
    /// <summary>
    /// Reader for Games on Demand (GOD) format.
    /// GOD containers split game content into multiple .data files
    /// which are essentially concatenated STFS-like data blocks.
    /// </summary>
    public class GodContainerReader : IContainerReader
    {
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private Stream _combinedStream;
        private XdvdfsReader _xdvdfsReader;
        private string _basePath;

        /// <summary>
        /// Check if a directory contains GOD container data files.
        /// </summary>
        public bool CanRead(Stream stream)
        {
            // GOD format is directory-based, but we check if the stream
            // contains a valid XDVDFS after reassembly
            return false; // Use CanReadDirectory instead
        }

        /// <summary>
        /// Check if a directory is a GOD container.
        /// </summary>
        public bool CanReadDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return false;

            // Look for numbered .data files
            var dataFiles = Directory.GetFiles(directoryPath, "Data*", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToArray();

            return dataFiles.Length > 0;
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            // GOD uses directory-based access, not a single stream
            throw new NotSupportedException("Use ReadFromDirectory instead.");
        }

        /// <summary>
        /// Read GOD container from a directory containing .data files.
        /// </summary>
        public ContainerInfo ReadFromDirectory(string directoryPath)
        {
            _basePath = directoryPath;
            _entries.Clear();

            // Find and sort data files
            var dataFiles = Directory.GetFiles(directoryPath, "Data*", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToArray();

            if (dataFiles.Length == 0)
                throw new InvalidOperationException("No GOD data files found.");

            // Combine data files into a single stream
            _combinedStream = new GodCombinedStream(dataFiles);

            // The combined stream should contain an XDVDFS filesystem
            _xdvdfsReader = new XdvdfsReader();
            if (_xdvdfsReader.CanRead(_combinedStream))
            {
                var info = _xdvdfsReader.ReadHeader(_combinedStream);
                foreach (var entry in _xdvdfsReader.GetEntries())
                {
                    _entries.Add(entry);
                }
                return new ContainerInfo
                {
                    Format = "GOD (Games on Demand)",
                    Title = info.Title,
                    TotalSize = _combinedStream.Length,
                    EntryCount = _entries.Count
                };
            }

            throw new InvalidOperationException("GOD container does not contain a valid XDVDFS filesystem.");
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry) => _xdvdfsReader?.OpenEntry(entry);

        public void Dispose()
        {
            _xdvdfsReader?.Dispose();
            _combinedStream?.Dispose();
        }
    }

    /// <summary>
    /// Stream that combines multiple GOD data files into a single continuous stream.
    /// </summary>
    internal class GodCombinedStream : Stream
    {
        private readonly string[] _filePaths;
        private readonly long[] _fileOffsets;
        private readonly long _totalLength;
        private long _position;

        public GodCombinedStream(string[] filePaths)
        {
            _filePaths = filePaths;
            _fileOffsets = new long[filePaths.Length];

            long offset = 0;
            for (int i = 0; i < filePaths.Length; i++)
            {
                _fileOffsets[i] = offset;
                var fileInfo = new FileInfo(filePaths[i]);
                offset += fileInfo.Length;
            }
            _totalLength = offset;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _totalLength;
        public override long Position
        {
            get => _position;
            set => _position = Math.Min(Math.Max(value, 0), _totalLength);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (count > 0 && _position < _totalLength)
            {
                int fileIndex = FindFileIndex(_position);
                if (fileIndex < 0) break;

                long localOffset = _position - _fileOffsets[fileIndex];
                long fileLength = (fileIndex + 1 < _filePaths.Length)
                    ? _fileOffsets[fileIndex + 1] - _fileOffsets[fileIndex]
                    : _totalLength - _fileOffsets[fileIndex];

                int toRead = (int)Math.Min(count, fileLength - localOffset);

                using (var fs = new FileStream(_filePaths[fileIndex], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(localOffset, SeekOrigin.Begin);
                    int bytesRead = fs.Read(buffer, offset, toRead);
                    totalRead += bytesRead;
                    offset += bytesRead;
                    count -= bytesRead;
                    _position += bytesRead;
                }
            }

            return totalRead;
        }

        private int FindFileIndex(long position)
        {
            for (int i = _filePaths.Length - 1; i >= 0; i--)
            {
                if (position >= _fileOffsets[i])
                    return i;
            }
            return -1;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: _position = offset; break;
                case SeekOrigin.Current: _position += offset; break;
                case SeekOrigin.End: _position = _totalLength + offset; break;
            }
            _position = Math.Min(Math.Max(_position, 0), _totalLength);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
