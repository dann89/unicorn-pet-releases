using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>Pet size, the unicorn's own name and the Sparkles switch: settings, size table, name text and menu.</summary>
    public class PetOptionsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "up-opt-" + Guid.NewGuid().ToString("N"));
        string SettingsPath => Path.Combine(_dir, "settings.json");
        public PetOptionsTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        // ---------- settings ----------

        [Fact]
        public void NewSettings_Defaults_LargeFifiSparklesAndScheduledOn()
        {
            var s = new Settings();
            Assert.Equal(PetSize.Large, s.PetSize);
            Assert.Equal("Fifi", s.PetName);
            Assert.True(s.Sparkles);
            Assert.True(s.ScheduledEnabled);
            Assert.True(s.CheekyMode);
            Assert.Equal(PetSize.Large, default(PetSize));
        }

        [Fact]
        public void Load_MissingFile_GivesDefaults()
        {
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal(PetSize.Large, s.PetSize);
            Assert.Equal("Fifi", s.PetName);
            Assert.True(s.Sparkles);
        }

        // the 2.0.0 game build's first-run file (no pet options in it)
        const string GameBuildFile = "{\n  \"CheekyMode\": false,\n  \"ScheduledEnabled\": true,\n  \"QuietUntil\": null,\n  \"CustomName\": \"Alex\",\n  \"UseWindowsName\": true,\n  \"FetchMessages\": false,\n  \"LastMessageCheck\": null,\n  \"RemoteMessagesVersion\": \"\",\n  \"GameHighScore\": 420\n}\n";

        [Fact]
        public void Load_OlderFileWithoutOptions_GivesDefaults_AndKeepsTheRest()
        {
            File.WriteAllText(SettingsPath, GameBuildFile);
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal(PetSize.Large, s.PetSize);
            Assert.Equal("Fifi", s.PetName);
            Assert.True(s.Sparkles);
            Assert.False(s.CheekyMode);
            Assert.Equal("Alex", s.CustomName);
            Assert.Equal(420, s.GameHighScore);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsTheOptions()
        {
            var store = new SettingsStore(SettingsPath);
            Assert.True(store.Save(new Settings { PetSize = PetSize.Small, PetName = "Sparkle", Sparkles = false, GameHighScore = 9 }));
            var s = store.Load();
            Assert.Equal(PetSize.Small, s.PetSize);
            Assert.Equal("Sparkle", s.PetName);
            Assert.False(s.Sparkles);
            Assert.Equal(9, s.GameHighScore);

            store.Save(new Settings { PetSize = PetSize.Medium });
            Assert.Equal(PetSize.Medium, store.Load().PetSize);
        }

        [Fact]
        public void Save_WritesSizeAsText_AndKeepsGameHighScoreLast()
        {
            new SettingsStore(SettingsPath).Save(new Settings { PetSize = PetSize.Medium, PetName = "Sparkle", Sparkles = true, GameHighScore = 5 });
            var text = File.ReadAllText(SettingsPath).Replace("\r\n", "\n");
            Assert.Contains("\"PetSize\": \"Medium\",\n  \"PetName\": \"Sparkle\",\n  \"Sparkles\": true,\n  \"GameHighScore\": 5\n}", text);
        }

        [Theory]
        [InlineData("{ \"PetSize\": \"Huge\", \"CheekyMode\": false }")]
        [InlineData("{ \"PetSize\": 2, \"CheekyMode\": false }")]
        [InlineData("{ \"PetSize\": \"2\", \"CheekyMode\": false }")]
        [InlineData("{ \"PetSize\": null, \"CheekyMode\": false }")]
        [InlineData("{ \"PetSize\": true, \"CheekyMode\": false }")]
        public void Load_BadSize_IsLarge_AndOtherFieldsSurvive(string json)
        {
            File.WriteAllText(SettingsPath, json);
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal(PetSize.Large, s.PetSize);
            Assert.False(s.CheekyMode);
        }

        [Fact]
        public void Load_SizeIgnoresLetterCase()
        {
            File.WriteAllText(SettingsPath, "{ \"PetSize\": \"small\" }");
            Assert.Equal(PetSize.Small, new SettingsStore(SettingsPath).Load().PetSize);
        }

        [Theory]
        [InlineData("{name}")]
        [InlineData("Sparkle<b>")]
        [InlineData("1234")]
        [InlineData("Abcdefghijklmnopqrstuvwxyzabcde")]   // 31 letters
        public void Load_PetNameBreakingTheNameRules_IsUnnamed(string bad)
        {
            File.WriteAllText(SettingsPath, "{ \"PetName\": " + MiniJson.Quote(bad) + ", \"CheekyMode\": false }");
            var s = new SettingsStore(SettingsPath).Load();
            Assert.Equal("", s.PetName);
            Assert.False(s.CheekyMode);
        }

        [Fact]
        public void Load_SavedBlankPetName_StaysUnnamed()
        {
            new SettingsStore(SettingsPath).Save(new Settings { PetName = "" });
            Assert.Equal("", new SettingsStore(SettingsPath).Load().PetName);
        }

        [Fact]
        public void Load_PetName_IsNormalisedLikeTheUsersName()
        {
            File.WriteAllText(SettingsPath, "{ \"PetName\": \"  Zoé   Star \" }");
            Assert.Equal("Zoé Star", new SettingsStore(SettingsPath).Load().PetName);
        }

        [Theory]
        [InlineData("\"yes\"")]
        [InlineData("1")]
        [InlineData("null")]
        public void Load_BadSparkles_IsOn(string value)
        {
            File.WriteAllText(SettingsPath, "{ \"Sparkles\": " + value + " }");
            Assert.True(new SettingsStore(SettingsPath).Load().Sparkles);
        }

        // ---------- size table ----------

        [Theory]
        [InlineData(PetSize.Large, 1.0, 200, 200, 250)]
        [InlineData(PetSize.Medium, 0.7, 140, 140, 175)]
        [InlineData(PetSize.Small, 0.5, 100, 100, 125)]
        public void SizeTable(PetSize size, double scale, int sprite, int width, int height)
        {
            Assert.Equal(scale, PetSizes.Scale(size), 6);
            Assert.Equal(sprite, PetSizes.SpritePx(size));
            Assert.Equal(width, PetSizes.WindowWidth(size));
            Assert.Equal(height, PetSizes.WindowHeight(size));
        }

        [Fact]
        public void UnknownSizeValue_ActsAsLarge()
        {
            var odd = (PetSize)7;
            Assert.Equal(1.0, PetSizes.Scale(odd), 6);
            Assert.Equal(200, PetSizes.SpritePx(odd));
            Assert.Equal("Large", PetSizes.ToText(odd));
        }

        [Fact]
        public void SizeText_RoundTrips()
        {
            foreach (var size in new[] { PetSize.Small, PetSize.Medium, PetSize.Large })
                Assert.Equal(size, PetSizes.Parse(PetSizes.ToText(size)));
            Assert.Equal(PetSize.Large, PetSizes.Parse(null));
            Assert.Equal(PetSize.Large, PetSizes.Parse(""));
            Assert.Equal(PetSize.Large, PetSizes.Parse("1"));
            Assert.Equal(PetSize.Medium, PetSizes.Parse(" MEDIUM "));
        }

        [Fact]
        public void WalkSpeed_ScalesWithSize()
        {
            var b = new PetBehaviour(() => 0.0) { Force = PetState.WalkRight, SpeedScale = 0.5 };
            for (int i = 0; i < 5; i++) b.Step(TimeSpan.FromMilliseconds(200), 0, 10000);
            Assert.Equal(PetBehaviour.WalkSpeed * 0.5, b.X, 6);

            var big = new PetBehaviour(() => 0.0) { Force = PetState.WalkRight };
            for (int i = 0; i < 5; i++) big.Step(TimeSpan.FromMilliseconds(200), 0, 10000);
            Assert.Equal(PetBehaviour.WalkSpeed, big.X, 6);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(3.0)]
        public void WalkSpeed_NonsenseScale_IsTheOriginalSpeed(double scale)
        {
            var b = new PetBehaviour(() => 0.0) { Force = PetState.WalkRight, SpeedScale = scale };
            for (int i = 0; i < 5; i++) b.Step(TimeSpan.FromMilliseconds(200), 0, 10000);
            Assert.Equal(PetBehaviour.WalkSpeed, b.X, 6);
        }

        // ---------- the unicorn's name ----------

        [Fact]
        public void Intro_UsesBothNames()
        {
            Assert.Equal("Hi Alex, I'm Sparkle!", PetNameText.Intro("Alex", "Sparkle"));
            Assert.Equal("Hi, I'm Sparkle!", PetNameText.Intro("", "Sparkle"));
            Assert.Equal("Hi, I'm Sparkle!", PetNameText.Intro(null, "Sparkle"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("{name}")]
        public void Intro_NoValidPetName_IsNull(string? pet)
        {
            Assert.Null(PetNameText.Intro("Alex", pet));
        }

        [Fact]
        public void TrayAndTitle_FallBackToTheProductName()
        {
            Assert.Equal("Unicorn Pet", PetNameText.TrayText(""));
            Assert.Equal("Unicorn Pet", PetNameText.TrayText("{x}"));
            Assert.Equal("Sparkle", PetNameText.TrayText(" Sparkle "));
            Assert.Equal("Unicorn Pet settings", PetNameText.SettingsTitle(""));
            Assert.Equal("Sparkle – settings", PetNameText.SettingsTitle("Sparkle"));
            Assert.True(PetNameText.TrayText(new string('a', NameRules.MaxLength)).Length < 64);   // NotifyIcon.Text limit on .NET Framework
        }

        // ---------- menu ----------

        static IEnumerable<MenuItemModel> Flat(IEnumerable<MenuItemModel> items)
        {
            foreach (var i in items)
            {
                yield return i;
                if (i.Children != null) foreach (var c in Flat(i.Children)) yield return c;
            }
        }

        static string[] Texts(PetMenuBuilder b) => Flat(b.Build()).Where(i => !i.IsSeparator).Select(i => i.Text).ToArray();

        static PetMenuBuilder OptionsMenu(Settings s, List<string> calls)
        {
            return new PetMenuBuilder(() => s, () => calls.Add("save"), () => new DateTime(2026, 11, 25, 10, 0, 0), new PetMenuActions
            {
                StartHelp = () => { },
                MiniGame = () => calls.Add("game"),
                SizeChanged = size => calls.Add("size:" + size),
                SparklesChanged = on => calls.Add("sparkles:" + on),
            });
        }

        [Fact]
        public void Menu_WithOptions_HasSizeAndSparklesInOrder()
        {
            var b = OptionsMenu(new Settings(), new List<string>());
            Assert.Equal(new[] { "Sleep now", "Quiet", "1 hour", "Until tomorrow", "Off", "Make it talk now", "Mini game", "Cheeky mode", "Scheduled messages",
                "Size", "Small", "Medium", "Large", "Sparkles", "How to start with Windows", "Settings…", "About", "Quit" }, Texts(b));
        }

        [Fact]
        public void Menu_TicksTheCurrentSize_AndSparkles()
        {
            var s = new Settings { PetSize = PetSize.Medium, Sparkles = false };
            var items = Flat(OptionsMenu(s, new List<string>()).Build()).ToList();
            Assert.False(items.First(i => i.Text == "Small").IsChecked);
            Assert.True(items.First(i => i.Text == "Medium").IsChecked);
            Assert.False(items.First(i => i.Text == "Large").IsChecked);
            Assert.False(items.First(i => i.Text == "Sparkles").IsChecked);
            Assert.True(Flat(OptionsMenu(new Settings(), new List<string>()).Build()).First(i => i.Text == "Large").IsChecked);
        }

        [Fact]
        public void Menu_PickSize_StoresSavesThenNotifies()
        {
            var s = new Settings();
            var calls = new List<string>();
            Flat(OptionsMenu(s, calls).Build()).First(i => i.Text == "Small").OnClick();
            Assert.Equal(PetSize.Small, s.PetSize);
            Assert.Equal(new[] { "save", "size:Small" }, calls.ToArray());
        }

        [Fact]
        public void Menu_Sparkles_TogglesSavesThenNotifies()
        {
            var s = new Settings();
            var calls = new List<string>();
            var b = OptionsMenu(s, calls);
            b.Build().First(i => i.Text == "Sparkles").OnClick();
            Assert.False(s.Sparkles);
            b.Build().First(i => i.Text == "Sparkles").OnClick();
            Assert.True(s.Sparkles);
            Assert.Equal(new[] { "save", "sparkles:False", "save", "sparkles:True" }, calls.ToArray());
        }

        [Fact]
        public void Menu_CallbackThrows_IsSwallowed()
        {
            var s = new Settings();
            var b = new PetMenuBuilder(() => s, () => { }, () => DateTime.Now, new PetMenuActions
            {
                SizeChanged = size => throw new InvalidOperationException("x"),
                SparklesChanged = on => throw new InvalidOperationException("x"),
            });
            Flat(b.Build()).First(i => i.Text == "Medium").OnClick();
            b.Build().First(i => i.Text == "Sparkles").OnClick();
            Assert.Equal(PetSize.Medium, s.PetSize);
            Assert.False(s.Sparkles);
        }
    }
}
