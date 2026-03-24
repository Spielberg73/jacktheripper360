using System.IO;
using System.Text;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Writes PCM audio data as a standard WAV file.
    /// </summary>
    public static class WavWriter
    {
        public static byte[] CreateWav(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
        {
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // RIFF header
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcmData.Length); // chunk size
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt sub-chunk
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // sub-chunk size
                writer.Write((short)1); // PCM format
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)bitsPerSample);

                // data sub-chunk
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcmData.Length);
                writer.Write(pcmData);

                return ms.ToArray();
            }
        }

        public static void WriteToFile(string path, byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
        {
            byte[] wavData = CreateWav(pcmData, sampleRate, channels, bitsPerSample);
            File.WriteAllBytes(path, wavData);
        }
    }
}
