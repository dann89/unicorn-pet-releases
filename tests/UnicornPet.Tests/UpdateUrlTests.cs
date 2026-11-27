using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class UpdateUrlTests : IDisposable
    {
        readonly string _saved = UpdateConfig.DefaultManifestUrl;
        public void Dispose() { UpdateConfig.DefaultManifestUrl = _saved; }

        const string Good = "https://github.com/x/y/releases/latest/download/manifest.json";

        [Theory]
        [InlineData(Good)]
        [InlineData("https://example.com/m.json")]
        [InlineData("HTTPS://EXAMPLE.COM/m.json")]
        public void Valid_Https_Accepted(string url) => Assert.True(UrlRules.IsAllowed(url));

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("not a url")] [InlineData("manifest.json")] [InlineData("/relative/path")]
        [InlineData("http://example.com/m.json")] [InlineData("ftp://example.com/m.json")] [InlineData("javascript:alert(1)")]
        [InlineData("https://")] [InlineData("https:///m.json")] [InlineData("https://user:pw@example.com/m.json")] [InlineData("https://example.com/\u0000")]
        [InlineData("\\\\server\\share\\m.json")] [InlineData("//example.com/m.json")] [InlineData("data:text/plain,hi")]
        public void Invalid_Rejected(string url) => Assert.False(UrlRules.IsAllowed(url));

        [Fact] public void TooLongUrl_Rejected() => Assert.False(UrlRules.IsAllowed("https://example.com/" + new string('a', 3000)));

#if !DEBUG
        [Fact] public void FileUrl_RejectedInRelease() => Assert.False(UrlRules.IsAllowed("file:///c:/m.json"));
#endif

        [Fact] public void Choose_UsesSettingsUrl_WhenValid()
        {
            UpdateConfig.DefaultManifestUrl = Good;
            Assert.Equal("https://other.example/m.json", UpdateConfig.ChooseManifestUrl("https://other.example/m.json"));
        }

        [Fact] public void Choose_EmptySettings_UsesConfigDefault()
        {
            UpdateConfig.DefaultManifestUrl = Good;
            Assert.Equal(Good, UpdateConfig.ChooseManifestUrl(""));
            Assert.Equal(Good, UpdateConfig.ChooseManifestUrl(null));
            Assert.Equal(Good, UpdateConfig.ChooseManifestUrl("   "));
        }

        [Theory]
        [InlineData("")] [InlineData("http://example.com/m.json")] [InlineData("garbage")] [InlineData("   ")]
        public void Choose_BadOrEmptyConfig_MeansNoUpdates(string configValue)
        {
            UpdateConfig.DefaultManifestUrl = configValue;
            Assert.Null(UpdateConfig.ChooseManifestUrl(""));
            Assert.Null(UpdateConfig.ChooseManifestUrl(null));
        }

        [Theory]
        [InlineData("http://example.com/m.json")] [InlineData("garbage")] [InlineData("ftp://x/y")]
        public void Choose_BadSettingsUrl_MeansNoUpdates_NotAFallback(string settingsValue)
        {
            UpdateConfig.DefaultManifestUrl = Good;
            Assert.Null(UpdateConfig.ChooseManifestUrl(settingsValue));
        }

        [Fact] public void Choose_ReadsTheCurrentConfigValueEachCall_SoStartupOrderCannotBeStale()
        {
            UpdateConfig.DefaultManifestUrl = "";
            Assert.Null(UpdateConfig.ChooseManifestUrl(""));
            UpdateConfig.DefaultManifestUrl = Good;
            Assert.Equal(Good, UpdateConfig.ChooseManifestUrl(""));
        }

        [Fact] public void Choose_TrimsSurroundingWhitespace()
        {
            UpdateConfig.DefaultManifestUrl = "  " + Good + "  ";
            Assert.Equal(Good, UpdateConfig.ChooseManifestUrl(""));
        }

        [Theory]
        [InlineData("")] [InlineData("http://example.com/m.json")] [InlineData("garbage")] [InlineData("https://")]
        public async Task Checker_NeverCallsHttp_ForEmptyOrInvalidUrls(string url)
        {
            var calls = new List<string>();
            var root = UpdateTestKit.TempDir();
            try
            {
                var checker = new UpdateChecker(u => { calls.Add(u); return Task.FromResult(new byte[0]); },
                    new ManifestVerifier(""), new UpdateStager(System.IO.Path.Combine(root, "versions")), new Version(1, 0, 0, 0));
                Assert.False(await checker.CheckAsync(url));
                Assert.Empty(calls);
            }
            finally { try { System.IO.Directory.Delete(root, true); } catch { } }
        }
    }
}
