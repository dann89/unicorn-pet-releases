using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>2.1 settings keys (message source, sections, idle poop, zoomies) and the custom source path and address rules.</summary>
    public class V21SettingsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "up-v21-" + Guid.NewGuid().ToString("N"));
        string SettingsPath => Path.Combine(_dir, "settings.json");
        public V21SettingsTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void NewSettings_Defaults()
        {
            var s = new Settings();
            Assert.Equal(MessageSourceKind.BuiltIn, s.MessageSource);
            Assert.Equal("", s.MessageFile); Assert.Equal("", s.MessageUrl); Assert.Equal("", s.MessageUrlCached);
            Assert.Null(s.LastCustomFetch); Assert.Empty(s.SectionsOff);
            Assert.False(s.IdlePoop); Assert.False(s.RandomZoomies);
        }

        [Fact]
        public void Save_WritesTheNewKeys_BetweenRemoteVersionAndPetSize()
        {
            new SettingsStore(SettingsPath).Save(new Settings());
            var text = File.ReadAllText(SettingsPath).Replace("\r\n", "\n");
            Assert.Contains("\"RemoteMessagesVersion\": \"\",\n  \"MessageSource\": \"BuiltIn\",\n  \"MessageFile\": \"\",\n  \"MessageUrl\": \"\",\n  \"MessageUrlCached\": \"\",\n  \"LastCustomFetch\": null,\n  \"SectionsOff\": [],\n  \"IdlePoop\": false,\n  \"RandomZoomies\": false,\n  \"PetSize\": \"Large\"", text);
            Assert.EndsWith("\"GameHighScore\": 0\n}\n", text);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var when = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
            var store = new SettingsStore(SettingsPath);
            Assert.True(store.Save(new Settings
            {
                MessageSource = MessageSourceKind.File,
                MessageFile = @"C:\lists\mine.json",
                MessageUrl = "https://example.invalid/pets/list.json",
                MessageUrlCached = "https://example.invalid/pets/list.json",
                LastCustomFetch = when,
                SectionsOff = new[] { "banter", "pep" },
                IdlePoop = true,
                RandomZoomies = true,
            }));
            var s = store.Load();
            Assert.Equal(MessageSourceKind.File, s.MessageSource);
            Assert.Equal(@"C:\lists\mine.json", s.MessageFile);
            Assert.Equal("https://example.invalid/pets/list.json", s.MessageUrl);
            Assert.Equal("https://example.invalid/pets/list.json", s.MessageUrlCached);
            Assert.Equal(when, s.LastCustomFetch);
            Assert.Equal(DateTimeKind.Utc, s.LastCustomFetch!.Value.Kind);
            Assert.Equal(new[] { "banter", "pep" }, s.SectionsOff);
            Assert.True(s.IdlePoop);
            Assert.True(s.RandomZoomies);
        }

        // the 2.0.0 game build's first-run file (as in PetOptionsTests)
        const string GameBuildFile = "{\n  \"CheekyMode\": false,\n  \"ScheduledEnabled\": true,\n  \"QuietUntil\": null,\n  \"CustomName\": \"Alex\",\n  \"UseWindowsName\": true,\n  \"FetchMessages\": false,\n  \"LastMessageCheck\": null,\n  \"RemoteMessagesVersion\": \"\",\n  \"GameHighScore\": 420\n}\n";

        [Fact]
        public void Load_A_2_0_0_File_GivesDefaultsForTheNewKeys_AndKeepsTheRest()
        {
            File.WriteAllText(SettingsPath, GameBuildFile);
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal("Alex", s.CustomName);
            Assert.Equal(420, s.GameHighScore);
            Assert.False(s.CheekyMode);
            Assert.Equal(MessageSourceKind.BuiltIn, s.MessageSource);
            Assert.Equal("", s.MessageFile); Assert.Equal("", s.MessageUrl); Assert.Equal("", s.MessageUrlCached);
            Assert.Null(s.LastCustomFetch); Assert.Empty(s.SectionsOff);
            Assert.False(s.IdlePoop); Assert.False(s.RandomZoomies);
        }

        [Fact]
        public void Load_BadValues_GiveDefaults_AndOtherKeysSurvive()
        {
            File.WriteAllText(SettingsPath, "{ \"CheekyMode\": false, \"MessageSource\": \"Ftp\", \"MessageFile\": \"\\\\\\\\.\\\\pipe\\\\x.json\", " +
                "\"MessageUrl\": \"http://example.invalid/a.json\", \"MessageUrlCached\": 7, \"LastCustomFetch\": \"soon\", " +
                "\"SectionsOff\": [1, \"ok\", \"bad id!\", \"ok\"], \"IdlePoop\": \"yes\", \"RandomZoomies\": 1, \"GameHighScore\": 3 }");
            var s = new SettingsStore(SettingsPath).Load();
            Assert.False(s.CheekyMode); Assert.Equal(3, s.GameHighScore);
            Assert.Equal(MessageSourceKind.BuiltIn, s.MessageSource);
            Assert.Equal("", s.MessageFile); Assert.Equal("", s.MessageUrl); Assert.Equal("", s.MessageUrlCached);
            Assert.Null(s.LastCustomFetch); Assert.Equal(new[] { "ok" }, s.SectionsOff);
            Assert.False(s.IdlePoop); Assert.False(s.RandomZoomies);
        }

        [Fact]
        public void Load_SectionsOff_KeepsAtMost32()
        {
            var ids = string.Join(", ", Enumerable.Range(0, 40).Select(i => "\"s" + i + "\""));
            File.WriteAllText(SettingsPath, "{ \"SectionsOff\": [" + ids + "] }");
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal(32, s.SectionsOff.Count);
            Assert.Equal("s0", s.SectionsOff[0]);
            Assert.Equal("s31", s.SectionsOff[31]);
        }

        [Fact]
        public void Load_SectionsOff_NotAList_IsEmpty()
        {
            File.WriteAllText(SettingsPath, "{ \"SectionsOff\": \"banter\" }");
            Assert.Empty(new SettingsStore(SettingsPath).Load().SectionsOff);
        }

        [Theory]
        [InlineData("https://example.invalid/pets/list.json", true)]
        [InlineData(" https://example.invalid/a ", true)]
        [InlineData("http://example.invalid/a.json", false)]
        [InlineData("file:///C:/a.json", false)]
        [InlineData("https://user@example.invalid/a", false)]
        [InlineData("example.invalid/a.json", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsAllowedUrl_Cases(string? url, bool expected) => Assert.Equal(expected, CustomSourceRules.IsAllowedUrl(url!));

        [Theory]
        [InlineData(@"C:\lists\mine.json", true)]
        [InlineData(@"\\server\share\team\lines.JSON", true)]
        [InlineData(@"lists\mine.json", false)]
        [InlineData(@"C:mine.json", false)]
        [InlineData(@"C:\lists\mine.txt", false)]
        [InlineData(@"\\?\C:\lists\mine.json", false)]
        [InlineData(@"\\.\pipe\x.json", false)]
        [InlineData(@"C:\lists\mine.json:stream.json", false)]
        [InlineData("C:/lists/mine.json", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData(@"\\server\x.json", false)]
        [InlineData(@"C:\lists\a|b.json", false)]
        [InlineData(@"\\localhost\pipe\x.json", false)]
        [InlineData(@"\\server\PIPE\x.json", false)]
        [InlineData(@"C:\x\CON.json", false)]
        [InlineData(@"C:\x\nul.json", false)]
        [InlineData(@"C:\x\COM1.json", false)]
        [InlineData(@"C:\x\CONIN$.json", false)]
        [InlineData(@"C:\lpt9\a.json", false)]
        [InlineData(@"C:\x\console.json", true)]
        [InlineData(@"C:\x\COM10.json", true)]
        [InlineData(@"\\server\pipes\x.json", true)]
        public void IsAllowedFile_Cases(string? path, bool expected) => Assert.Equal(expected, CustomSourceRules.IsAllowedFile(path!));

        [Fact]
        public void IsAllowedFile_TooLong_OrWithAControlCharacter_IsFalse()
        {
            var longPath = @"C:\" + new string('a', 261 - 3 - 5) + ".json";
            Assert.Equal(261, longPath.Length);
            Assert.False(CustomSourceRules.IsAllowedFile(longPath));
            var justRight = @"C:\" + new string('a', 260 - 3 - 5) + ".json";
            Assert.Equal(260, justRight.Length);
            Assert.True(CustomSourceRules.IsAllowedFile(justRight));
            Assert.False(CustomSourceRules.IsAllowedFile("C:\\lists\\mi\tne.json"));
        }

        [Theory]
        [InlineData("  \"C:\\lists\\mine.json\"  ", @"C:\lists\mine.json")]
        [InlineData(@"C:\a.json", @"C:\a.json")]
        [InlineData(null, "")]
        [InlineData("\"", "\"")]
        public void CleanPath_Cases(string? typed, string expected) => Assert.Equal(expected, CustomSourceRules.CleanPath(typed!));

        [Theory]
        [InlineData("file", MessageSourceKind.File)]
        [InlineData("WEB", MessageSourceKind.Web)]
        [InlineData("BuiltIn", MessageSourceKind.BuiltIn)]
        [InlineData("2", MessageSourceKind.BuiltIn)]
        [InlineData(null, MessageSourceKind.BuiltIn)]
        public void MessageSources_Parse(string? text, MessageSourceKind expected) => Assert.Equal(expected, MessageSources.Parse(text!));

        [Fact]
        public void MessageSources_ToText_RoundTrips()
        {
            foreach (var k in new[] { MessageSourceKind.BuiltIn, MessageSourceKind.File, MessageSourceKind.Web })
                Assert.Equal(k, MessageSources.Parse(MessageSources.ToText(k)));
            Assert.Equal("Web", MessageSources.ToText(MessageSourceKind.Web));
        }
    }
}
