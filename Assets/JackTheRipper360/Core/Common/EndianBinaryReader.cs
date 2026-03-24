using System;
using System.IO;
using System.Text;

namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// BinaryReader wrapper that supports big-endian reads for Xbox 360's PowerPC byte order.
    /// </summary>
    public class EndianBinaryReader : IDisposable
    {
        private readonly BinaryReader _reader;
        private readonly bool _bigEndian;
        private readonly bool _ownsStream;

        public Stream BaseStream => _reader.BaseStream;
        public bool IsBigEndian => _bigEndian;
        public long Position
        {
            get => _reader.BaseStream.Position;
            set => _reader.BaseStream.Position = value;
        }
        public long Length => _reader.BaseStream.Length;

        public EndianBinaryReader(Stream stream, bool bigEndian = true, bool leaveOpen = false)
        {
            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
            _bigEndian = bigEndian;
            _ownsStream = !leaveOpen;
        }

        public byte ReadByte() => _reader.ReadByte();

        public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

        public sbyte ReadSByte() => _reader.ReadSByte();

        public short ReadInt16()
        {
            short value = _reader.ReadInt16();
            return _bigEndian ? SwapInt16(value) : value;
        }

        public ushort ReadUInt16()
        {
            ushort value = _reader.ReadUInt16();
            return _bigEndian ? SwapUInt16(value) : value;
        }

        public int ReadInt32()
        {
            int value = _reader.ReadInt32();
            return _bigEndian ? SwapInt32(value) : value;
        }

        public uint ReadUInt32()
        {
            uint value = _reader.ReadUInt32();
            return _bigEndian ? SwapUInt32(value) : value;
        }

        public long ReadInt64()
        {
            long value = _reader.ReadInt64();
            return _bigEndian ? SwapInt64(value) : value;
        }

        public ulong ReadUInt64()
        {
            ulong value = _reader.ReadUInt64();
            return _bigEndian ? SwapUInt64(value) : value;
        }

        public float ReadSingle()
        {
            if (!_bigEndian)
                return _reader.ReadSingle();

            byte[] bytes = _reader.ReadBytes(4);
            Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        public double ReadDouble()
        {
            if (!_bigEndian)
                return _reader.ReadDouble();

            byte[] bytes = _reader.ReadBytes(8);
            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        public string ReadString(int length) => ReadString(length, Encoding.ASCII);

        public string ReadString(int length, Encoding encoding)
        {
            byte[] bytes = _reader.ReadBytes(length);
            return encoding.GetString(bytes).TrimEnd('\0');
        }

        public string ReadNullTerminatedString()
        {
            var sb = new StringBuilder();
            byte b;
            while ((b = _reader.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        public uint ReadUInt24()
        {
            byte[] bytes = _reader.ReadBytes(3);
            if (_bigEndian)
                return (uint)((bytes[0] << 16) | (bytes[1] << 8) | bytes[2]);
            return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16));
        }

        public void Skip(long count) => _reader.BaseStream.Seek(count, SeekOrigin.Current);

        public void Seek(long offset) => _reader.BaseStream.Seek(offset, SeekOrigin.Begin);

        public void Seek(long offset, SeekOrigin origin) => _reader.BaseStream.Seek(offset, origin);

        public void Align(int alignment)
        {
            long pos = _reader.BaseStream.Position;
            long remainder = pos % alignment;
            if (remainder != 0)
                _reader.BaseStream.Seek(alignment - remainder, SeekOrigin.Current);
        }

        // Byte swap helpers
        private static short SwapInt16(short value) =>
            (short)SwapUInt16((ushort)value);

        private static ushort SwapUInt16(ushort value) =>
            (ushort)((value >> 8) | (value << 8));

        private static int SwapInt32(int value) =>
            (int)SwapUInt32((uint)value);

        private static uint SwapUInt32(uint value) =>
            ((value >> 24) & 0xFF) |
            ((value >> 8) & 0xFF00) |
            ((value << 8) & 0xFF0000) |
            ((value << 24) & 0xFF000000);

        private static long SwapInt64(long value) =>
            (long)SwapUInt64((ulong)value);

        private static ulong SwapUInt64(ulong value) =>
            ((value >> 56) & 0xFF) |
            ((value >> 40) & 0xFF00) |
            ((value >> 24) & 0xFF0000) |
            ((value >> 8) & 0xFF000000) |
            ((value << 8) & 0xFF00000000) |
            ((value << 24) & 0xFF0000000000) |
            ((value << 40) & 0xFF000000000000) |
            ((value << 56) & 0xFF00000000000000);

        public void Dispose()
        {
            _reader?.Dispose();
        }
    }
}
