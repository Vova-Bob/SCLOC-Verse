using SCLOCVerse.Services.OcrPlatform.Validation;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class ConsensusBufferTests
    {
        [Fact]
        public void EmptyBuffer_ReturnsNull()
        {
            var buffer = new ConsensusBuffer(5);
            Assert.Null(buffer.GetConsensus());
        }

        [Fact]
        public void SingleEntry_NoConsensus_Under3()
        {
            var buffer = new ConsensusBuffer(5);
            buffer.Add("21425");

            // Менше 3 однакових — strict majority не досягнуто.
            Assert.Null(buffer.GetConsensus());
        }

        [Fact]
        public void TwoSameEntries_NoConsensus_Under3()
        {
            var buffer = new ConsensusBuffer(5);
            buffer.Add("21425");
            buffer.Add("21425");

            // Тільки 2 — strict majority для capacity 5 потребує 3.
            Assert.Null(buffer.GetConsensus());
        }

        [Fact]
        public void ThreeSameEntries_ReturnsConsensus()
        {
            var buffer = new ConsensusBuffer(5);
            buffer.Add("21425");
            buffer.Add("21425");
            buffer.Add("21425");

            Assert.Equal("21425", buffer.GetConsensus());
        }

        [Fact]
        public void MixedEntries_MajorityWins()
        {
            var buffer = new ConsensusBuffer(5);
            buffer.Add("21425");
            buffer.Add("21425");
            buffer.Add("21425");
            buffer.Add("21426"); // промах
            buffer.Add("");      // промах

            Assert.Equal("21425", buffer.GetConsensus());
        }

        [Fact]
        public void Rolling_OldEntriesEvicted()
        {
            var buffer = new ConsensusBuffer(3);
            buffer.Add("OLD1");
            buffer.Add("OLD1");
            buffer.Add("NEW");
            buffer.Add("NEW");
            buffer.Add("NEW");

            // OLD1 відкидається, NEW — majority.
            Assert.Equal("NEW", buffer.GetConsensus());
        }

        [Fact]
        public void Clear_ResetsBuffer()
        {
            var buffer = new ConsensusBuffer(5);
            buffer.Add("21425");
            buffer.Add("21425");
            buffer.Add("21425");
            buffer.Clear();

            Assert.Equal(0, buffer.Count);
            Assert.Null(buffer.GetConsensus());
        }
    }
}