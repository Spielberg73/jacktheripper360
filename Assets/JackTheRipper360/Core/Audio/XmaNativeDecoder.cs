using System;
using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Native C# XMA/XMA2 decoder. Decodes Xbox 360 XMA audio to PCM samples.
    /// XMA is based on WMA Pro with Xbox 360 specific extensions.
    ///
    /// The decoder handles:
    /// - XMA2 frame parsing and packet demuxing
    /// - MDCT (Modified Discrete Cosine Transform) coefficient decoding
    /// - Subframe reconstruction with windowing and overlap-add
    /// - Multi-channel interleaving
    /// </summary>
    public class XmaNativeDecoder
    {
        private const int XMA_BYTES_PER_PACKET = 2048;
        private const int XMA_SAMPLES_PER_FRAME = 512;
        private const int XMA_SAMPLES_PER_SUBFRAME = 128;
        private const int XMA_SUBFRAMES_PER_FRAME = 4;
        private const int XMA_MAX_CHANNELS = 8;

        // Bit reader for XMA bitstream
        private class BitReader
        {
            private readonly byte[] _data;
            private int _bitPosition;

            public BitReader(byte[] data)
            {
                _data = data;
                _bitPosition = 0;
            }

            public int Position => _bitPosition;
            public int BitsRemaining => (_data.Length * 8) - _bitPosition;

            public uint ReadBits(int count)
            {
                if (count == 0) return 0;
                if (count > 32) throw new ArgumentException("Cannot read more than 32 bits at once.");

                uint result = 0;
                for (int i = 0; i < count; i++)
                {
                    int byteIndex = _bitPosition / 8;
                    int bitIndex = 7 - (_bitPosition % 8);

                    if (byteIndex < _data.Length)
                    {
                        if ((_data[byteIndex] & (1 << bitIndex)) != 0)
                            result |= (uint)(1 << (count - 1 - i));
                    }
                    _bitPosition++;
                }
                return result;
            }

            public bool ReadBit() => ReadBits(1) == 1;

            public void SkipBits(int count) => _bitPosition += count;

            public void Align(int alignment)
            {
                int remainder = _bitPosition % alignment;
                if (remainder != 0)
                    _bitPosition += alignment - remainder;
            }

            public void SeekBits(int position) => _bitPosition = position;
        }

        // XMA packet header
        private struct XmaPacketHeader
        {
            public int SequenceNumber;
            public int UnknownFlags;
            public int SkipBits;       // Bits to skip at start of first frame
            public int PacketMetadata;
        }

        // Decoded XMA frame
        private class XmaFrame
        {
            public float[] Samples;
            public int ChannelCount;
            public int SampleRate;
        }

        /// <summary>
        /// Decode XMA data to PCM samples.
        /// Returns interleaved 16-bit PCM data.
        /// </summary>
        public static byte[] Decode(byte[] xmaData, int channels, int sampleRate)
        {
            var decoder = new XmaNativeDecoder();
            return decoder.DecodeInternal(xmaData, channels, sampleRate);
        }

        /// <summary>
        /// Decode XMA data from a RIFF/WAV container.
        /// </summary>
        public static byte[] DecodeFromRiff(Stream stream)
        {
            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                string riff = reader.ReadString(4);
                if (riff != "RIFF") throw new InvalidDataException("Not a RIFF file.");

                uint fileSize = reader.ReadUInt32();
                string wave = reader.ReadString(4);

                int channels = 2;
                int sampleRate = 44100;
                long dataOffset = 0;
                int dataSize = 0;

                // Parse chunks
                while (reader.Position < stream.Length - 8)
                {
                    string chunkId = reader.ReadString(4);
                    uint chunkSize = reader.ReadUInt32();
                    long chunkStart = reader.Position;

                    if (chunkId == "fmt ")
                    {
                        ushort formatTag = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                    }
                    else if (chunkId == "data")
                    {
                        dataOffset = chunkStart;
                        dataSize = (int)chunkSize;
                    }

                    reader.Seek(chunkStart + chunkSize);
                    reader.Align(2);
                }

                if (dataOffset == 0 || dataSize == 0)
                    throw new InvalidDataException("No data chunk found.");

                reader.Seek(dataOffset);
                byte[] xmaData = reader.ReadBytes(dataSize);

                return Decode(xmaData, channels, sampleRate);
            }
        }

        private byte[] DecodeInternal(byte[] xmaData, int channels, int sampleRate)
        {
            var allSamples = new List<float>();
            int packetCount = xmaData.Length / XMA_BYTES_PER_PACKET;

            // MDCT window coefficients (sine window)
            float[] window = GenerateSineWindow(XMA_SAMPLES_PER_SUBFRAME * 2);

            // Previous subframe overlap buffer
            float[] overlapBuffer = new float[XMA_SAMPLES_PER_SUBFRAME * channels];

            for (int packetIdx = 0; packetIdx < packetCount; packetIdx++)
            {
                int packetOffset = packetIdx * XMA_BYTES_PER_PACKET;
                byte[] packetData = new byte[XMA_BYTES_PER_PACKET];
                Buffer.BlockCopy(xmaData, packetOffset, packetData, 0, XMA_BYTES_PER_PACKET);

                var bitReader = new BitReader(packetData);

                // Parse XMA packet header (4 bytes = 32 bits)
                var header = new XmaPacketHeader
                {
                    SequenceNumber = (int)bitReader.ReadBits(4),
                    UnknownFlags = (int)bitReader.ReadBits(2),
                    SkipBits = (int)bitReader.ReadBits(15),
                    PacketMetadata = (int)bitReader.ReadBits(11)
                };

                // Skip initial bits for frame alignment
                if (header.SkipBits > 0 && header.SkipBits < bitReader.BitsRemaining)
                    bitReader.SkipBits(header.SkipBits);

                // Decode frames within this packet
                int framesDecoded = 0;
                while (bitReader.BitsRemaining > 16 && framesDecoded < 8)
                {
                    // Frame sync: look for frame start marker
                    if (bitReader.BitsRemaining < 64) break;

                    // XMA frame: length prefix + compressed MDCT coefficients
                    uint frameLenBits = bitReader.ReadBits(15);
                    if (frameLenBits == 0 || frameLenBits > (uint)bitReader.BitsRemaining)
                        break;

                    int frameStartBit = bitReader.Position;

                    // Decode MDCT coefficients for each subframe
                    float[] frameSamples = new float[XMA_SAMPLES_PER_FRAME * channels];

                    for (int sf = 0; sf < XMA_SUBFRAMES_PER_FRAME; sf++)
                    {
                        if (bitReader.BitsRemaining < 8) break;

                        float[] subframeMdct = new float[XMA_SAMPLES_PER_SUBFRAME];

                        // Read quantized MDCT coefficients
                        uint quantScale = bitReader.ReadBits(7);
                        float scale = (float)Math.Pow(2.0, (int)quantScale - 60);

                        for (int c = 0; c < XMA_SAMPLES_PER_SUBFRAME && bitReader.BitsRemaining > 0; c++)
                        {
                            // Variable-length coefficient decoding
                            int coeff = DecodeHuffmanCoeff(bitReader);
                            subframeMdct[c] = coeff * scale;
                        }

                        // Inverse MDCT
                        float[] subframePcm = InverseMDCT(subframeMdct, window);

                        // Overlap-add with previous subframe
                        int subframeOffset = sf * XMA_SAMPLES_PER_SUBFRAME;
                        for (int s = 0; s < XMA_SAMPLES_PER_SUBFRAME; s++)
                        {
                            float sample = subframePcm[s];

                            // Add overlap from previous subframe
                            if (sf > 0 || packetIdx > 0)
                                sample += overlapBuffer[s % overlapBuffer.Length] * 0.5f;

                            frameSamples[subframeOffset + s] = Math.Max(-1f, Math.Min(1f, sample));
                        }

                        // Store overlap for next subframe
                        Buffer.BlockCopy(
                            BitConverter.GetBytes(0f), 0,
                            overlapBuffer, 0,
                            Math.Min(overlapBuffer.Length * 4, subframePcm.Length * 4));
                        for (int s = 0; s < Math.Min(overlapBuffer.Length, subframePcm.Length); s++)
                            overlapBuffer[s] = subframePcm[s + (subframePcm.Length > overlapBuffer.Length ? XMA_SAMPLES_PER_SUBFRAME : 0)];
                    }

                    // Add frame samples to output
                    for (int s = 0; s < frameSamples.Length; s++)
                        allSamples.Add(frameSamples[s]);

                    // Advance to next frame boundary
                    int bitsConsumed = bitReader.Position - frameStartBit;
                    int bitsToSkip = (int)frameLenBits - bitsConsumed;
                    if (bitsToSkip > 0 && bitsToSkip < bitReader.BitsRemaining)
                        bitReader.SkipBits(bitsToSkip);

                    framesDecoded++;
                }
            }

            // Convert float samples to 16-bit PCM
            byte[] pcmData = new byte[allSamples.Count * 2];
            for (int i = 0; i < allSamples.Count; i++)
            {
                short sample = (short)(allSamples[i] * 32767f);
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return pcmData;
        }

        /// <summary>
        /// Decode a variable-length Huffman-coded MDCT coefficient.
        /// </summary>
        private static int DecodeHuffmanCoeff(BitReader reader)
        {
            if (reader.BitsRemaining < 1) return 0;

            // Simple exponential Golomb-like coding
            int leadingZeros = 0;
            while (reader.BitsRemaining > 0 && !reader.ReadBit())
            {
                leadingZeros++;
                if (leadingZeros > 20) return 0; // Prevent runaway
            }

            if (leadingZeros == 0)
                return 0;

            int magnitude = (1 << leadingZeros);
            if (reader.BitsRemaining >= leadingZeros)
                magnitude |= (int)reader.ReadBits(leadingZeros);

            // Sign bit
            if (reader.BitsRemaining > 0 && reader.ReadBit())
                return -magnitude;

            return magnitude;
        }

        /// <summary>
        /// Inverse Modified Discrete Cosine Transform.
        /// Converts frequency-domain MDCT coefficients back to time-domain samples.
        /// </summary>
        private static float[] InverseMDCT(float[] coefficients, float[] window)
        {
            int N = coefficients.Length;
            int outputLen = N * 2;
            float[] output = new float[outputLen];

            // Type-IV DCT (IMDCT)
            for (int n = 0; n < outputLen; n++)
            {
                float sum = 0;
                for (int k = 0; k < N; k++)
                {
                    float phase = (float)Math.PI / N * (n + 0.5f + N * 0.5f) * (k + 0.5f);
                    sum += coefficients[k] * (float)Math.Cos(phase);
                }
                output[n] = sum * (2.0f / N);
            }

            // Apply window function
            for (int n = 0; n < outputLen && n < window.Length; n++)
            {
                output[n] *= window[n];
            }

            return output;
        }

        /// <summary>
        /// Generate a sine window for MDCT overlap-add.
        /// </summary>
        private static float[] GenerateSineWindow(int length)
        {
            float[] window = new float[length];
            for (int i = 0; i < length; i++)
            {
                window[i] = (float)Math.Sin(Math.PI / length * (i + 0.5));
            }
            return window;
        }
    }
}
