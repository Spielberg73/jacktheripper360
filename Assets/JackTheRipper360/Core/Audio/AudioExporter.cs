using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Orchestrates audio export from various Xbox 360 audio formats.
    /// </summary>
    public static class AudioExporter
    {
        /// <summary>
        /// Export audio data to WAV format (PCM only) or raw format for encoded audio.
        /// </summary>
        public static ExportResult ExportAudio(AssetEntry entry, Stream source, string outputPath, ExportOptions options)
        {
            string format = entry.FormatName?.ToUpperInvariant() ?? "";

            if (format.Contains("PCM"))
            {
                return ExportPcmAsWav(entry, source, outputPath);
            }
            else if (format.Contains("ADPCM"))
            {
                return ExportAdpcmAsWav(entry, source, outputPath);
            }
            else
            {
                // XMA, WMA, etc. - export as raw
                return ExportRaw(entry, source, outputPath);
            }
        }

        private static ExportResult ExportPcmAsWav(AssetEntry entry, Stream source, string outputPath)
        {
            int channels = entry.Metadata.ContainsKey("Channels") ? (int)entry.Metadata["Channels"] : 1;
            int sampleRate = entry.Metadata.ContainsKey("SampleRate") ? (int)entry.Metadata["SampleRate"] : 44100;
            int bitsPerSample = entry.Metadata.ContainsKey("BitsPerSample") ? (int)entry.Metadata["BitsPerSample"] : 16;

            long dataOffset = entry.Metadata.ContainsKey("DataOffset") ? (long)entry.Metadata["DataOffset"] : 0;
            int dataSize = entry.Metadata.ContainsKey("DataSize") ? (int)entry.Metadata["DataSize"] : (int)entry.Size;

            source.Seek(dataOffset, SeekOrigin.Begin);
            byte[] pcmData = new byte[dataSize];
            source.Read(pcmData, 0, dataSize);

            string wavPath = Path.ChangeExtension(outputPath, ".wav");
            WavWriter.WriteToFile(wavPath, pcmData, sampleRate, channels, bitsPerSample);
            return ExportResult.Succeeded(wavPath, pcmData.Length);
        }

        private static ExportResult ExportAdpcmAsWav(AssetEntry entry, Stream source, string outputPath)
        {
            int channels = entry.Metadata.ContainsKey("Channels") ? (int)entry.Metadata["Channels"] : 1;
            int sampleRate = entry.Metadata.ContainsKey("SampleRate") ? (int)entry.Metadata["SampleRate"] : 44100;

            long dataOffset = entry.Metadata.ContainsKey("DataOffset") ? (long)entry.Metadata["DataOffset"] : 0;
            int dataSize = entry.Metadata.ContainsKey("DataSize") ? (int)entry.Metadata["DataSize"] : (int)entry.Size;

            source.Seek(dataOffset, SeekOrigin.Begin);
            byte[] adpcmData = new byte[dataSize];
            source.Read(adpcmData, 0, dataSize);

            // Decode Xbox ADPCM to PCM
            byte[] pcmData = DecodeXboxAdpcm(adpcmData, channels);

            string wavPath = Path.ChangeExtension(outputPath, ".wav");
            WavWriter.WriteToFile(wavPath, pcmData, sampleRate, channels, 16);
            return ExportResult.Succeeded(wavPath, pcmData.Length);
        }

        /// <summary>
        /// Decode Xbox IMA ADPCM to 16-bit PCM.
        /// </summary>
        private static byte[] DecodeXboxAdpcm(byte[] adpcmData, int channels)
        {
            // Xbox ADPCM block size: 36 bytes per channel per block = 64 samples per channel
            int blockSize = 36 * channels;
            int samplesPerBlock = 64;
            int totalBlocks = adpcmData.Length / blockSize;
            int totalSamples = totalBlocks * samplesPerBlock * channels;

            byte[] pcmData = new byte[totalSamples * 2];
            int pcmOffset = 0;

            int[] stepTable = {
                7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
                34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
                157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544,
                598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878,
                2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
                6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818,
                18500, 20350, 22385, 24623, 27086, 29794, 32767
            };
            int[] indexTable = { -1, -1, -1, -1, 2, 4, 6, 8 };

            for (int block = 0; block < totalBlocks; block++)
            {
                int blockOffset = block * blockSize;

                for (int ch = 0; ch < channels; ch++)
                {
                    int chOffset = blockOffset + ch * 36;

                    // Preamble: initial predictor and step index
                    short predictor = (short)(adpcmData[chOffset] | (adpcmData[chOffset + 1] << 8));
                    int stepIndex = adpcmData[chOffset + 2];
                    stepIndex = System.Math.Max(0, System.Math.Min(88, stepIndex));

                    // Write initial sample
                    if (pcmOffset + 1 < pcmData.Length)
                    {
                        pcmData[pcmOffset] = (byte)(predictor & 0xFF);
                        pcmData[pcmOffset + 1] = (byte)((predictor >> 8) & 0xFF);
                        pcmOffset += 2;
                    }

                    // Decode 32 bytes = 64 nibbles (minus 1 for preamble sample)
                    for (int i = 4; i < 36 && pcmOffset + 1 < pcmData.Length; i++)
                    {
                        byte b = adpcmData[chOffset + i];
                        for (int nibble = 0; nibble < 2 && pcmOffset + 1 < pcmData.Length; nibble++)
                        {
                            int code = nibble == 0 ? (b & 0x0F) : ((b >> 4) & 0x0F);
                            int step = stepTable[stepIndex];

                            int diff = step >> 3;
                            if ((code & 4) != 0) diff += step;
                            if ((code & 2) != 0) diff += step >> 1;
                            if ((code & 1) != 0) diff += step >> 2;
                            if ((code & 8) != 0) diff = -diff;

                            predictor = (short)System.Math.Max(-32768, System.Math.Min(32767, predictor + diff));
                            stepIndex += indexTable[code & 7];
                            stepIndex = System.Math.Max(0, System.Math.Min(88, stepIndex));

                            pcmData[pcmOffset] = (byte)(predictor & 0xFF);
                            pcmData[pcmOffset + 1] = (byte)((predictor >> 8) & 0xFF);
                            pcmOffset += 2;
                        }
                    }
                }
            }

            return pcmData;
        }

        private static ExportResult ExportRaw(AssetEntry entry, Stream source, string outputPath)
        {
            source.Seek(0, SeekOrigin.Begin);
            byte[] data = new byte[source.Length];
            source.Read(data, 0, data.Length);

            string ext = ".bin";
            if (entry.FormatName != null)
            {
                if (entry.FormatName.Contains("XMA")) ext = ".xma";
                else if (entry.FormatName.Contains("WMA")) ext = ".wma";
            }

            string rawPath = Path.ChangeExtension(outputPath, ext);
            File.WriteAllBytes(rawPath, data);
            return ExportResult.Succeeded(rawPath, data.Length);
        }
    }
}
