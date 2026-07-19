using SCLOCVerse.Services.OcrPlatform.Validation;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class FieldLockTests
    {
        [Fact]
        public void EmptyLock_ReturnsFalse()
        {
            var lock_ = new FieldLock();
            Assert.False(lock_.TryGetLocked(12345, out var value, out var conf));
            Assert.Null(value);
            Assert.Equal(0, conf);
        }

        [Fact]
        public void SameFingerprint_ReturnsLocked()
        {
            var lock_ = new FieldLock();
            lock_.Update(10000, "21425", 0.95);

            Assert.True(lock_.TryGetLocked(10000, out var value, out var conf));
            Assert.Equal("21425", value);
            Assert.Equal(0.95, conf, 2);
        }

        [Fact]
        public void DifferentFingerprint_DoesNotReturn()
        {
            var lock_ = new FieldLock();
            lock_.Update(10000, "21425", 0.95);

            // 1% дельти — дозволено.
            Assert.True(lock_.TryGetLocked(10099, out _, out _));
            // 2% дельти — занадто різко.
            Assert.False(lock_.TryGetLocked(10201, out var value, out var conf));
            Assert.Null(value);
        }

        [Fact]
        public void NullFingerprint_ReturnsFalse()
        {
            var lock_ = new FieldLock();
            lock_.Update(10000, "21425", 0.95);

            Assert.False(lock_.TryGetLocked(null, out var value, out var conf));
            Assert.Null(value);
        }

        [Fact]
        public void Clear_ResetsLock()
        {
            var lock_ = new FieldLock();
            lock_.Update(10000, "21425", 0.95);
            lock_.Clear();

            Assert.False(lock_.IsLocked);
            Assert.False(lock_.TryGetLocked(10000, out _, out _));
        }

        [Fact]
        public void Update_ReplacesOldValue()
        {
            var lock_ = new FieldLock();
            lock_.Update(10000, "OLD", 0.9);
            lock_.Update(10000, "NEW", 0.95);

            Assert.True(lock_.TryGetLocked(10000, out var value, out var conf));
            Assert.Equal("NEW", value);
            Assert.Equal(0.95, conf, 2);
        }
    }
}