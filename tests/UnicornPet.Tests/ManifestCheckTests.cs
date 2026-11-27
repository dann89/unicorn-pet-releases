using System;
using System.Security.Cryptography;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class ManifestCheckTests
    {
        static string Json(Manifest m) =>
            "{\"version\":\"" + m.Version + "\",\"url\":\"" + m.Url + "\",\"sha256\":\"" + m.Sha256 + "\",\"signature\":\"" + m.Signature + "\"}";

        static (RSA rsa, ManifestVerifier v, Manifest m) Setup(string url = "https://example.com/UnicornPet-1.2.3.zip")
        {
            var rsa = UpdateTestKit.NewKey();
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "x"));
            return (rsa, new ManifestVerifier(UpdateTestKit.PublicXml(rsa)), UpdateTestKit.Signed(rsa, "1.2.3", url, zip));
        }

        [Fact] public void Valid_ReturnsNull()
        {
            var (_, v, m) = Setup();
            Assert.Null(ManifestCheck.Evaluate(Json(m), v));
        }

        [Fact] public void Valid_WithBom_ReturnsNull()
        {
            var (_, v, m) = Setup();
            Assert.Null(ManifestCheck.Evaluate("﻿" + Json(m), v));
        }

        [Fact] public void TamperedHash_Fails()
        {
            var (_, v, m) = Setup();
            Assert.NotNull(ManifestCheck.Evaluate(Json(m with { Sha256 = new string('a', 64) }), v));
        }

        [Fact] public void WrongKey_Fails()
        {
            var (_, _, m) = Setup();
            using (var other = UpdateTestKit.NewKey())
                Assert.NotNull(ManifestCheck.Evaluate(Json(m), new ManifestVerifier(UpdateTestKit.PublicXml(other))));
        }

        [Fact] public void NonHttpsUrl_Fails_EvenWithAValidSignature()
        {
            var (_, v, m) = Setup("http://example.com/UnicornPet-1.2.3.zip");
            var msg = ManifestCheck.Evaluate(Json(m), v);
            Assert.NotNull(msg);
            Assert.Contains("https", msg);
        }

        [Theory] [InlineData("")] [InlineData("not json")] [InlineData("{}")] [InlineData("[]")] [InlineData(null)]
        public void Garbage_Fails(string json)
        {
            var (_, v, _) = Setup();
            Assert.NotNull(ManifestCheck.Evaluate(json, v));
        }

        [Fact] public void EmptyPublicKey_Fails()
        {
            var (_, _, m) = Setup();
            Assert.NotNull(ManifestCheck.Evaluate(Json(m), new ManifestVerifier("")));
        }

        [Fact] public void VersionWithFourParts_IsAcceptedByTheChecker_ButPublishRestrictsToThree()
        {
            var rsa = UpdateTestKit.NewKey();
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "x"));
            var m = UpdateTestKit.Signed(rsa, "1.2.3.4", "https://example.com/a.zip", zip);
            Assert.Null(ManifestCheck.Evaluate(Json(m), new ManifestVerifier(UpdateTestKit.PublicXml(rsa))));
        }
    }
}
