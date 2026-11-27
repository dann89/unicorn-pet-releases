using System;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class SettingsStoreTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "up-set-" + Guid.NewGuid().ToString("N"));
        string P => Path.Combine(_dir, "settings.json");
        public SettingsStoreTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Load_Missing_ReturnsDefaults()
        {
            var s = new SettingsStore(P).Load();
            Assert.True(s.CheekyMode);
            Assert.True(s.ScheduledEnabled);
            Assert.True(s.StartWithWindows);
            Assert.False(s.Sleeping);
            Assert.Null(s.QuietUntil);
        }

        [Fact]
        public void Load_Corrupt_ReturnsDefaults()
        {
            File.WriteAllText(P, "{{{ garbage");
            var s = new SettingsStore(P).Load();
            Assert.True(s.CheekyMode);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new SettingsStore(P, () => new DateTime(2026, 11, 24, 12, 0, 0));
            var q = new DateTime(2026, 11, 25, 8, 0, 0);
            store.Save(new Settings { CheekyMode = false, ScheduledEnabled = false, StartWithWindows = false, Sleeping = true, QuietUntil = q, UpdateUrl = "https://x/y.json", LastUpdateCheck = q });
            var s = store.Load();
            Assert.False(s.CheekyMode);
            Assert.False(s.ScheduledEnabled);
            Assert.False(s.StartWithWindows);
            Assert.False(s.Sleeping); // session-only, never persisted
            Assert.Equal(q, s.QuietUntil);
            Assert.Equal("https://x/y.json", s.UpdateUrl);
            Assert.Equal(q, s.LastUpdateCheck);
        }

        [Fact]
        public void Save_IsAtomic_NoPartialFileOnFailure()
        {
            var store = new SettingsStore(P);
            store.Save(new Settings { CheekyMode = false });
            using (new FileStream(P, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                store.Save(new Settings { CheekyMode = true });
            }
            Assert.False(store.Load().CheekyMode);
            Assert.DoesNotContain(Directory.GetFiles(_dir), f => !f.EndsWith("settings.json"));
        }

        [Fact]
        public void Save_CreatesMissingDirectory()
        {
            var p = Path.Combine(_dir, "sub", "settings.json");
            new SettingsStore(p).Save(new Settings { CheekyMode = false });
            Assert.False(new SettingsStore(p).Load().CheekyMode);
        }

        // the exact file the 1.0.0 work-safe exe writes on its first run (recorded from a real run)
        const string FirstRun100 = "{\n  \"CheekyMode\": true,\n  \"ScheduledEnabled\": true,\n  \"QuietUntil\": null,\n  \"CustomName\": \"\",\n  \"UseWindowsName\": true,\n  \"FetchMessages\": false,\n  \"LastMessageCheck\": null,\n  \"RemoteMessagesVersion\": \"\"\n}\n";

        [Fact]
        public void Load_FileWithoutGameHighScore_IsZero()
        {
            File.WriteAllText(P, FirstRun100);
            var s = new SettingsStore(P).Load();
            Assert.Equal(0, s.GameHighScore);
            Assert.True(s.CheekyMode);
            Assert.True(s.UseWindowsName);
        }

        [Fact]
        public void Load_BadGameHighScore_IsZero_AndOtherFieldsSurvive()
        {
            File.WriteAllText(P, "{ \"CheekyMode\": false, \"GameHighScore\": \"lots\" }");
            var s = new SettingsStore(P).Load();
            Assert.Equal(0, s.GameHighScore);
            Assert.False(s.CheekyMode);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsGameHighScore()
        {
            var store = new SettingsStore(P);
            Assert.True(store.Save(new Settings { GameHighScore = 1234 }));
            Assert.Equal(1234, store.Load().GameHighScore);
        }

        [Fact]
        public void Save_WritesGameHighScoreLast()
        {
            new SettingsStore(P).Save(new Settings { GameHighScore = 77 });
            var text = File.ReadAllText(P).TrimEnd();
            Assert.EndsWith("\"GameHighScore\": 77\n}", text.Replace("\r\n", "\n"));
        }
    }
}


