using OpenCvSharp;
using SCLOCVerse.Services.Mining.Signatures;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для MiningSignatureDatabase: lookup + defaults + override.
    /// </summary>
    public class MiningSignatureDatabaseTests
    {
        [Fact]
        public void Lookup_KnownCode_ReturnsMaterial()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var material = db.Lookup("21425");

            Assert.NotNull(material);
            Assert.Equal("Aluminium", material!.Name);
            Assert.Equal("Metal", material.Category);
        }

        [Fact]
        public void Lookup_UnknownCode_ReturnsNull()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var material = db.Lookup("99999");
            Assert.Null(material);
        }

        [Fact]
        public void Lookup_EmptyCode_ReturnsNull()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            Assert.Null(db.Lookup(""));
            Assert.Null(db.Lookup(null!));
        }

        [Fact]
        public void Count_ReturnsDefaultCount()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            Assert.Equal(DefaultMiningSignatures.Defaults.Count, db.Count);
            Assert.True(db.Count >= 10); // щонайменше 11 defaults
        }

        [Fact]
        public void Reload_RestoresDefaults()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var originalCount = db.Count;
            db.Reload();
            Assert.Equal(originalCount, db.Count);
        }

        [Fact]
        public void Lookup_AllDefaults_Found()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            foreach (var (code, expected) in DefaultMiningSignatures.Defaults)
            {
                var material = db.Lookup(code);
                Assert.NotNull(material);
                Assert.Equal(expected.Name, material!.Name);
            }
        }
    }
}