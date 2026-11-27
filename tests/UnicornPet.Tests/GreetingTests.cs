using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class GreetingTests
    {
        class Clock { public DateTime Now; public Func<DateTime> F => () => Now; }

        static readonly DateTime Mon = new DateTime(2026, 11, 23);   // a Monday
        static readonly DateTime Sat = new DateTime(2026, 11, 28);

        static (Scheduler s, Clock c) Make(DateTime start, int seed = 1)
        {
            var c = new Clock { Now = start };
            return (new Scheduler(MessageBank.Default, c.F, new Random(seed)), c);
        }

        static HashSet<string> Rendered(IEnumerable<string> pool) => new HashSet<string>(pool.Select(p => MessagePicker.Substitute(p, "")));

        [Fact] public void Start_Weekday_GreetsOnce()
        {
            var (s, _) = Make(Mon.AddHours(9));
            var g = s.Greet(new Settings(), firstRun: false);
            Assert.NotNull(g);
            Assert.Equal(SpeechKind.Manual, g.Kind);
            Assert.Contains(g.Text, Rendered(MessageBank.Default.Hello));
            Assert.Null(s.Greet(new Settings(), firstRun: false));
        }

        [Fact] public void Start_FirstRun_UsesWelcomePool_AndMentionsRightClick()
        {
            var (s, _) = Make(Mon.AddHours(9));
            var g = s.Greet(new Settings(), firstRun: true);
            Assert.NotNull(g);
            Assert.Contains(g.Text, Rendered(MessageBank.Default.Welcome));
            Assert.All(MessageBank.Default.Welcome, w => Assert.Contains("Right-click", w));
        }

        [Fact] public void Start_WithName_GreetingIsPersonal()
        {
            var (s, _) = Make(Mon.AddHours(9));
            s.UpdateName("Sam");
            var g = s.Greet(new Settings(), firstRun: true);
            Assert.Contains("Sam", g.Text);
            Assert.DoesNotContain("{name}", g.Text);
        }

        [Fact] public void Start_Quiet_NoGreeting()
        {
            var (s, c) = Make(Mon.AddHours(9));
            Assert.Null(s.Greet(new Settings { QuietUntil = c.Now.AddHours(1) }, false));
        }

        [Fact] public void Start_Sleeping_NoGreeting()
        {
            var (s, _) = Make(Mon.AddHours(9));
            Assert.Null(s.Greet(new Settings { Sleeping = true }, false));
        }

        [Fact] public void Start_Weekend_NoGreeting()
        {
            var (s, _) = Make(Sat.AddHours(9));
            Assert.Null(s.Greet(new Settings(), false));
        }

        [Fact] public void Quiet_ThatHasExpired_DoesNotBlockGreeting()
        {
            var (s, c) = Make(Mon.AddHours(9));
            Assert.NotNull(s.Greet(new Settings { QuietUntil = c.Now.AddMinutes(-1) }, false));
        }

        [Fact] public void Greeting_CountsAsLastSpeech_NoRandomWithinFiveMinutes()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var (s, c) = Make(Mon.AddHours(9), seed);
                var greetAt = c.Now;
                Assert.NotNull(s.Greet(new Settings(), false));
                DateTime? first = null;
                for (int i = 0; i < 20 && first == null; i++)
                {
                    c.Now = c.Now.AddSeconds(30);
                    var sp = s.Tick(new Settings());
                    if (sp != null) first = c.Now;
                }
                Assert.NotNull(first);
                Assert.True((first.Value - greetAt).TotalMinutes >= 5 - 1e-9, "random at " + (first.Value - greetAt));
            }
        }

        [Fact] public void FirstRandom_Between2And5Minutes()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var (s, c) = Make(Mon.AddHours(9), seed);
                var start = c.Now;
                DateTime? first = null;
                for (int i = 0; i < 30 && first == null; i++)
                {
                    c.Now = c.Now.AddSeconds(15);
                    var sp = s.Tick(new Settings());
                    if (sp != null) { Assert.Equal(SpeechKind.Random, sp.Kind); first = c.Now; }
                }
                Assert.NotNull(first);
                var mins = (first.Value - start).TotalMinutes;
                Assert.InRange(mins, 2, 5.5);
            }
        }

        [Fact] public void SubsequentRandoms_StayTwentyToFortyMinutes()
        {
            var (s, c) = Make(Mon.AddHours(9), 3);
            var fires = new List<DateTime>();
            var end = Mon.AddHours(12).AddMinutes(30);
            while (c.Now < end)
            {
                if (s.Tick(new Settings()) != null) fires.Add(c.Now);
                c.Now = c.Now.AddMinutes(1);
            }
            Assert.True(fires.Count >= 3);
            for (int i = 1; i < fires.Count; i++) Assert.InRange((fires[i] - fires[i - 1]).TotalMinutes, 20, 41);
        }

        [Fact] public void Sleeping_NotPersisted()
        {
            var p = Path.Combine(Path.GetTempPath(), "up-sl-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var st = new SettingsStore(p);
                st.Save(new Settings { Sleeping = true, CheekyMode = false });
                Assert.DoesNotContain("Sleeping", File.ReadAllText(p));
                var l = st.Load();
                Assert.False(l.Sleeping);
                Assert.False(l.CheekyMode);
                File.WriteAllText(p, "{\"Sleeping\":true}");
                Assert.False(st.Load().Sleeping);
            }
            finally { try { File.Delete(p); } catch { } }
        }

        [Fact] public void PastQuietUntil_ClearedOnLoad()
        {
            var p = Path.Combine(Path.GetTempPath(), "up-q-" + Guid.NewGuid().ToString("N") + ".json");
            var now = new DateTime(2026, 11, 24, 10, 0, 0);
            try
            {
                var st = new SettingsStore(p, () => now);
                st.Save(new Settings { QuietUntil = now.AddMinutes(-5) });
                Assert.Null(st.Load().QuietUntil);
                st.Save(new Settings { QuietUntil = now.AddHours(2) });
                Assert.Equal(now.AddHours(2), st.Load().QuietUntil);
            }
            finally { try { File.Delete(p); } catch { } }
        }

        [Fact] public void SettingsStore_Exists_ReflectsFirstRun()
        {
            var p = Path.Combine(Path.GetTempPath(), "up-ex-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var st = new SettingsStore(p);
                Assert.False(st.Exists);
                st.Save(new Settings());
                Assert.True(st.Exists);
            }
            finally { try { File.Delete(p); } catch { } }
        }

        [Fact] public void Bank_WelcomeAndHello_FallBackWhenFileLacksThem()
        {
            var p = Path.Combine(Path.GetTempPath(), "up-wb-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(p, "{\"name\":\"X\",\"scheduled\":[{\"id\":\"a\",\"time\":\"08:00\",\"pool\":[\"hi\"]}],\"random\":[\"r\"],\"cheeky\":[\"c\"]}");
                var b = MessageBank.Load(p);
                Assert.NotEmpty(b.Welcome);
                Assert.NotEmpty(b.Hello);
                File.WriteAllText(p, "{\"name\":\"X\",\"scheduled\":[{\"id\":\"a\",\"time\":\"08:00\",\"pool\":[\"hi\"]}],\"random\":[\"r\"],\"cheeky\":[\"c\"],\"welcome\":[\"Custom welcome\"],\"hello\":[\"Custom hello\"]}");
                b = MessageBank.Load(p);
                Assert.Equal(new[] { "Custom welcome" }, b.Welcome.ToArray());
                Assert.Equal(new[] { "Custom hello" }, b.Hello.ToArray());
            }
            finally { try { File.Delete(p); } catch { } }
        }

        [Fact] public void DefaultPools_HaveEnoughVariety()
        {
            Assert.True(MessageBank.Default.Welcome.Count >= 3);
            Assert.True(MessageBank.Default.Hello.Count >= 6);
        }
    }
}
