using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Containers;

namespace JackTheRipper360.Core.Plugins.Unreal
{
    /// <summary>
    /// Reader for Unreal Engine .pak archive files.
    /// Supports PAK versions 1 through 11 (UE4.0 through UE5.x).
    /// Handles uncompressed and Zlib-compressed entry extraction.
    /// </summary>
    public class UnrealPakReader : IContainerReader
    {
        private Stream _stream;
        private PakHeader _header;
        private readonly List<ContainerEntry> _entries = new List<ContainerEntry>();
        private readonly List<PakFileEntry> _pakEntries = new List<PakFileEntry>();

        private const uint PAK_MAGIC = 0x5A6F12E1;
        private const int PAK_FOOTER_SIZE_V1 = 44;    // magic(4) + version(4) + indexOffset(8) + indexSize(8) + indexHash(20)
        private const int PAK_FOOTER_SIZE_V4 = 44;    // Same layout
        private const int PAK_FOOTER_SIZE_V7 = 45;    // +encryptedIndex(1)
        private const int PAK_FOOTER_SIZE_V8 = 192;   // +compressionMethods(128)
        private const int PAK_FOOTER_SIZE_V9 = 225;   // +frozenIndex(1) + pathHashSeed(8)
        private const int SHA1_HASH_SIZE = 20;
        private const int COMPRESSION_METHOD_NAME_LEN = 32;
        private const int MAX_COMPRESSION_METHODS = 4;

        /// <summary>
        /// Compression methods used in PAK files.
        /// </summary>
        public enum PakCompressionMethod
        {
            None = 0,
            Zlib = 1,
            Gzip = 2,
            Oodle = 3,
            LZ4 = 4
        }

        /// <summary>
        /// PAK file header/footer information read from the end of the archive.
        /// </summary>
        private class PakHeader
        {
            public uint Magic;
            public int Version;
            public long IndexOffset;
            public long IndexSize;
            public byte[] IndexHash;  // SHA1 (20 bytes)
            public bool EncryptedIndex;
            public string MountPoint;
            public string[] CompressionMethods;
            public bool FrozenIndex;
            public ulong PathHashSeed;
        }

        /// <summary>
        /// Internal representation of a PAK file entry with full metadata.
        /// </summary>
        private class PakFileEntry
        {
            public string FileName;
            public long Offset;
            public long CompressedSize;
            public long UncompressedSize;
            public int CompressionMethodIndex;
            public byte[] Hash;  // SHA1
            public PakCompressionBlock[] CompressionBlocks;
            public uint Flags;
            public uint CompressionBlockSize;
            public bool IsEncrypted;
        }

        /// <summary>
        /// Describes a single compression block within a compressed entry.
        /// </summary>
        private struct PakCompressionBlock
        {
            public long StartOffset;
            public long EndOffset;
        }

        public bool CanRead(Stream stream)
        {
            if (stream == null || !stream.CanSeek || !stream.CanRead)
                return false;

            // The PAK footer resides at the end of the file.
            // Try the smallest footer size first to locate the magic.
            if (stream.Length < PAK_FOOTER_SIZE_V1)
                return false;

            try
            {
                // Try footer sizes from largest to smallest to find the magic.
                int[] footerSizes = { PAK_FOOTER_SIZE_V9, PAK_FOOTER_SIZE_V8, PAK_FOOTER_SIZE_V7, PAK_FOOTER_SIZE_V4 };
                foreach (int footerSize in footerSizes)
                {
                    if (stream.Length < footerSize)
                        continue;

                    stream.Seek(-footerSize, SeekOrigin.End);
                    byte[] magicBytes = new byte[4];
                    if (stream.Read(magicBytes, 0, 4) != 4)
                        continue;

                    uint magic = BitConverter.ToUInt32(magicBytes, 0);
                    if (magic == PAK_MAGIC)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public ContainerInfo ReadHeader(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek || !stream.CanRead)
                throw new ArgumentException("Stream must be seekable and readable.");

            _stream = stream;
            _entries.Clear();
            _pakEntries.Clear();

            _header = ReadFooter(stream);

            if (_header.EncryptedIndex)
            {
                // We cannot parse encrypted indices without the AES key.
                return new ContainerInfo
                {
                    Format = $"Unreal PAK v{_header.Version} (Encrypted Index)",
                    Title = _header.MountPoint ?? "",
                    TotalSize = stream.Length,
                    EntryCount = 0
                };
            }

            if (_header.FrozenIndex)
            {
                // Frozen index uses a serialized format that differs from the standard index.
                return new ContainerInfo
                {
                    Format = $"Unreal PAK v{_header.Version} (Frozen Index)",
                    Title = _header.MountPoint ?? "",
                    TotalSize = stream.Length,
                    EntryCount = 0
                };
            }

            ParseIndex(stream);

            return new ContainerInfo
            {
                Format = $"Unreal PAK v{_header.Version}",
                Title = _header.MountPoint ?? "",
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
            if (_stream == null)
                throw new InvalidOperationException("No PAK file is currently open. Call ReadHeader first.");

            int index = _entries.IndexOf(entry);
            if (index < 0 || index >= _pakEntries.Count)
                throw new InvalidOperationException("Entry not found in this PAK archive.");

            PakFileEntry pakEntry = _pakEntries[index];

            if (pakEntry.IsEncrypted)
                throw new NotSupportedException(
                    $"Entry '{pakEntry.FileName}' is encrypted. Decryption requires an AES key which is not available.");

            PakCompressionMethod method = ResolveCompressionMethod(pakEntry.CompressionMethodIndex);

            if (method == PakCompressionMethod.None)
            {
                return ReadUncompressedEntry(pakEntry);
            }
            else if (method == PakCompressionMethod.Zlib)
            {
                return DecompressZlibEntry(pakEntry);
            }
            else if (method == PakCompressionMethod.Gzip)
            {
                return DecompressGzipEntry(pakEntry);
            }
            else
            {
                throw new NotSupportedException(
                    $"Compression method '{method}' is not supported for extraction. " +
                    $"Entry: '{pakEntry.FileName}'.");
            }
        }

        public void Dispose()
        {
            // We do not own the stream; caller is responsible for disposal.
        }

        #region Footer Reading

        /// <summary>
        /// Reads the PAK footer from the end of the stream to determine the version
        /// and locate the index.
        /// </summary>
        private PakHeader ReadFooter(Stream stream)
        {
            var header = new PakHeader();
            header.CompressionMethods = new string[0];

            // Determine the PAK version by trying footer sizes from largest to smallest.
            int footerSize = DetermineFooterSize(stream);
            if (footerSize == 0)
                throw new InvalidDataException("Unable to locate PAK footer magic. Not a valid Unreal PAK file.");

            stream.Seek(-footerSize, SeekOrigin.End);
            long footerOffset = stream.Position;

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(footerOffset);

                header.Magic = reader.ReadUInt32();
                if (header.Magic != PAK_MAGIC)
                    throw new InvalidDataException($"Invalid PAK magic: 0x{header.Magic:X8}, expected 0x{PAK_MAGIC:X8}.");

                header.Version = reader.ReadInt32();
                if (header.Version < 1 || header.Version > 11)
                    throw new InvalidDataException($"Unsupported PAK version: {header.Version}. Supported: 1-11.");

                header.IndexOffset = reader.ReadInt64();
                header.IndexSize = reader.ReadInt64();
                header.IndexHash = reader.ReadBytes(SHA1_HASH_SIZE);

                // v7+: encrypted index flag
                if (header.Version >= 7)
                {
                    header.EncryptedIndex = reader.ReadByte() != 0;
                }

                // v8+: compression method names (4 names x 32 chars each)
                if (header.Version >= 8)
                {
                    var methods = new List<string>();
                    for (int i = 0; i < MAX_COMPRESSION_METHODS; i++)
                    {
                        string name = reader.ReadString(COMPRESSION_METHOD_NAME_LEN);
                        if (!string.IsNullOrEmpty(name))
                            methods.Add(name);
                    }
                    header.CompressionMethods = methods.ToArray();
                }

                // v9+: frozen index flag
                if (header.Version >= 9)
                {
                    header.FrozenIndex = reader.ReadByte() != 0;
                }

                // Validate index bounds
                if (header.IndexOffset < 0 || header.IndexOffset >= stream.Length)
                    throw new InvalidDataException(
                        $"PAK index offset 0x{header.IndexOffset:X} is outside the file (length: 0x{stream.Length:X}).");

                if (header.IndexOffset + header.IndexSize > stream.Length)
                    throw new InvalidDataException(
                        $"PAK index extends beyond end of file. Offset: 0x{header.IndexOffset:X}, " +
                        $"Size: 0x{header.IndexSize:X}, FileLength: 0x{stream.Length:X}.");
            }

            return header;
        }

        /// <summary>
        /// Probes the stream to find which footer size places the magic at the correct offset.
        /// </summary>
        private int DetermineFooterSize(Stream stream)
        {
            int[] candidates = { PAK_FOOTER_SIZE_V9, PAK_FOOTER_SIZE_V8, PAK_FOOTER_SIZE_V7, PAK_FOOTER_SIZE_V4 };
            byte[] buf = new byte[4];

            foreach (int size in candidates)
            {
                if (stream.Length < size)
                    continue;

                stream.Seek(-size, SeekOrigin.End);
                if (stream.Read(buf, 0, 4) == 4)
                {
                    uint magic = BitConverter.ToUInt32(buf, 0);
                    if (magic == PAK_MAGIC)
                    {
                        // Read the version to confirm the footer size is correct.
                        byte[] vBuf = new byte[4];
                        if (stream.Read(vBuf, 0, 4) == 4)
                        {
                            int version = BitConverter.ToInt32(vBuf, 0);
                            int expectedSize = GetExpectedFooterSize(version);
                            if (expectedSize == size)
                                return size;
                        }
                    }
                }
            }

            // Fallback: try the smallest footer for older versions.
            if (stream.Length >= PAK_FOOTER_SIZE_V1)
            {
                stream.Seek(-PAK_FOOTER_SIZE_V1, SeekOrigin.End);
                if (stream.Read(buf, 0, 4) == 4)
                {
                    uint magic = BitConverter.ToUInt32(buf, 0);
                    if (magic == PAK_MAGIC)
                        return PAK_FOOTER_SIZE_V1;
                }
            }

            return 0;
        }

        private static int GetExpectedFooterSize(int version)
        {
            if (version >= 9) return PAK_FOOTER_SIZE_V9;
            if (version >= 8) return PAK_FOOTER_SIZE_V8;
            if (version >= 7) return PAK_FOOTER_SIZE_V7;
            return PAK_FOOTER_SIZE_V4;
        }

        #endregion

        #region Index Parsing

        /// <summary>
        /// Parses the PAK index to enumerate all file entries.
        /// </summary>
        private void ParseIndex(Stream stream)
        {
            stream.Seek(_header.IndexOffset, SeekOrigin.Begin);

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(_header.IndexOffset);

                // Mount point
                _header.MountPoint = ReadFString(reader);

                // Entry count
                int entryCount = reader.ReadInt32();
                if (entryCount < 0 || entryCount > 1_000_000)
                    throw new InvalidDataException($"Unreasonable entry count: {entryCount}.");

                // v9+: uses PathHashIndex rather than sequential filename entries.
                // We only handle the standard index here.
                if (_header.Version >= 10)
                {
                    // v10/v11 may use FName-based path hash index.
                    // Attempt to read the path hash seed indicator.
                    ParseStandardIndex(reader, entryCount);
                }
                else
                {
                    ParseStandardIndex(reader, entryCount);
                }
            }
        }

        /// <summary>
        /// Parses the standard sequential index format (v1-v9).
        /// Each record has: filename, then entry record data.
        /// </summary>
        private void ParseStandardIndex(EndianBinaryReader reader, int entryCount)
        {
            for (int i = 0; i < entryCount; i++)
            {
                if (reader.Position >= _stream.Length - 4)
                    break;

                string fileName;
                try
                {
                    fileName = ReadFString(reader);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"Failed to read filename for entry {i} at offset 0x{reader.Position:X}.", ex);
                }

                if (string.IsNullOrEmpty(fileName))
                    continue;

                PakFileEntry pakEntry = ReadEntryRecord(reader);
                pakEntry.FileName = fileName;
                _pakEntries.Add(pakEntry);

                // Determine display name and path
                string displayName = Path.GetFileName(fileName);
                string displayPath = fileName;

                // Clean up mount point prefix
                if (_header.MountPoint != null && fileName.StartsWith(_header.MountPoint, StringComparison.OrdinalIgnoreCase))
                {
                    displayPath = fileName.Substring(_header.MountPoint.Length);
                }

                // Determine if this is a directory (rare in PAK, but handle it)
                bool isDir = fileName.EndsWith("/") || fileName.EndsWith("\\");

                PakCompressionMethod method = ResolveCompressionMethod(pakEntry.CompressionMethodIndex);
                string compressionName = method.ToString();
                if (_header.Version >= 8 && pakEntry.CompressionMethodIndex > 0 &&
                    pakEntry.CompressionMethodIndex <= _header.CompressionMethods.Length)
                {
                    compressionName = _header.CompressionMethods[pakEntry.CompressionMethodIndex - 1];
                }

                bool canExtract = !pakEntry.IsEncrypted &&
                    (method == PakCompressionMethod.None ||
                     method == PakCompressionMethod.Zlib ||
                     method == PakCompressionMethod.Gzip);

                // Use the Size field to store the uncompressed size (most useful to consumers).
                // Store the compressed size in the entry offset alongside metadata.
                var containerEntry = new ContainerEntry
                {
                    Name = displayName,
                    Path = displayPath,
                    Offset = pakEntry.Offset,
                    Size = pakEntry.UncompressedSize,
                    IsDirectory = isDir
                };

                _entries.Add(containerEntry);
            }
        }

        /// <summary>
        /// Reads a single entry record (offset, sizes, compression info, hash, flags).
        /// The format varies by PAK version.
        /// </summary>
        private PakFileEntry ReadEntryRecord(EndianBinaryReader reader)
        {
            var entry = new PakFileEntry();

            entry.Offset = reader.ReadInt64();
            entry.CompressedSize = reader.ReadInt64();
            entry.UncompressedSize = reader.ReadInt64();

            if (_header.Version <= 1)
            {
                // v1: compression stored as a 4-byte value with a legacy enum
                entry.CompressionMethodIndex = reader.ReadInt32();
            }
            else if (_header.Version < 8)
            {
                // v2-v7: compression method index (4 bytes)
                entry.CompressionMethodIndex = reader.ReadInt32();
            }
            else
            {
                // v8+: compression method is an index into the footer's CompressionMethods array
                entry.CompressionMethodIndex = reader.ReadInt32();
            }

            if (_header.Version <= 1)
            {
                // v1: 8 bytes of timestamp
                reader.Skip(8);
            }

            entry.Hash = reader.ReadBytes(SHA1_HASH_SIZE);

            // v4+: compression blocks
            if (_header.Version >= 4 && entry.CompressionMethodIndex != 0)
            {
                int blockCount = reader.ReadInt32();
                if (blockCount < 0 || blockCount > 1_000_000)
                    throw new InvalidDataException($"Unreasonable compression block count: {blockCount}.");

                entry.CompressionBlocks = new PakCompressionBlock[blockCount];
                for (int b = 0; b < blockCount; b++)
                {
                    entry.CompressionBlocks[b] = new PakCompressionBlock
                    {
                        StartOffset = reader.ReadInt64(),
                        EndOffset = reader.ReadInt64()
                    };
                }
            }
            else
            {
                entry.CompressionBlocks = Array.Empty<PakCompressionBlock>();
            }

            // v3+: flags and compression block size
            if (_header.Version >= 3)
            {
                entry.Flags = reader.ReadByte();
                entry.IsEncrypted = (entry.Flags & 0x01) != 0;
            }

            if (_header.Version >= 4)
            {
                entry.CompressionBlockSize = reader.ReadUInt32();
            }

            return entry;
        }

        #endregion

        #region Entry Extraction

        /// <summary>
        /// Reads an uncompressed entry directly from the PAK stream.
        /// </summary>
        private Stream ReadUncompressedEntry(PakFileEntry pakEntry)
        {
            // The entry offset points to the serialized FPakEntry header in the data section,
            // followed by the raw file data. We need to skip the embedded header.
            long dataOffset = CalculateDataOffset(pakEntry);
            int size = (int)pakEntry.UncompressedSize;

            if (size < 0 || size > int.MaxValue)
                throw new InvalidOperationException(
                    $"Entry '{pakEntry.FileName}' has an invalid uncompressed size: {pakEntry.UncompressedSize}.");

            byte[] data = new byte[size];
            _stream.Seek(dataOffset, SeekOrigin.Begin);
            ReadFully(_stream, data, 0, size);

            return new MemoryStream(data, writable: false);
        }

        /// <summary>
        /// Decompresses a Zlib-compressed entry, handling multiple compression blocks.
        /// </summary>
        private Stream DecompressZlibEntry(PakFileEntry pakEntry)
        {
            byte[] output = new byte[pakEntry.UncompressedSize];
            int outputOffset = 0;

            if (pakEntry.CompressionBlocks == null || pakEntry.CompressionBlocks.Length == 0)
            {
                // Single block: the entire entry is one compressed chunk.
                long dataOffset = CalculateDataOffset(pakEntry);
                int compressedSize = (int)pakEntry.CompressedSize;

                _stream.Seek(dataOffset, SeekOrigin.Begin);
                byte[] compressedData = new byte[compressedSize];
                ReadFully(_stream, compressedData, 0, compressedSize);

                DecompressZlibBlock(compressedData, output, 0, (int)pakEntry.UncompressedSize);
            }
            else
            {
                // Multiple compression blocks.
                long entryBaseOffset = pakEntry.Offset;
                int embeddedHeaderSize = GetEmbeddedHeaderSize(pakEntry);

                foreach (var block in pakEntry.CompressionBlocks)
                {
                    long blockStart = block.StartOffset;
                    long blockEnd = block.EndOffset;
                    int blockCompressedSize = (int)(blockEnd - blockStart);

                    // In v8+, block offsets are relative to the entry header.
                    // In earlier versions, they are absolute.
                    long absoluteStart;
                    if (_header.Version >= 8 || _header.Version < 4)
                    {
                        absoluteStart = entryBaseOffset + embeddedHeaderSize + blockStart;
                    }
                    else
                    {
                        absoluteStart = blockStart;
                    }

                    _stream.Seek(absoluteStart, SeekOrigin.Begin);
                    byte[] compressedBlock = new byte[blockCompressedSize];
                    ReadFully(_stream, compressedBlock, 0, blockCompressedSize);

                    int decompressedBlockSize = (int)Math.Min(
                        pakEntry.CompressionBlockSize,
                        pakEntry.UncompressedSize - outputOffset);

                    DecompressZlibBlock(compressedBlock, output, outputOffset, decompressedBlockSize);
                    outputOffset += decompressedBlockSize;
                }
            }

            return new MemoryStream(output, writable: false);
        }

        /// <summary>
        /// Decompresses a Gzip-compressed entry, handling multiple compression blocks.
        /// </summary>
        private Stream DecompressGzipEntry(PakFileEntry pakEntry)
        {
            byte[] output = new byte[pakEntry.UncompressedSize];
            int outputOffset = 0;

            if (pakEntry.CompressionBlocks == null || pakEntry.CompressionBlocks.Length == 0)
            {
                long dataOffset = CalculateDataOffset(pakEntry);
                int compressedSize = (int)pakEntry.CompressedSize;

                _stream.Seek(dataOffset, SeekOrigin.Begin);
                byte[] compressedData = new byte[compressedSize];
                ReadFully(_stream, compressedData, 0, compressedSize);

                using (var ms = new MemoryStream(compressedData))
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                {
                    ReadFully(gzip, output, 0, (int)pakEntry.UncompressedSize);
                }
            }
            else
            {
                long entryBaseOffset = pakEntry.Offset;
                int embeddedHeaderSize = GetEmbeddedHeaderSize(pakEntry);

                foreach (var block in pakEntry.CompressionBlocks)
                {
                    long blockStart = block.StartOffset;
                    int blockCompressedSize = (int)(block.EndOffset - blockStart);

                    long absoluteStart;
                    if (_header.Version >= 8 || _header.Version < 4)
                    {
                        absoluteStart = entryBaseOffset + embeddedHeaderSize + blockStart;
                    }
                    else
                    {
                        absoluteStart = blockStart;
                    }

                    _stream.Seek(absoluteStart, SeekOrigin.Begin);
                    byte[] compressedBlock = new byte[blockCompressedSize];
                    ReadFully(_stream, compressedBlock, 0, blockCompressedSize);

                    int decompressedBlockSize = (int)Math.Min(
                        pakEntry.CompressionBlockSize,
                        pakEntry.UncompressedSize - outputOffset);

                    using (var ms = new MemoryStream(compressedBlock))
                    using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                    {
                        ReadFully(gzip, output, outputOffset, decompressedBlockSize);
                    }

                    outputOffset += decompressedBlockSize;
                }
            }

            return new MemoryStream(output, writable: false);
        }

        /// <summary>
        /// Decompresses a single Zlib block. Zlib data in PAK files uses raw deflate
        /// with a 2-byte zlib header (0x78 xx).
        /// </summary>
        private static void DecompressZlibBlock(byte[] compressedData, byte[] output, int outputOffset, int expectedSize)
        {
            // Zlib format: 2-byte header, then deflate stream, then 4-byte Adler32 checksum.
            // DeflateStream expects raw deflate, so skip the 2-byte zlib header.
            int zlibHeaderSize = 0;
            if (compressedData.Length >= 2)
            {
                byte cmf = compressedData[0];
                // Check for valid zlib header (CMF byte: method=8, window size varies)
                if ((cmf & 0x0F) == 0x08)
                {
                    zlibHeaderSize = 2;
                }
            }

            using (var ms = new MemoryStream(compressedData, zlibHeaderSize, compressedData.Length - zlibHeaderSize))
            using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int totalRead = 0;
                while (totalRead < expectedSize)
                {
                    int bytesRead = deflate.Read(output, outputOffset + totalRead, expectedSize - totalRead);
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }

                if (totalRead != expectedSize)
                    throw new InvalidDataException(
                        $"Zlib decompression produced {totalRead} bytes, expected {expectedSize}.");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Calculates the absolute offset to the raw file data within the PAK,
        /// skipping the embedded entry header that precedes each file's data.
        /// </summary>
        private long CalculateDataOffset(PakFileEntry pakEntry)
        {
            // Each entry in the data section starts with an embedded FPakEntry header,
            // then the actual data follows.
            return pakEntry.Offset + GetEmbeddedHeaderSize(pakEntry);
        }

        /// <summary>
        /// Returns the size of the embedded FPakEntry header that precedes each file's data.
        /// This varies by PAK version.
        /// </summary>
        private int GetEmbeddedHeaderSize(PakFileEntry pakEntry)
        {
            // Base: offset(8) + compressedSize(8) + uncompressedSize(8) + compressionMethod(4) + hash(20) = 48
            int size = 8 + 8 + 8 + 4 + SHA1_HASH_SIZE;

            if (_header.Version <= 1)
            {
                // v1 includes a timestamp
                size += 8;
            }

            // v4+: compression blocks and block size
            if (_header.Version >= 4 && pakEntry.CompressionMethodIndex != 0)
            {
                // block count (4) + blocks (16 each)
                int blockCount = pakEntry.CompressionBlocks?.Length ?? 0;
                size += 4 + blockCount * 16;
            }

            // v3+: flags byte
            if (_header.Version >= 3)
            {
                size += 1;
            }

            // v4+: compression block size
            if (_header.Version >= 4)
            {
                size += 4;
            }

            return size;
        }

        /// <summary>
        /// Resolves a compression method index to the corresponding enum value.
        /// For v8+, the index references the footer's CompressionMethods array;
        /// for older versions, it maps directly to the enum.
        /// </summary>
        private PakCompressionMethod ResolveCompressionMethod(int methodIndex)
        {
            if (methodIndex == 0)
                return PakCompressionMethod.None;

            if (_header.Version >= 8 && _header.CompressionMethods != null)
            {
                // methodIndex is 1-based for the compression methods array in v8+.
                int arrayIndex = methodIndex - 1;
                if (arrayIndex >= 0 && arrayIndex < _header.CompressionMethods.Length)
                {
                    string name = _header.CompressionMethods[arrayIndex];
                    if (name.Equals("Zlib", StringComparison.OrdinalIgnoreCase))
                        return PakCompressionMethod.Zlib;
                    if (name.Equals("Gzip", StringComparison.OrdinalIgnoreCase))
                        return PakCompressionMethod.Gzip;
                    if (name.Equals("Oodle", StringComparison.OrdinalIgnoreCase))
                        return PakCompressionMethod.Oodle;
                    if (name.Equals("LZ4", StringComparison.OrdinalIgnoreCase))
                        return PakCompressionMethod.LZ4;
                    // Unknown method name - return based on index
                }
            }

            // Pre-v8: direct enum mapping
            if (Enum.IsDefined(typeof(PakCompressionMethod), methodIndex))
                return (PakCompressionMethod)methodIndex;

            return PakCompressionMethod.None;
        }

        /// <summary>
        /// Reads an FString (length-prefixed string) as used throughout Unreal's serialization.
        /// Positive length = ASCII/UTF-8, negative length = UTF-16LE (character count).
        /// </summary>
        private static string ReadFString(EndianBinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length == 0)
                return string.Empty;

            if (length < 0)
            {
                // UTF-16LE encoded string. Length is the negative character count.
                int charCount = -length;
                if (charCount > 65536)
                    throw new InvalidDataException($"FString UTF-16 length is unreasonably large: {charCount}.");

                byte[] bytes = reader.ReadBytes(charCount * 2);
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            else
            {
                // ASCII/UTF-8 encoded string. Length includes the null terminator.
                if (length > 65536)
                    throw new InvalidDataException($"FString length is unreasonably large: {length}.");

                byte[] bytes = reader.ReadBytes(length);
                // Trim the null terminator
                int end = Array.IndexOf(bytes, (byte)0);
                if (end < 0) end = bytes.Length;
                return Encoding.UTF8.GetString(bytes, 0, end);
            }
        }

        /// <summary>
        /// Reads exactly the requested number of bytes from a stream,
        /// retrying until the full count is satisfied or the stream ends.
        /// </summary>
        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream. Read {totalRead} of {count} requested bytes.");
                totalRead += bytesRead;
            }
        }

        #endregion
    }
}
