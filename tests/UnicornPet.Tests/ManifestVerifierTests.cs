using System;
using System.Security.Cryptography;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class ManifestVerifierTests
    {
        static (RSA rsa, ManifestVerifier v, Manifest m) Setup()
        {
            var rsa = UpdateTestKit.NewKey();
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "x"));
            var m = UpdateTestKit.Signed(rsa, "1.2.3", "https://example.com/u/1.2.3.zip", zip);
            return (rsa, new ManifestVerifier(UpdateTestKit.PublicXml(rsa)), m);
        }

        [Fact] public void Verify_ValidSignature_True() { var (_, v, m) = Setup(); Assert.True(v.Verify(m)); }

        [Fact] public void Verify_TamperedUrl_False() { var (_, v, m) = Setup(); Assert.False(v.Verify(m with { Url = "https://evil.example/u.zip" })); }

        [Fact] public void Verify_TamperedHash_False() { var (_, v, m) = Setup(); Assert.False(v.Verify(m with { Sha256 = new string('a', 64) })); }

        [Fact] public void Verify_TamperedVersion_False() { var (_, v, m) = Setup(); Assert.False(v.Verify(m with { Version = new Version(9, 9, 9) })); }

        [Fact] public void Verify_SignedWithOtherKey_False()
        {
            var (_, v, m) = Setup();
            using (var other = UpdateTestKit.NewKey())
                Assert.False(v.Verify(m with { Signature = UpdateTestKit.Sign(other, m.Version, m.Url, m.Sha256) }));
        }

        [Fact] public void Verify_GarbageSignature_FalseNoThrow()
        {
            var (_, v, m) = Setup();
            Assert.False(v.Verify(m with { Signature = "not base64 !!!" }));
            Assert.False(v.Verify(m with { Signature = "" }));
            Assert.False(v.Verify(null));
        }

        [Fact] public void Verify_BadOrEmptyPublicKey_AlwaysFalse()
        {
            var (_, _, m) = Setup();
            Assert.False(new ManifestVerifier("").Verify(m));
            Assert.False(new ManifestVerifier("<junk/>").Verify(m));
            Assert.False(new ManifestVerifier(null).Verify(m));
        }

        [Fact] public void Parse_ValidJson_RoundTrips()
        {
            var m = Manifest.Parse("{\"version\":\"1.2.3\",\"url\":\"https://a/b.zip\",\"sha256\":\"ab\",\"signature\":\"c2ln\"}");
            Assert.NotNull(m);
            Assert.Equal(new Version(1, 2, 3), m.Version);
            Assert.Equal("https://a/b.zip", m.Url);
        }

        [Fact] public void Parse_WithUtf8Bom_StillParses()
        {
            var m = Manifest.Parse("\uFEFF{\"version\":\"1.2.3\",\"url\":\"https://a/b.zip\",\"sha256\":\"ab\",\"signature\":\"c2ln\"}");
            Assert.NotNull(m);
        }

        [Theory]
        [InlineData("")] [InlineData("nope")] [InlineData("{}")] [InlineData("{\"version\":\"x\",\"url\":\"u\",\"sha256\":\"s\",\"signature\":\"g\"}")]
        [InlineData("[1,2]")]
        public void Parse_Invalid_ReturnsNull(string json) => Assert.Null(Manifest.Parse(json));
    }
}

