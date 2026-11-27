using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class UpdateCheckerTests : IDisposable
    {
        readonly string _root = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        readonly List<string> _calls = new List<string>();
        readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0);
        string Versions => Path.Combine(_root, "versions");
        const string ManifestUrl = "https://github.com/x/y/releases/latest/download/manifest.json";

        public UpdateCheckerTests()
        {
            Directory.CreateDirectory(Path.Combine(Versions, "1.0.0.0"));
            File.WriteAllText(Path.Combine(Versions, "1.0.0.0", "UnicornPet.exe"), "v1");
            new UpdateStager(Versions).InitState("1.0.0.0");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        static string ManifestJson(Manifest m) =>
            "{\"version\":\"" + m.Version + "\",\"url\":\"" + m.Url + "\",\"sha256\":\"" + m.Sha256 + "\",\"signature\":\"" + m.Signature + "\"}";

        UpdateChecker Make(Func<string, Task<byte[]>> http, Version current = null)
            => new UpdateChecker(http, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), new UpdateStager(Versions), current ?? new Version(1, 0, 0), () => _now);

        (Func<string, Task<byte[]>> http, Manifest m) Server(string version, string zipUrl = "https://github.com/x/y/releases/download/v/app.zip", Func<Manifest, Manifest> tamper = null)
        {
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "v2"));
            var m = UpdateTestKit.Signed(_rsa, version, zipUrl, zip);
            if (tamper != null) m = tamper(m);
            Func<string, Task<byte[]>> http = url =>
            {
                _calls.Add(url);
                if (url == ManifestUrl) return Task.FromResult(Encoding.UTF8.GetBytes(ManifestJson(m)));
                if (url == zipUrl) return Task.FromResult(zip);
                throw new InvalidOperationException("unexpected " + url);
            };
            return (http, m);
        }

        [Fact] public async Task Check_NewerVersion_StagesAndReturnsTrue()
        {
            var (http, _) = Server("1.1.0");
            Assert.True(await Make(http).CheckAsync(ManifestUrl));
            Assert.Equal(2, _calls.Count);
            Assert.True(File.Exists(Path.Combine(Versions, "1.1.0.0", "UnicornPet.exe")));
        }

        [Theory] [InlineData("1.0.0")] [InlineData("0.9.0")]
        public async Task Check_SameOrOlderVersion_NoDownload(string v)
        {
            var (http, _) = Server(v);
            Assert.False(await Make(http).CheckAsync(ManifestUrl));
            Assert.Single(_calls);
            Assert.Equal(ManifestUrl, _calls[0]);
            Assert.DoesNotContain(_calls, c => c.EndsWith(".zip"));
        }

        [Fact] public async Task Check_ServerThrows_ReturnsFalseNoException()
        {
            var c = Make(url => throw new System.Net.Http.HttpRequestException("down"));
            Assert.False(await c.CheckAsync(ManifestUrl));
        }

        [Fact] public async Task Check_ManifestIsGarbage_ReturnsFalse()
        {
            var c = Make(url => Task.FromResult(Encoding.UTF8.GetBytes("<html>captive portal</html>")));
            Assert.False(await c.CheckAsync(ManifestUrl));
        }

        [Fact] public async Task Check_BadSignature_NoDownload()
        {
            var (http, _) = Server("1.1.0", tamper: m => m with { Signature = Convert.ToBase64String(new byte[256]) });
            Assert.False(await Make(http).CheckAsync(ManifestUrl));
            Assert.Single(_calls);
            Assert.False(Directory.Exists(Path.Combine(Versions, "1.1.0.0")));
        }

        [Fact] public async Task Check_SignedWithWrongKey_NoDownload()
        {
            using (var other = UpdateTestKit.NewKey())
            {
                var (http, _) = Server("1.1.0", tamper: m => m with { Signature = UpdateTestKit.Sign(other, m.Version, m.Url, m.Sha256) });
                Assert.False(await Make(http).CheckAsync(ManifestUrl));
                Assert.Single(_calls);
            }
        }

        [Fact] public async Task Check_NonHttpsManifestUrl_NoHttpCalls()
        {
            var (http, _) = Server("1.1.0");
            Assert.False(await Make(http).CheckAsync("http://example.com/manifest.json"));
            Assert.False(await Make(http).CheckAsync(""));
            Assert.Empty(_calls);
        }

#if !DEBUG
        [Fact] public async Task Check_FileUrl_RejectedInReleaseBuild()
        {
            var (http, _) = Server("1.1.0");
            Assert.False(await Make(http).CheckAsync("file:///c:/manifest.json"));
            Assert.Empty(_calls);
        }
#endif

        [Fact] public async Task Check_SignedManifestWithHttpZipUrl_NoDownload()
        {
            var (http, _) = Server("1.1.0", zipUrl: "http://example.com/app.zip");
            Assert.False(await Make(http).CheckAsync(ManifestUrl));
            Assert.Single(_calls);
        }

        [Fact] public async Task Check_ZipHashDiffersFromSignedHash_NotApplied()
        {
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "v2"));
            var m = UpdateTestKit.Signed(_rsa, "1.1.0", "https://x.example/app.zip", zip);
            var evil = UpdateTestKit.Zip(("UnicornPet.exe", "EVIL"));
            Func<string, Task<byte[]>> http = url => Task.FromResult(url == ManifestUrl ? Encoding.UTF8.GetBytes(ManifestJson(m)) : evil);
            Assert.False(await Make(http).CheckAsync(ManifestUrl));
            Assert.False(Directory.Exists(Path.Combine(Versions, "1.1.0.0")));
        }

        [Fact] public async Task Check_NotMoreOftenThanEvery6Hours()
        {
            var (http, _) = Server("1.1.0");
            var s = new Settings { LastUpdateCheck = _now.AddHours(-1) };
            Assert.False(await Make(http).CheckAsync(ManifestUrl, s));
            Assert.Empty(_calls);
            s.LastUpdateCheck = _now.AddHours(-7);
            Assert.True(await Make(http).CheckAsync(ManifestUrl, s));
            Assert.Equal(_now, s.LastUpdateCheck);
        }

        [Fact] public async Task Check_Force_BypassesInterval()
        {
            var (http, _) = Server("1.1.0");
            var s = new Settings { LastUpdateCheck = _now.AddMinutes(-1) };
            Assert.True(await Make(http).CheckAsync(ManifestUrl, s, force: true));
        }

        [Fact] public async Task Check_FailureStillRecordsAttempt_NoRetryStorm()
        {
            var s = new Settings();
            var c = Make(url => throw new Exception("down"));
            Assert.False(await c.CheckAsync(ManifestUrl, s));
            Assert.Equal(_now, s.LastUpdateCheck);
            Assert.False(await c.CheckAsync(ManifestUrl, s));
            Assert.Equal(_now, s.LastUpdateCheck);
        }

        [Fact] public async Task Check_AlreadyStagedVersion_ReturnsFalse()
        {
            var (http, _) = Server("1.1.0");
            Assert.True(await Make(http).CheckAsync(ManifestUrl));
            _calls.Clear();
            Assert.False(await Make(http).CheckAsync(ManifestUrl));
            Assert.Single(_calls);
        }

        [Fact] public async Task Check_EmptyPublicKey_NeverApplies()
        {
            var (http, _) = Server("1.1.0");
            var c = new UpdateChecker(http, new ManifestVerifier(""), new UpdateStager(Versions), new Version(1, 0, 0), () => _now);
            Assert.False(await c.CheckAsync(ManifestUrl));
            Assert.False(Directory.Exists(Path.Combine(Versions, "1.1.0.0")));
        }
    }
}



