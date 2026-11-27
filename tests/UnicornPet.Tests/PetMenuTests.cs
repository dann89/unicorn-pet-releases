using System;
using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class PetMenuTests
    {
        class Ctx
        {
            public Settings S = new Settings();
            public int Saves;
            public DateTime Now = new DateTime(2026, 11, 24, 10, 30, 0);
            public List<string> Calls = new List<string>();
            public PetMenuBuilder B;
            public Ctx()
            {
                B = new PetMenuBuilder(() => S, () => Saves++, () => Now, new PetMenuActions
                {
                    TalkNow = () => Calls.Add("talk"),
                    SleepChanged = v => Calls.Add("sleep:" + v),
                    StartupChanged = v => Calls.Add("startup:" + v),
                    OpenSettings = () => Calls.Add("settings"),
                    CheckUpdates = () => Calls.Add("update"),
                    About = () => Calls.Add("about"),
                    Quit = () => Calls.Add("quit"),
                });
            }
            public MenuItemModel Find(string startsWith) => Flat(B.Build()).First(i => i.Text != null && i.Text.StartsWith(startsWith));
        }

        static IEnumerable<MenuItemModel> Flat(IEnumerable<MenuItemModel> items)
        {
            foreach (var i in items)
            {
                yield return i;
                if (i.Children != null) foreach (var c in Flat(i.Children)) yield return c;
            }
        }

        [Fact]
        public void Menu_HasAllRequiredItems()
        {
            var texts = Flat(new Ctx().B.Build()).Where(i => !i.IsSeparator).Select(i => i.Text).ToList();
            foreach (var want in new[] { "Sleep now", "Quiet", "Make it talk now", "Cheeky mode", "Scheduled messages", "Start with Windows", "Settings", "Check for updates", "About", "Quit" })
                Assert.Contains(texts, t => t.StartsWith(want));
            Assert.Contains(texts, t => t.StartsWith("1 hour"));
            Assert.Contains(texts, t => t.StartsWith("Until tomorrow"));
            Assert.Contains(texts, t => t.StartsWith("Off"));
        }

        [Fact]
        public void SleepItem_TogglesAndPersists()
        {
            var c = new Ctx();
            c.Find("Sleep now").OnClick();
            Assert.True(c.S.Sleeping);
            Assert.Equal(1, c.Saves);
            Assert.Contains("sleep:True", c.Calls);
            var wake = c.Find("Wake up");
            wake.OnClick();
            Assert.False(c.S.Sleeping);
            Assert.Equal(2, c.Saves);
            Assert.Contains("sleep:False", c.Calls);
        }

        [Fact]
        public void CheekyToggle_FlipsPersistsAndChecks()
        {
            var c = new Ctx();
            Assert.True(c.Find("Cheeky mode").IsChecked);
            c.Find("Cheeky mode").OnClick();
            Assert.False(c.S.CheekyMode);
            Assert.False(c.Find("Cheeky mode").IsChecked);
            Assert.Equal(1, c.Saves);
        }

        [Fact]
        public void ScheduledToggle_FlipsAndPersists()
        {
            var c = new Ctx();
            c.Find("Scheduled messages").OnClick();
            Assert.False(c.S.ScheduledEnabled);
            Assert.False(c.Find("Scheduled messages").IsChecked);
            Assert.Equal(1, c.Saves);
        }

        [Fact]
        public void StartWithWindows_FlipsPersistsAndNotifies()
        {
            var c = new Ctx();
            c.Find("Start with Windows").OnClick();
            Assert.False(c.S.StartWithWindows);
            Assert.Equal(1, c.Saves);
            Assert.Contains("startup:False", c.Calls);
        }

        [Fact]
        public void Quiet_OneHour_SetsQuietUntilNowPlusOneHour()
        {
            var c = new Ctx();
            c.Find("1 hour").OnClick();
            Assert.Equal(c.Now.AddHours(1), c.S.QuietUntil);
            Assert.Equal(1, c.Saves);
        }

        [Fact]
        public void Quiet_UntilTomorrow_IsNextLocal0800()
        {
            var c = new Ctx();
            c.Find("Until tomorrow").OnClick();
            Assert.Equal(new DateTime(2026, 11, 25, 8, 0, 0), c.S.QuietUntil);
            c.Now = new DateTime(2026, 11, 24, 6, 0, 0);
            c.Find("Until tomorrow").OnClick();
            Assert.Equal(new DateTime(2026, 11, 24, 8, 0, 0), c.S.QuietUntil);
        }

        [Fact]
        public void Quiet_Off_ClearsQuietUntil()
        {
            var c = new Ctx();
            c.S.QuietUntil = c.Now.AddHours(3);
            c.Find("Off").OnClick();
            Assert.Null(c.S.QuietUntil);
            Assert.Equal(1, c.Saves);
        }

        [Fact]
        public void Quiet_OffIsChecked_OnlyWhenNoActiveQuiet()
        {
            var c = new Ctx();
            Assert.True(c.Find("Off").IsChecked);
            c.S.QuietUntil = c.Now.AddMinutes(10);
            Assert.False(c.Find("Off").IsChecked);
            c.S.QuietUntil = c.Now.AddMinutes(-10);
            Assert.True(c.Find("Off").IsChecked);
        }

        [Fact]
        public void ActionItems_InvokeCallbacksWithoutChangingSettings()
        {
            var c = new Ctx();
            c.Find("Make it talk now").OnClick();
            c.Find("Settings").OnClick();
            c.Find("Check for updates").OnClick();
            c.Find("About").OnClick();
            c.Find("Quit").OnClick();
            Assert.Equal(new[] { "talk", "settings", "update", "about", "quit" }, c.Calls);
            Assert.Equal(0, c.Saves);
        }
    }
}


namespace UnicornPet.Tests
{
    public class WorkSafeMenuTests
    {
        static System.Collections.Generic.List<string> Texts(PetMenuBuilder b)
        {
            var l = new System.Collections.Generic.List<string>();
            void Walk(System.Collections.Generic.IEnumerable<MenuItemModel> items) { foreach (var i in items) { if (!i.IsSeparator) l.Add(i.Text); if (i.Children != null) Walk(i.Children); } }
            Walk(b.Build());
            return l;
        }

        [Fact] public void StartHelp_ReplacesTheToggle_AndNoUpdateItem()
        {
            var calls = new System.Collections.Generic.List<string>();
            var settings = new Settings();
            var b = new PetMenuBuilder(() => settings, () => calls.Add("save"), () => new DateTime(2026, 11, 24, 10, 0, 0),
                new PetMenuActions { StartHelp = () => calls.Add("help"), OpenSettings = () => calls.Add("settings"), About = () => calls.Add("about"), Quit = () => calls.Add("quit"), TalkNow = () => calls.Add("talk") });
            var texts = Texts(b);
            Assert.Contains("How to start with Windows", texts);
            Assert.DoesNotContain("Start with Windows", texts);
            Assert.DoesNotContain("Check for updates", texts);
            Assert.Contains("Settings…", texts);
            var help = b.Build().First(i => i.Text == "How to start with Windows");
            Assert.False(help.IsChecked);
            help.OnClick();
            Assert.Equal(new[] { "help" }, calls.ToArray());
            Assert.True(settings.StartWithWindows);
        }

        [Fact] public void WorkSafeMenu_HasExactlyTheExpectedItems()
        {
            var settings = new Settings();
            var b = new PetMenuBuilder(() => settings, () => { }, () => DateTime.Now, new PetMenuActions { StartHelp = () => { } });
            Assert.Equal(new[] { "Sleep now", "Quiet", "1 hour", "Until tomorrow", "Off", "Make it talk now", "Cheeky mode", "Scheduled messages", "How to start with Windows", "Settings…", "About", "Quit" }, Texts(b).ToArray());
        }

        static PetMenuBuilder GameMenu(System.Collections.Generic.List<string> calls, System.Func<bool>? paused)
        {
            var settings = new Settings();
            return new PetMenuBuilder(() => settings, () => calls.Add("save"), () => new DateTime(2026, 11, 24, 10, 0, 0),
                new PetMenuActions { StartHelp = () => { }, MiniGame = () => calls.Add("game"), MiniGamePaused = paused });
        }

        [Fact] public void MiniGame_NotSet_MenuIsUnchanged()
        {
            var settings = new Settings();
            var b = new PetMenuBuilder(() => settings, () => { }, () => DateTime.Now, new PetMenuActions { StartHelp = () => { }, MiniGamePaused = () => true });
            Assert.DoesNotContain(Texts(b), t => t.Contains("ini game"));
        }

        [Fact] public void WorkSafeMenu_WithMiniGame_HasThirteenItemsInOrder()
        {
            var b = GameMenu(new System.Collections.Generic.List<string>(), null);
            Assert.Equal(new[] { "Sleep now", "Quiet", "1 hour", "Until tomorrow", "Off", "Make it talk now", "Mini game", "Cheeky mode", "Scheduled messages", "How to start with Windows", "Settings…", "About", "Quit" }, Texts(b).ToArray());
            Assert.False(b.Build().First(i => i.Text == "Mini game").IsChecked);
        }

        [Fact] public void MiniGame_Paused_SaysResume()
        {
            var b = GameMenu(new System.Collections.Generic.List<string>(), () => true);
            Assert.Contains("Resume mini game", Texts(b));
            Assert.DoesNotContain("Mini game", Texts(b));
        }

        [Fact] public void MiniGame_PausedFuncThrows_SaysMiniGame()
        {
            var b = GameMenu(new System.Collections.Generic.List<string>(), () => throw new InvalidOperationException("x"));
            Assert.Contains("Mini game", Texts(b));
        }

        [Fact] public void MiniGame_Click_CallsAction_SavesNothing()
        {
            var calls = new System.Collections.Generic.List<string>();
            var b = GameMenu(calls, () => false);
            b.Build().First(i => i.Text == "Mini game").OnClick();
            Assert.Equal(new[] { "game" }, calls.ToArray());
        }
    }
}
