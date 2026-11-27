using System;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class BubbleTextTests
    {
        [Fact] public void Short_Text_Is_Untouched() => Assert.Equal("Hello", BubbleText.Fit("Hello"));

        [Fact] public void Long_Text_Is_Cut_With_An_Ellipsis_Within_The_Cap()
        {
            var r = BubbleText.Fit(new string('a', 500));
            Assert.True(r.Length <= BubbleText.MaxDisplayLength);
            Assert.EndsWith("…", r);
        }

        [Fact] public void The_Cut_Never_Splits_A_Surrogate_Pair_Or_An_Accented_Letter()
        {
            // the cap falls in the middle of an emoji pair, and in the middle of a letter plus its accent
            var emoji = new string('a', 198) + "\U0001F984\U0001F984";
            var r = BubbleText.Fit(emoji);
            Assert.DoesNotContain("\uD83E", r.Replace("\U0001F984", ""));
            Assert.True(r.Length <= BubbleText.MaxDisplayLength);

            var accent = new string('a', 198) + "ééé";
            r = BubbleText.Fit(accent);
            Assert.False(r.Contains("e") && !r.Contains("é") && accent.Contains("é"));
            Assert.True(r.Length <= BubbleText.MaxDisplayLength);
        }

        [Fact] public void A_Worst_Case_Name_Line_Is_Capped_After_Substitution()
        {
            var name = new string('N', 30);
            var line = string.Concat(Enumerable.Repeat("{name} ", 20)).TrimEnd();
            Assert.True(BubbleText.Fit(MessagePicker.Substitute(line, name)).Length <= BubbleText.MaxDisplayLength);
        }
    }

    public class SleepDerivedTests
    {
        class Clock { public DateTime Now; }

        [Fact] public void Weekend_Sleeps_And_Monday_Wakes_By_Itself()
        {
            var c = new Clock { Now = new DateTime(2026, 11, 28, 10, 0, 0) };   // Saturday
            var t = new SleepTracker(() => c.Now);
            var s = new Settings();
            Assert.True(t.Poll(s, true));
            c.Now = new DateTime(2026, 11, 29, 23, 59, 0);                       // Sunday night
            Assert.Null(t.Poll(s));
            c.Now = new DateTime(2026, 11, 30, 0, 1, 0);                         // Monday just after midnight
            Assert.False(t.Poll(s));
            Assert.False(t.Current);
        }

        [Fact] public void An_Explicit_Sleep_Survives_Monday()
        {
            var c = new Clock { Now = new DateTime(2026, 11, 29, 20, 0, 0) };
            var t = new SleepTracker(() => c.Now);
            var s = new Settings { Sleeping = true };
            Assert.True(t.Poll(s, true));
            c.Now = new DateTime(2026, 11, 30, 9, 0, 0);
            Assert.Null(t.Poll(s));
            Assert.True(t.Current);
            s.Sleeping = false;                                                  // the person wakes it
            Assert.False(t.Poll(s, true));
        }

        [Fact] public void The_Clock_Is_Read_At_Most_Once_A_Minute_Unless_Forced()
        {
            var c = new Clock { Now = new DateTime(2026, 11, 27, 10, 0, 0) };    // Friday
            var t = new SleepTracker(() => c.Now);
            var s = new Settings();
            Assert.Null(t.Poll(s, true));
            s.Sleeping = true;
            c.Now = c.Now.AddSeconds(20);
            Assert.Null(t.Poll(s));                                              // too soon to look again
            c.Now = c.Now.AddSeconds(45);
            Assert.True(t.Poll(s));
        }

        [Fact] public void The_Menu_Offers_Wake_Up_Only_For_An_Explicit_Sleep()
        {
            var saturdayOnly = new Settings();
            var b = new PetMenuBuilder(() => saturdayOnly, () => { }, () => new DateTime(2026, 11, 28, 10, 0, 0), new PetMenuActions());
            Assert.Equal("Sleep now", b.Build()[0].Text);
            var explicitSleep = new Settings { Sleeping = true };
            b = new PetMenuBuilder(() => explicitSleep, () => { }, () => new DateTime(2026, 11, 28, 10, 0, 0), new PetMenuActions());
            Assert.Equal("Wake up", b.Build()[0].Text);
        }
    }

    public class GuardRecoveryTests
    {
        static FailureGuard Tripped()
        {
            var g = new FailureGuard(2);
            g.Run(() => throw new Exception()); g.Run(() => throw new Exception());
            Assert.True(g.Tripped);
            return g;
        }

        [Fact] public void A_Tripped_Guard_Can_Be_Reset_And_Runs_Again()
        {
            var g = Tripped();
            Assert.True(g.TryRecover(new DateTime(2026, 11, 24, 10, 0, 0)));
            Assert.False(g.Tripped);
            Assert.True(g.Run(() => { }));
        }

        [Fact] public void Recovery_Is_Allowed_At_Most_Once_Every_Thirty_Seconds()
        {
            var g = Tripped();
            var t0 = new DateTime(2026, 11, 24, 10, 0, 0);
            Assert.True(g.TryRecover(t0));
            g.Run(() => throw new Exception()); g.Run(() => throw new Exception());
            Assert.True(g.Tripped);
            Assert.False(g.TryRecover(t0.AddSeconds(10)));        // too soon
            Assert.True(g.Tripped);
            Assert.False(g.TryRecover(t0.AddSeconds(29)));
            Assert.True(g.TryRecover(t0.AddSeconds(30)));
        }

        [Fact] public void A_Guard_That_Is_Not_Tripped_Is_Left_Alone()
        {
            var g = new FailureGuard(3);
            Assert.False(g.TryRecover(DateTime.Now));
        }

        [Fact] public void The_Window_Restarts_The_Timer_Only_On_A_Display_Change_Or_An_Explicit_Action()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            var src = File.ReadAllText(Path.Combine(dir, "src", "UnicornPet.App", "PetWindow.xaml.cs"));
            Assert.Contains("TryRecover(", src);
            // never from the frame handler itself
            var frame = src.Substring(src.IndexOf("void OnFrame()", StringComparison.Ordinal));
            Assert.DoesNotContain("TryRestartAnimation", frame.Substring(0, frame.IndexOf("void OnDown", StringComparison.Ordinal)));
        }
    }

    public class NameConsentTests
    {
        class Source : IUserNameSource
        {
            public int Calls;
            public string DisplayName { get { Calls++; return "Sam Smith"; } }
            public string AccountName { get { Calls++; return "ssmith"; } }
        }

        [Fact] public void With_The_Option_Off_The_Name_Source_Is_Never_Touched()
        {
            var src = new Source();
            Assert.Equal("", FirstNameResolver.ResolveIfAllowed(new Settings { UseWindowsName = false }, src));
            Assert.Equal(0, src.Calls);
        }

        [Fact] public void With_The_Option_On_The_Name_Is_Looked_Up()
        {
            var src = new Source();
            Assert.Equal("Sam", FirstNameResolver.ResolveIfAllowed(new Settings { UseWindowsName = true }, src));
            Assert.True(src.Calls > 0);
        }
    }

    public class SettingsCommitTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "up-sc-" + Guid.NewGuid().ToString("N"));
        public SettingsCommitTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact] public void A_Failed_Save_Is_Reported_And_The_Change_Stays_In_Memory()
        {
            var path = Path.Combine(_dir, "settings.json");
            var store = new SettingsStore(path);
            Assert.True(store.Save(new Settings()));
            var live = new Settings { UseWindowsName = true };
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))   // another process has the file locked
            {
                var msg = SettingsCommit.Run(() => live.UseWindowsName = false, () => store.Save(live));
                Assert.Equal("Couldn't save settings", msg);
            }
            Assert.False(live.UseWindowsName);                      // the opt-out applies at once
            Assert.True(store.Load().UseWindowsName);               // and the disk still says the old thing, honestly
        }

        [Fact] public void A_Good_Save_Reports_Nothing()
        {
            var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
            var live = new Settings();
            Assert.Equal("", SettingsCommit.Run(() => live.CheekyMode = false, () => store.Save(live)));
        }

        [Fact] public void A_Throwing_Save_Counts_As_Failed()
        {
            Assert.Equal("Couldn't save settings", SettingsCommit.Run(() => { }, () => throw new IOException()));
        }

        [Fact] public void The_Dialog_Uses_The_Commit_Helper_And_Reports_The_Message()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            var src = File.ReadAllText(Path.Combine(dir, "src", "UnicornPet.App", "SettingsWindow.xaml.cs"));
            Assert.Contains("SettingsCommit.Run(", src);
            Assert.Contains("ErrorText.Text = message", src);
        }
    }

    public class JsonNumberTests
    {
        [Theory] [InlineData("1e999")] [InlineData("-1e999")] [InlineData("[1e400]")]
        public void Numbers_That_Overflow_Are_Refused_On_Every_Runtime(string json)
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }

        [Fact] public void Ordinary_Numbers_Still_Parse() => Assert.Equal(1.5e3, MiniJson.Parse("1.5e3"));
    }

    public class SchedulerOutsideHoursTests
    {
        [Fact] public void A_Random_Event_That_Just_Became_Due_Before_Work_Hours_Stays_Silent_And_Is_Rescheduled()
        {
            DateTime now = new DateTime(2026, 11, 24, 7, 0, 0);      // Tuesday, an hour before work starts
            var s = new Scheduler(MessageBank.Default, () => now, new Random(1));
            for (int i = 0; i < 30; i++) { now = now.AddSeconds(10); Assert.Null(s.Tick(new Settings { ScheduledEnabled = false })); }
            // the first random event (2 to 5 minutes in) came due during those 5 minutes, outside working hours: nothing was said
            now = new DateTime(2026, 11, 24, 7, 6, 0);
            Assert.Null(s.Tick(new Settings { ScheduledEnabled = false }));
            // and it is not stuck: the next one is 20+ minutes out, so nothing fires at 07:10 either
            now = new DateTime(2026, 11, 24, 7, 10, 0);
            Assert.Null(s.Tick(new Settings { ScheduledEnabled = false }));
        }
    }
}
