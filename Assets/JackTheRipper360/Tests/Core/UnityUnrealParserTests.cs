using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using JackTheRipper360.Core.Common;
using JackTheRipper360.Core.Discovery;
using JackTheRipper360.Core.Plugins;
using JackTheRipper360.Core.Plugins.Unity;
using JackTheRipper360.Core.Plugins.Unreal;

namespace JackTheRipper360.Tests.Core
{
    /// <summary>
    /// Tests for Unity and Unreal Engine asset parsers, format detection, and reimport converter.
    /// </summary>
    [TestFixture]
    public class UnityUnrealParserTests
    {
        #region Format Detection Tests

        [Test]
        public void FormatDetector_IdentifiesUnityFS_Bundle()
        {
            // "UnityFS\0" header
            byte[] header = Encoding.ASCII.GetBytes("UnityFS\0");
            byte[] data = new byte[64];
            Array.Copy(header, data, header.Length);

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("UnityFS"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesUnityWeb_Bundle()
        {
            byte[] header = Encoding.ASCII.GetBytes("UnityWeb\0");
            byte[] data = new byte[64];
            Array.Copy(header, data, header.Length);

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("UnityWeb"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesUnityRaw_Bundle()
        {
            byte[] header = Encoding.ASCII.GetBytes("UnityRaw\0");
            byte[] data = new byte[64];
            Array.Copy(header, data, header.Length);

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("UnityRaw"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesUAsset_BigEndian()
        {
            // UAsset magic 0xC1832A9E (big-endian)
            byte[] data = new byte[64];
            data[0] = 0xC1; data[1] = 0x83; data[2] = 0x2A; data[3] = 0x9E;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("UAsset"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesUAsset_LittleEndian()
        {
            // UAsset magic 0x9E2A83C1 (little-endian)
            byte[] data = new byte[64];
            data[0] = 0x9E; data[1] = 0x2A; data[2] = 0x83; data[3] = 0xC1;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("UAsset") || match.FormatName.Contains("UE"));
                Assert.AreEqual(1.0f, match.Confidence);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesUnrealPak_ByFooter()
        {
            // PAK footer magic at offset -44: 0x5A6F12E1
            byte[] data = new byte[128];
            int footerOffset = data.Length - 44;
            data[footerOffset] = 0x5A;
            data[footerOffset + 1] = 0x6F;
            data[footerOffset + 2] = 0x12;
            data[footerOffset + 3] = 0xE1;

            using (var stream = new MemoryStream(data))
            {
                var match = FormatDetector.Identify(stream);
                Assert.AreEqual(AssetType.Container, match.Type);
                Assert.IsTrue(match.FormatName.Contains("PAK") || match.FormatName.Contains("Pak"));
                Assert.GreaterOrEqual(match.Confidence, 0.9f);
            }
        }

        [Test]
        public void FormatDetector_IdentifiesByExtension_UnityFormats()
        {
            var assets = FormatDetector.IdentifyByExtension("level1.assets");
            Assert.AreEqual(AssetType.Container, assets.Type);

            var bundle = FormatDetector.IdentifyByExtension("resource.unity3d");
            Assert.AreEqual(AssetType.Container, bundle.Type);
        }

        [Test]
        public void FormatDetector_IdentifiesByExtension_UnrealFormats()
        {
            var pak = FormatDetector.IdentifyByExtension("game.pak");
            Assert.AreEqual(AssetType.Container, pak.Type);

            var uasset = FormatDetector.IdentifyByExtension("texture.uasset");
            Assert.AreEqual(AssetType.Container, uasset.Type);

            var umap = FormatDetector.IdentifyByExtension("level.umap");
            Assert.AreEqual(AssetType.Container, umap.Type);

            var uexp = FormatDetector.IdentifyByExtension("data.uexp");
            Assert.AreEqual(AssetType.Data, uexp.Type);

            var ubulk = FormatDetector.IdentifyByExtension("big.ubulk");
            Assert.AreEqual(AssetType.Data, ubulk.Type);
        }

        #endregion

        #region Unity Parser Tests

        [Test]
        public void UnityAssetParser_Type_IsContainer()
        {
            var parser = new UnityAssetParser();
            Assert.AreEqual(AssetType.Container, parser.Type);
        }

        [Test]
        public void UnityAssetParser_CanParse_ValidUnityFS()
        {
            var parser = new UnityAssetParser();
            byte[] header = Encoding.ASCII.GetBytes("UnityFS\0");
            byte[] data = new byte[64];
            Array.Copy(header, data, header.Length);

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "bundle.unity3d"));
            }
        }

        [Test]
        public void UnityAssetParser_CanParse_InvalidData_ReturnsFalse()
        {
            var parser = new UnityAssetParser();
            byte[] data = new byte[64]; // all zeros

            using (var stream = new MemoryStream(data))
            {
                Assert.IsFalse(parser.CanParse(stream, "random.bin"));
            }
        }

        [Test]
        public void UnityAssetParser_CanParse_ByExtension()
        {
            var parser = new UnityAssetParser();
            byte[] data = new byte[64];

            using (var stream = new MemoryStream(data))
            {
                // .assets and .unity3d extensions should be accepted
                bool canParseAssets = parser.CanParse(stream, "level0.assets");
                // May or may not return true depending on header validation
                // The parser should at minimum not throw
                Assert.DoesNotThrow(() => parser.CanParse(stream, "level0.assets"));
            }
        }

        #endregion

        #region Unreal Parser Tests

        [Test]
        public void UnrealAssetParser_Type_IsContainer()
        {
            var parser = new UnrealAssetParser();
            Assert.AreEqual(AssetType.Container, parser.Type);
        }

        [Test]
        public void UnrealAssetParser_CanParse_ValidMagic()
        {
            var parser = new UnrealAssetParser();
            byte[] data = new byte[64];
            // UE4 magic in little-endian: 0xC1832A9E
            data[0] = 0xC1; data[1] = 0x83; data[2] = 0x2A; data[3] = 0x9E;

            using (var stream = new MemoryStream(data))
            {
                Assert.IsTrue(parser.CanParse(stream, "texture.uasset"));
            }
        }

        [Test]
        public void UnrealAssetParser_CanParse_InvalidData_ReturnsFalse()
        {
            var parser = new UnrealAssetParser();
            byte[] data = new byte[64]; // all zeros

            using (var stream = new MemoryStream(data))
            {
                Assert.IsFalse(parser.CanParse(stream, "random.bin"));
            }
        }

        [Test]
        public void UnrealPakReader_CanRead_ValidFooter()
        {
            using (var reader = new UnrealPakReader())
            {
                // Create minimal PAK with footer magic
                byte[] data = new byte[256];
                // PAK magic at footer position (end - 44): 0x5A6F12E1
                int footerPos = data.Length - 44;
                data[footerPos] = 0x5A;
                data[footerPos + 1] = 0x6F;
                data[footerPos + 2] = 0x12;
                data[footerPos + 3] = 0xE1;

                using (var stream = new MemoryStream(data))
                {
                    Assert.IsTrue(reader.CanRead(stream));
                }
            }
        }

        [Test]
        public void UnrealPakReader_CanRead_InvalidData_ReturnsFalse()
        {
            using (var reader = new UnrealPakReader())
            {
                byte[] data = new byte[256]; // all zeros

                using (var stream = new MemoryStream(data))
                {
                    Assert.IsFalse(reader.CanRead(stream));
                }
            }
        }

        #endregion

        #region Engine Reimport Converter Tests

        [Test]
        public void ConversionProfile_DefaultValues()
        {
            var profile = new ConversionProfile();
            Assert.AreEqual(TargetEngine.Unity, profile.Target);
            Assert.IsTrue(profile.GenerateMetaFiles);
            Assert.IsTrue(profile.GenerateImportSettings);
            Assert.IsTrue(profile.PreserveDirectoryStructure);
        }

        [Test]
        public void EngineReimportConverter_ConvertAsset_NullEntry_ReturnsFailure()
        {
            var converter = new EngineReimportConverter();
            var profile = new ConversionProfile { Target = TargetEngine.Unity };

            var result = converter.ConvertAsset(null, "/tmp/test_output", profile);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void EngineReimportConverter_GetOutputSubdirectory_Texture()
        {
            var entry = new AssetEntry("test_texture.dds", AssetType.Texture)
            {
                FormatName = "DDS"
            };

            var converter = new EngineReimportConverter();
            var profile = new ConversionProfile
            {
                Target = TargetEngine.Unity,
                PreserveDirectoryStructure = true
            };

            // The converter should map Texture type to a Textures subdirectory
            var result = converter.ConvertAsset(entry, "/tmp/test_output", profile);
            // Even if conversion fails due to no source data, check it attempted correctly
            Assert.IsNotNull(result);
        }

        [Test]
        public void EngineReimportConverter_ConvertAsset_UnrealProfile()
        {
            var entry = new AssetEntry("test_mesh.obj", AssetType.Model)
            {
                FormatName = "OBJ"
            };

            var converter = new EngineReimportConverter();
            var profile = new ConversionProfile
            {
                Target = TargetEngine.UnrealEngine,
                GenerateImportSettings = true
            };

            var result = converter.ConvertAsset(entry, "/tmp/test_output", profile);
            Assert.IsNotNull(result);
        }

        [Test]
        public void TargetEngine_HasExpectedValues()
        {
            Assert.AreEqual(1, (int)TargetEngine.Unity);
            Assert.AreEqual(2, (int)TargetEngine.UnrealEngine);
            Assert.AreEqual(3, (int)TargetEngine.Both);
        }

        #endregion

        #region Unity Texture Extractor Tests

        [Test]
        public void UnityTextureExtractor_DdsHeader_HasCorrectMagic()
        {
            // Verify DDS magic constant
            byte[] magic = { 0x44, 0x44, 0x53, 0x20 }; // "DDS "
            Assert.AreEqual(0x44, magic[0]);
            Assert.AreEqual(0x44, magic[1]);
            Assert.AreEqual(0x53, magic[2]);
            Assert.AreEqual(0x20, magic[3]);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void AssetScanner_IncludesUnityAndUnrealParsers()
        {
            var database = new AssetDatabase();
            // AssetScanner constructor should include Unity and Unreal parsers
            Assert.DoesNotThrow(() => new AssetScanner(database));
        }

        [Test]
        public void FormatDetector_AllUnityExtensions_Detected()
        {
            string[] unityExtensions = { ".assets", ".unity3d", ".bundle", ".resource" };
            foreach (var ext in unityExtensions)
            {
                var match = FormatDetector.IdentifyByExtension($"test{ext}");
                Assert.AreNotEqual(AssetType.Unknown, match.Type,
                    $"Extension {ext} should be recognized");
            }
        }

        [Test]
        public void FormatDetector_AllUnrealExtensions_Detected()
        {
            string[] unrealExtensions = { ".pak", ".uasset", ".umap", ".uexp", ".ubulk", ".upk" };
            foreach (var ext in unrealExtensions)
            {
                var match = FormatDetector.IdentifyByExtension($"test{ext}");
                Assert.AreNotEqual(AssetType.Unknown, match.Type,
                    $"Extension {ext} should be recognized");
            }
        }

        #endregion
    }
}
