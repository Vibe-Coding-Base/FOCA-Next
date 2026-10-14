using FOCA.Utilites;
using Xunit;

namespace FOCA.Tests
{
    public class SecretProtectorTests
    {
        [Fact]
        public void RoundTrip()
        {
            string stored = SecretProtector.Protect("my-shodan-key");
            Assert.StartsWith("dpapi:", stored);
            Assert.DoesNotContain("my-shodan-key", stored);
            Assert.Equal("my-shodan-key", SecretProtector.Unprotect(stored));
        }

        [Fact]
        public void LegacyPlaintextIsReturnedUnchanged()
        {
            Assert.Equal("legacy-key", SecretProtector.Unprotect("legacy-key"));
        }

        [Fact]
        public void ProtectIsIdempotent()
        {
            string stored = SecretProtector.Protect("k");
            Assert.Equal(stored, SecretProtector.Protect(stored));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyValuesPassThrough(string value)
        {
            Assert.Equal(value, SecretProtector.Protect(value));
            Assert.Equal(value, SecretProtector.Unprotect(value));
        }

        [Fact]
        public void CorruptedValueYieldsEmpty()
        {
            Assert.Equal(string.Empty, SecretProtector.Unprotect("dpapi:not-base64!"));
        }
    }
}
