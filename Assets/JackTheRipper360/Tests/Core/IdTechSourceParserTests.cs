using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Core.Plugins.IdTech;
using JackTheRipper360.Core.Plugins.Source;

namespace JackTheRipper360.Tests.Core
{
    /// <summary>
    /// Tests for id Tech engine parsers, Source Engine parsers,
    /// format detection, and automatic engine detection.
    /// </summary>
    [TestFixture]
    public class IdTechSourceParserTests
    {
        #region Format Detection - id Tech

        [Test]
        public void FormatDetector_IdentifiesIdTechPak()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'P'; data[1] = (byte)'A'; data[2] = (byte)'C'; data[3] = (byte)'K';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("PAK"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesZipPk3()
        {
            byte[] data = new byte[64];
            data[0] = 0x50; data[1] = 0x4B; data[2] = 0x03; data[3] = 0x04;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Archive, match.Type);
                Assert.IsTrue(match.FormatName.Contains("ZIP") || match.FormatName.Contains("PK3"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesIWAD()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'W'; data[2] = (byte)'A'; data[3] = (byte)'D';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("IWAD"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesPWAD()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'P'; data[1] = (byte)'W'; data[2] = (byte)'A'; data[3] = (byte)'D';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("PWAD"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesWAD2()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'W'; data[1] = (byte)'A'; data[2] = (byte)'D'; data[3] = (byte)'2';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("WAD2"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesIBSP_Quake2()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'B'; data[2] = (byte)'S'; data[3] = (byte)'P';
            data[4] = 38; data[5] = 0; data[6] = 0; data[7] = 0; // version 38 LE

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("id Tech 2") || match.FormatName.Contains("Quake 2"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesIBSP_Quake3()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'B'; data[2] = (byte)'S'; data[3] = (byte)'P';
            data[4] = 46; data[5] = 0; data[6] = 0; data[7] = 0; // version 46 LE

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("id Tech 3") || match.FormatName.Contains("Quake 3"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesMD3()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'D'; data[2] = (byte)'P'; data[3] = (byte)'3';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Model, match.Type);
                Assert.IsTrue(match.FormatName.Contains("MD3"));
            }
        }

        #endregion

        #region Format Detection - Source Engine

        [Test]
        public void FormatDetector_IdentifiesVBSP()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'V'; data[1] = (byte)'B'; data[2] = (byte)'S'; data[3] = (byte)'P';
            data[4] = 20; data[5] = 0; data[6] = 0; data[7] = 0; // version 20

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("Source"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesVPK()
        {
            byte[] data = new byte[64];
            data[0] = 0x34; data[1] = 0x12; data[2] = 0xAA; data[3] = 0x55;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("VPK"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesVTF()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'V'; data[1] = (byte)'T'; data[2] = (byte)'F'; data[3] = 0x00;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Texture, match.Type);
                Assert.IsTrue(match.FormatName.Contains("VTF"));
            }
        }

        [Test]
        public void FormatDetector_IdentifiesMDL()
        {
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'D'; data[2] = (byte)'S'; data[3] = (byte)'T';

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Model, match.Type);
                Assert.IsTrue(match.FormatName.Contains("MDL"));
            }
        }

        #endregion

        #region Extension Detection

        [Test]
        public void FormatDetector_IdTechExtensions()
        {
            string[] exts = { ".pk3", ".pk4", ".wad", ".bsp", ".md3", ".md5mesh", ".md5anim", ".mtr", ".lmp" };
            foreach (var ext in exts)
            {
                var match = FormatDetector.IdentifyByExtension($"test{ext}");
                Assert.AreNotEqual(AssetType.Unknown, match.Type, $"Extension {ext} should be recognized");
            }
        }

        [Test]
        public void FormatDetector_SourceExtensions()
        {
            string[] exts = { ".vpk", ".vtf", ".vmt", ".mdl", ".vvd", ".vtx", ".phy" };
            foreach (var ext in exts)
            {
                var match = FormatDetector.IdentifyByExtension($"test{ext}");
                Assert.AreNotEqual(AssetType.Unknown, match.Type, $"Extension {ext} should be recognized");
            }
        }

        #endregion

        #region id Tech Parser Tests

        [Test]
        public void IdTechWadParser_Type_IsContainer()
        {
            var parser = new IdTechWadParser();
            Assert.AreEqual(AssetType.Container, parser.Type);
        }

        [Test]
        public void IdTechWadParser_CanParse_ValidIWAD()
        {
            var parser = new IdTechWadParser();
            byte[] data = new byte[64];
            data[0] = (byte)'I'; data[1] = (byte)'W'; data[2] = (byte)'A'; data[3] = (byte)'D';

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "doom.wad"));
            }
        }

        [Test]
        public void IdTechWadParser_CanParse_InvalidData()
        {
            var parser = new IdTechWadParser();
            byte[] data = new byte[64];

            using (var stream = new MemoryStream(data))
            {
                Assert.IsFalse(parser.CanParse(stream, "random.bin"));
            }
        }

        [Test]
        public void IdTechBspParser_Type_IsContainer()
        {
            var parser = new IdTechBspParser();
            Assert.AreEqual(AssetType.Container, parser.Type);
        }

        [Test]
        public void IdTechBspParser_CanParse_ValidIBSP()
        {
            var parser = new IdTechBspParser();
            byte[] data = new byte[256];
            data[0] = (byte)'I'; data[1] = (byte)'B'; data[2] = (byte)'S'; data[3] = (byte)'P';
            data[4] = 46; // Q3 version

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "map.bsp"));
            }
        }

        [Test]
        public void IdTechPakReader_CanRead_ValidPack()
        {
            using (var reader = new IdTechPakReader())
            {
                byte[] data = new byte[64];
                data[0] = (byte)'P'; data[1] = (byte)'A'; data[2] = (byte)'C'; data[3] = (byte)'K';
                // dir offset = 12, dir size = 0
                data[4] = 12; data[8] = 0;

                using (var stream = new MemoryStream(data))
                {
                    Assert.IsTrue(reader.CanRead(stream));
                }
            }
        }

        [Test]
        public void IdTechPk3Reader_CanRead_ValidZip()
        {
            using (var reader = new IdTechPk3Reader())
            {
                byte[] data = new byte[64];
                data[0] = 0x50; data[1] = 0x4B; data[2] = 0x03; data[3] = 0x04;

                using (var stream = new MemoryStream(data))
                {
                    Assert.IsTrue(reader.CanRead(stream));
                }
            }
        }

        #endregion

        #region Source Engine Parser Tests

        [Test]
        public void SourceVtfParser_Type_IsTexture()
        {
            var parser = new SourceVtfParser();
            Assert.AreEqual(AssetType.Texture, parser.Type);
        }

        [Test]
        public void SourceVtfParser_CanParse_ValidVTF()
        {
            var parser = new SourceVtfParser();
            byte[] data = new byte[128];
            data[0] = (byte)'V'; data[1] = (byte)'T'; data[2] = (byte)'F'; data[3] = 0x00;

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "texture.vtf"));
            }
        }

        [Test]
        public void SourceMdlParser_Type_IsModel()
        {
            var parser = new SourceMdlParser();
            Assert.AreEqual(AssetType.Model, parser.Type);
        }

        [Test]
        public void SourceMdlParser_CanParse_ValidMDL()
        {
            var parser = new SourceMdlParser();
            byte[] data = new byte[512];
            data[0] = (byte)'I'; data[1] = (byte)'D'; data[2] = (byte)'S'; data[3] = (byte)'T';
            data[4] = 44; // version 44

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "model.mdl"));
            }
        }

        [Test]
        public void SourceVpkReader_CanRead_ValidVPK()
        {
            using (var reader = new SourceVpkReader())
            {
                byte[] data = new byte[64];
                data[0] = 0x34; data[1] = 0x12; data[2] = 0xAA; data[3] = 0x55;

                using (var stream = new MemoryStream(data))
                {
                    Assert.IsTrue(reader.CanRead(stream));
                }
            }
        }

        #endregion

        #region Engine Detection Tests

        [Test]
        public void EngineDetector_DetectFromFiles_IdTech3()
        {
            var files = new[]
            {
                "baseq3/pak0.pk3", "baseq3/pak1.pk3",
                "baseq3/scripts/common.shader",
                "baseq3/maps/q3dm1.bsp"
            };

            var result = EngineDetector.DetectFromFiles(files);
            Assert.AreEqual(EngineType.idTech3, result.Engine);
            Assert.Greater(result.Confidence, 0.5f);
        }

        [Test]
        public void EngineDetector_DetectFromFiles_Unity()
        {
            var files = new[]
            {
                "game_Data/level0", "game_Data/mainData",
                "game_Data/resources.assets",
                "game_Data/Managed/Assembly-CSharp.dll"
            };

            var result = EngineDetector.DetectFromFiles(files);
            Assert.AreEqual(EngineType.Unity, result.Engine);
            Assert.Greater(result.Confidence, 0.5f);
        }

        [Test]
        public void EngineDetector_DetectFromFiles_Source()
        {
            var files = new[]
            {
                "hl2/hl2_pak_dir.vpk", "hl2/hl2_textures_dir.vpk",
                "hl2/materials/brick/brickwall001a.vtf",
                "hl2/gameinfo.txt",
                "hl2/maps/d1_trainstation_01.bsp"
            };

            var result = EngineDetector.DetectFromFiles(files);
            Assert.AreEqual(EngineType.Source, result.Engine);
            Assert.Greater(result.Confidence, 0.5f);
        }

        [Test]
        public void EngineDetector_DetectFromFiles_UnrealEngine4()
        {
            var files = new[]
            {
                "Game/Content/Paks/game.pak",
                "Game/Content/Textures/T_Default.uasset",
                "Game/Content/Textures/T_Default.uexp"
            };

            var result = EngineDetector.DetectFromFiles(files);
            Assert.AreEqual(EngineType.UnrealEngine4, result.Engine);
            Assert.Greater(result.Confidence, 0.5f);
        }

        [Test]
        public void EngineDetector_DetectFromFiles_Unknown()
        {
            var files = new[] { "readme.txt", "license.md", "data.bin" };
            var result = EngineDetector.DetectFromFiles(files);
            Assert.AreEqual(EngineType.Unknown, result.Engine);
        }

        [Test]
        public void EngineType_HasExpectedValues()
        {
            Assert.AreEqual("Unknown", EngineType.Unknown.ToString());
            Assert.AreEqual("Unity", EngineType.Unity.ToString());
            Assert.AreEqual("Source", EngineType.Source.ToString());
            Assert.AreEqual("idTech3", EngineType.idTech3.ToString());
        }

        #endregion

        #region Integration Tests

        [Test]
        public void AssetScanner_IncludesIdTechAndSourceParsers()
        {
            var database = new AssetDatabase();
            Assert.DoesNotThrow(() => new AssetScanner(database));
        }

        #endregion
    }
}
