using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;

namespace JackTheRipper360.Core.Plugins.Source
{
    /// <summary>
    /// Reader for Valve VPK (Valve Pak) archives used by Source engine games.
    /// Supports VPK version 1 and version 2 directory files (_dir.vpk).
    /// Archive data may reside in the directory file itself or in numbered archive files (_000.vpk, _001.vpk, etc.).
    /// </summary>
    public class SourceVpkReader : IContainerReader
    {
        private const uint VPK_MAGIC = 0x55AA1234;
        private const ushort VPK_SELF_ARCHIVE_INDEX = 0x7FFF;
        private const ushort VPK_ENTRY_TERMINATOR = 0xFFFF;

        private Stream _stream;
        private string _basePath;
        private uint _version;
        private uint _treeSize;
        private long _treeStartOffset;
        private long _fileDataOffset;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private readonly List<VpkFileEntry> _vpkEntries = new List<VpkFileEntry>();
        private readonly HashSet<int> _archiveIndices = new HashSet<int>();

        // VPK v2 header fields
        private uint _fileDataSectionSize;
        private uint _archiveMd5SectionSize;
        private uint _otherMd5SectionSize;
        private uint _signatureSectionSize;

        private class VpkFileEntry
        {
            public string FullPath;
            public uint Crc32;
            public ushort PreloadBytes;
            public ushort ArchiveIndex;
            public uint EntryOffset;
            public uint EntryLength;
            public byte[] PreloadData;
        }

        public bool CanRead(Stream stream)
        {
            if (stream == null || stream.Length < 12)
                return false;

            long originalPosition = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                byte[] magicBytes = new byte[4];
                if (stream.Read(magicBytes, 0, 4) < 4)
                    return false;

                uint magic = (uint)(magicBytes[0] | (magicBytes[1] << 8) |
                             (magicBytes[2] << 16) | (magicBytes[3] << 24));
                return magic == VPK_MAGIC;
            }
            finally
            {
                stream.Seek(originalPosition, SeekOrigin.Begin);
            }
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _stream = stream;
            _entries.Clear();
            _vpkEntries.Clear();
            _archiveIndices.Clear();

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);

                uint magic = reader.ReadUInt32();
                if (magic != VPK_MAGIC)
                    throw new InvalidDataException(
                        $"Invalid VPK magic: 0x{magic:X8}, expected 0x{VPK_MAGIC:X8}.");

                _version = reader.ReadUInt32();
                _treeSize = reader.ReadUInt32();

                if (_version == 1)
                {
                    _treeStartOffset = 12;
                }
                else if (_version == 2)
                {
                    _fileDataSectionSize = reader.ReadUInt32();
                    _archiveMd5SectionSize = reader.ReadUInt32();
                    _otherMd5SectionSize = reader.ReadUInt32();
                    _signatureSectionSize = reader.ReadUInt32();
                    _treeStartOffset = 28;
                }
                else
                {
                    throw new InvalidDataException(
                        $"Unsupported VPK version: {_version}. Only versions 1 and 2 are supported.");
                }

                // File data embedded in _dir.vpk starts right after the tree
                _fileDataOffset = _treeStartOffset + _treeSize;

                // Parse the directory tree
                reader.Seek(_treeStartOffset);
                ParseTree(reader);
            }

            // Resolve the base path from the stream if it is a FileStream.
            // This is needed later for opening numbered archive files.
            ResolveBasePath();

            return new ContainerInfo
            {
                Format = $"VPK v{_version}",
                Title = ExtractPackageName(),
                TotalSize = stream.Length,
                EntryCount = _entries.Count
            };
        }

        public IReadOnlyList<ContainerEntry> GetEntries() => _entries.AsReadOnly();

        public Stream OpenEntry(ContainerEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (entry.IsDirectory)
                throw new InvalidOperationException("Cannot open a directory as a stream.");

            int index = _entries.IndexOf(entry);
            if (index < 0 || index >= _vpkEntries.Count)
                throw new InvalidOperationException("Entry not found in this VPK archive.");

            var vpkEntry = _vpkEntries[index];
            int totalSize = vpkEntry.PreloadBytes + (int)vpkEntry.EntryLength;
            byte[] data = new byte[totalSize];
            int writeOffset = 0;

            // Copy preload data if present
            if (vpkEntry.PreloadBytes > 0 && vpkEntry.PreloadData != null)
            {
                Buffer.BlockCopy(vpkEntry.PreloadData, 0, data, 0, vpkEntry.PreloadBytes);
                writeOffset += vpkEntry.PreloadBytes;
            }

            // Read archive data if there is an entry length
            if (vpkEntry.EntryLength > 0)
            {
                if (vpkEntry.ArchiveIndex == VPK_SELF_ARCHIVE_INDEX)
                {
                    // Data is in the _dir.vpk itself, after the tree
                    long readOffset = _fileDataOffset + vpkEntry.EntryOffset;
                    _stream.Seek(readOffset, SeekOrigin.Begin);
                    int bytesRead = ReadFully(_stream, data, writeOffset, (int)vpkEntry.EntryLength);
                    if (bytesRead < (int)vpkEntry.EntryLength)
                        throw new EndOfStreamException(
                            $"Could not read {vpkEntry.EntryLength} bytes from _dir.vpk for entry '{vpkEntry.FullPath}'.");
                }
                else
                {
                    // Data is in a numbered archive file
                    string archivePath = GetArchivePath(vpkEntry.ArchiveIndex);
                    if (archivePath == null)
                        throw new FileNotFoundException(
                            $"Cannot resolve archive path for index {vpkEntry.ArchiveIndex}. " +
                            "Ensure the VPK was opened from a file path.");

                    if (!File.Exists(archivePath))
                        throw new FileNotFoundException(
                            $"Archive file not found: {archivePath}", archivePath);

                    using (var archiveStream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        archiveStream.Seek(vpkEntry.EntryOffset, SeekOrigin.Begin);
                        int bytesRead = ReadFully(archiveStream, data, writeOffset, (int)vpkEntry.EntryLength);
                        if (bytesRead < (int)vpkEntry.EntryLength)
                            throw new EndOfStreamException(
                                $"Could not read {vpkEntry.EntryLength} bytes from '{archivePath}' for entry '{vpkEntry.FullPath}'.");
                    }
                }
            }

            return new MemoryStream(data, 0, totalSize, writable: false);
        }

        public void Dispose()
        {
            // We do not own the stream, so we do not dispose it.
        }

        #region Tree Parsing

        private void ParseTree(EndianBinaryReader reader)
        {
            long treeEnd = _treeStartOffset + _treeSize;

            while (reader.Position < treeEnd)
            {
                string extension = reader.ReadNullTerminatedString();
                if (string.IsNullOrEmpty(extension))
                    break; // End of tree

                while (reader.Position < treeEnd)
                {
                    string path = reader.ReadNullTerminatedString();
                    if (string.IsNullOrEmpty(path))
                        break; // End of paths for this extension

                    while (reader.Position < treeEnd)
                    {
                        string filename = reader.ReadNullTerminatedString();
                        if (string.IsNullOrEmpty(filename))
                            break; // End of filenames for this path

                        ReadFileEntry(reader, extension, path, filename);
                    }
                }
            }
        }

        private void ReadFileEntry(EndianBinaryReader reader, string extension, string path, string filename)
        {
            uint crc32 = reader.ReadUInt32();
            ushort preloadBytes = reader.ReadUInt16();
            ushort archiveIndex = reader.ReadUInt16();
            uint entryOffset = reader.ReadUInt32();
            uint entryLength = reader.ReadUInt32();
            ushort terminator = reader.ReadUInt16();

            if (terminator != VPK_ENTRY_TERMINATOR)
                throw new InvalidDataException(
                    $"Expected VPK entry terminator 0x{VPK_ENTRY_TERMINATOR:X4}, " +
                    $"got 0x{terminator:X4} for '{filename}.{extension}'.");

            byte[] preloadData = null;
            if (preloadBytes > 0)
            {
                preloadData = reader.ReadBytes(preloadBytes);
                if (preloadData.Length < preloadBytes)
                    throw new EndOfStreamException(
                        $"Failed to read {preloadBytes} preload bytes for '{filename}.{extension}'.");
            }

            // Build full path: "path/filename.extension"
            // A single space path means root
            string fullPath;
            if (path == " ")
                fullPath = $"{filename}.{extension}";
            else
                fullPath = $"{path}/{filename}.{extension}";

            // Track unique archive indices (excluding self)
            if (archiveIndex != VPK_SELF_ARCHIVE_INDEX)
                _archiveIndices.Add(archiveIndex);

            var vpkEntry = new VpkFileEntry
            {
                FullPath = fullPath,
                Crc32 = crc32,
                PreloadBytes = preloadBytes,
                ArchiveIndex = archiveIndex,
                EntryOffset = entryOffset,
                EntryLength = entryLength,
                PreloadData = preloadData
            };
            _vpkEntries.Add(vpkEntry);

            long totalSize = preloadBytes + (long)entryLength;

            var containerEntry = new ContainerEntry
            {
                Name = $"{filename}.{extension}",
                Path = fullPath,
                Offset = entryOffset,
                Size = totalSize,
                IsDirectory = false
            };
            _entries.Add(containerEntry);
        }

        #endregion

        #region Archive Path Resolution

        private void ResolveBasePath()
        {
            if (_stream is FileStream fileStream)
            {
                string dirFilePath = fileStream.Name;
                // VPK dir files are named like "pak01_dir.vpk"
                // Archive files are named "pak01_000.vpk", "pak01_001.vpk", etc.
                // Strip "_dir.vpk" suffix to get the base prefix
                if (dirFilePath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
                {
                    _basePath = dirFilePath.Substring(0, dirFilePath.Length - "_dir.vpk".Length);
                }
                else
                {
                    // Fallback: strip just ".vpk"
                    _basePath = System.IO.Path.ChangeExtension(dirFilePath, null);
                }
            }
        }

        private string GetArchivePath(int archiveIndex)
        {
            if (string.IsNullOrEmpty(_basePath))
                return null;

            return $"{_basePath}_{archiveIndex:D3}.vpk";
        }

        private string ExtractPackageName()
        {
            if (_stream is FileStream fileStream)
            {
                return System.IO.Path.GetFileNameWithoutExtension(fileStream.Name);
            }
            return "VPK Archive";
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Reads exactly the requested number of bytes, retrying partial reads.
        /// Returns the total number of bytes actually read.
        /// </summary>
        private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                    break; // End of stream
                totalRead += bytesRead;
            }
            return totalRead;
        }

        #endregion
    }
}
