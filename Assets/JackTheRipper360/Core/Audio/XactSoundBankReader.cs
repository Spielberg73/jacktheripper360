using System.Collections.Generic;
using System.IO;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Core.Audio
{
    /// <summary>
    /// Reader for XACT Sound Bank (.xsb) files.
    /// Extracts cue names and mappings to wave bank entries.
    /// </summary>
    public class XactSoundBankReader
    {
        public class SoundBankCue
        {
            public string Name { get; set; }
            public int WaveBankIndex { get; set; }
            public int TrackIndex { get; set; }
        }

        public List<SoundBankCue> ReadSoundBank(Stream stream)
        {
            var cues = new List<SoundBankCue>();

            using (var reader = new EndianBinaryReader(stream, bigEndian: true, leaveOpen: true))
            {
                reader.Seek(0);
                byte[] magic = reader.ReadBytes(4);

                // Verify SDBK magic
                if (magic[0] != 'S' || magic[1] != 'D' || magic[2] != 'B' || magic[3] != 'K')
                {
                    // Try little-endian
                    reader.Dispose();
                    return ReadSoundBankLE(stream);
                }

                uint version = reader.ReadUInt32();
                uint crc = reader.ReadUInt32();
                reader.ReadUInt32(); // last modified (low)
                reader.ReadUInt32(); // last modified (high)
                reader.ReadByte();   // platform
                ushort simpleCueCount = reader.ReadUInt16();
                ushort complexCueCount = reader.ReadUInt16();
                reader.ReadUInt16(); // unknown
                reader.ReadUInt16(); // total cue count
                byte waveBankCount = reader.ReadByte();

                reader.Skip(2); // sound count

                uint cueNameTableOffset = reader.ReadUInt32();
                reader.Skip(4); // simple cue table offset
                reader.Skip(4); // complex cue table offset
                uint cueNameOffset = reader.ReadUInt32();

                // Read cue names
                if (cueNameOffset > 0 && cueNameTableOffset > 0)
                {
                    reader.Seek(cueNameTableOffset);
                    int totalCues = simpleCueCount + complexCueCount;

                    for (int i = 0; i < totalCues && reader.Position < stream.Length - 4; i++)
                    {
                        uint nameOffset = reader.ReadUInt32();
                        long savedPos = reader.Position;

                        reader.Seek(nameOffset);
                        string name = reader.ReadNullTerminatedString();

                        cues.Add(new SoundBankCue
                        {
                            Name = name,
                            WaveBankIndex = 0,
                            TrackIndex = i
                        });

                        reader.Seek(savedPos);
                    }
                }
            }

            return cues;
        }

        private List<SoundBankCue> ReadSoundBankLE(Stream stream)
        {
            var cues = new List<SoundBankCue>();

            using (var reader = new EndianBinaryReader(stream, bigEndian: false, leaveOpen: true))
            {
                reader.Seek(0);
                string magic = reader.ReadString(4);
                if (magic != "SDBK") return cues;

                // Same structure but little-endian
                uint version = reader.ReadUInt32();
                reader.Skip(8); // CRC + timestamp
                reader.ReadByte(); // platform
                ushort simpleCueCount = reader.ReadUInt16();
                ushort complexCueCount = reader.ReadUInt16();

                // Minimal parsing - just extract what we can
                int totalCues = simpleCueCount + complexCueCount;
                for (int i = 0; i < totalCues; i++)
                {
                    cues.Add(new SoundBankCue
                    {
                        Name = $"cue_{i:D4}",
                        WaveBankIndex = 0,
                        TrackIndex = i
                    });
                }
            }

            return cues;
        }
    }
}
