using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>Idle sparkle poop: idle time from the last-input tick, the shell's busy state, when a party starts and stops, roaming and piece placement, the menu item.</summary>
    public class IdlePartyTests
    {
        sealed class FakeIdle : IIdleSource
        {
            public uint Now, Last;
            public bool Fail;
            public int Calls;
            public uint NowTick { get { Calls++; return Now; } }
            public bool TryGetLastInputTick(out uint tick) { Calls++; tick = Last; return !Fail; }
        }

        sealed class FakePresence : IPresenceSource
        {
            public bool Accepts = true;
            public int Calls;
            public bool AcceptsNotifications() { Calls++; return Accepts; }
        }

        static (IdleParty party, FakeIdle idle, FakePresence presence) Make(uint now = 1000000u, uint last = 1000000u)
        {
            var idle = new FakeIdle { Now = now, Last = last };
            var presence = new FakePresence();
            return (new IdleParty(idle, presence), idle, presence);
        }

        /// <summary>Starts a party: three idle minutes with the shell accepting.</summary>
        static (IdleParty party, FakeIdle idle, FakePresence presence) Started()
        {
            var m = Make();
            m.idle.Now = m.idle.Last + IdleParty.IdleDelayMs;
            Assert.Equal(PartyChange.Start, m.party.Poll(true, false));
            return m;
        }

        // ---------- idle time ----------

        [Theory]
        [InlineData(1000u, 400u, 600u)]
        [InlineData(5u, 4294967290u, 11u)]   // the tick wrapped after 49.7 days
        [InlineData(400u, 1000u, 0u)]        // last input a little "ahead" of now
        [InlineData(0u, 0u, 0u)]
        public void IdleMs_Cases(uint now, uint last, uint expected) => Assert.Equal(expected, IdleMath.IdleMs(now, last));

        [Fact]
        public void IdleMs_ExactlyHalfRange_IsStillIdleTime()
        {
            Assert.Equal(0x7FFFFFFFu, IdleMath.IdleMs(0x7FFFFFFFu, 0u));
            Assert.Equal(0u, IdleMath.IdleMs(0x80000000u, 0u));
        }

        [Theory]
        [InlineData(0, 5, true)]
        [InlineData(0, 2, false)]
        [InlineData(0, 1, false)]
        [InlineData(0, 6, false)]
        [InlineData(0, 0, false)]
        [InlineData(-2147467259, 5, false)]
        public void Accepts_OnlyOnSuccessAndState5(int hr, int state, bool expected) => Assert.Equal(expected, PresenceCodes.Accepts(hr, state));

        // ---------- when a party starts and stops ----------

        [Fact]
        public void Disabled_NeverAsksEitherSource()
        {
            var (party, idle, presence) = Make();
            for (int i = 0; i < 100; i++)
            {
                idle.Now += 5000;
                Assert.Equal(PartyChange.None, party.Poll(false, false));
            }
            Assert.Equal(0, idle.Calls);
            Assert.Equal(0, presence.Calls);
        }

        [Fact]
        public void Starts_AfterThreeMinutes_WhenAccepting()
        {
            var (party, idle, presence) = Make();
            idle.Now = idle.Last + 179999u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            Assert.False(party.Active);
            idle.Now = idle.Last + 180000u;
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
            Assert.True(party.Active);
        }

        [Fact]
        public void Presence_IsNotAsked_BeforeThreeMinutes()
        {
            var (party, idle, presence) = Make();
            for (uint t = 0; t <= 179999u; t += 5000u) { idle.Now = idle.Last + t; party.Poll(true, false); }
            idle.Now = idle.Last + 179999u;
            party.Poll(true, false);
            Assert.Equal(0, presence.Calls);
        }

        [Fact]
        public void Busy_HoldsItBack_ThenStartsWhenFree()
        {
            var (party, idle, presence) = Make();
            presence.Accepts = false;
            idle.Now = idle.Last + 180000u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            idle.Now += 5000u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            presence.Accepts = true;
            idle.Now += 5000u;
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
        }

        [Fact]
        public void Blocked_HoldsItBack()
        {
            var (party, idle, presence) = Make();
            idle.Now = idle.Last + 600000u;
            Assert.Equal(PartyChange.None, party.Poll(true, true));
            Assert.Equal(0, presence.Calls);
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
        }

        [Fact]
        public void AnyInput_Stops_AtOnce()
        {
            var (party, idle, presence) = Started();
            idle.Now += 100u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            idle.Last += 1u;
            Assert.Equal(PartyChange.Stop, party.Poll(true, false));
            Assert.False(party.Active);
        }

        [Fact]
        public void BecomingBusy_Stops_WithinFiveSeconds()
        {
            var (party, idle, presence) = Started();
            var start = idle.Now;
            int askedAtStart = presence.Calls;
            presence.Accepts = false;
            uint stoppedAt = 0;
            for (uint t = 100u; t <= 10000u; t += 100u)
            {
                idle.Now = start + t;
                if (party.Poll(true, false) == PartyChange.Stop) { stoppedAt = t; break; }
            }
            Assert.Equal(5000u, stoppedAt);
            Assert.Equal(askedAtStart + 1, presence.Calls);
        }

        [Fact]
        public void Blocked_Or_Disabled_WhileActive_Stops()
        {
            var a = Started();
            Assert.Equal(PartyChange.Stop, a.party.Poll(true, true));
            Assert.False(a.party.Active);
            var b = Started();
            Assert.Equal(PartyChange.Stop, b.party.Poll(false, false));
            Assert.False(b.party.Active);
        }

        [Fact]
        public void AfterAStopWithoutInput_NoRestart_UntilInput()
        {
            var (party, idle, presence) = Started();
            presence.Accepts = false;
            idle.Now += 5000u;
            Assert.Equal(PartyChange.Stop, party.Poll(true, false));
            presence.Accepts = true;
            for (int i = 0; i < 2000; i++)   // hours, no input
            {
                idle.Now += 5000u;
                Assert.Equal(PartyChange.None, party.Poll(true, false));
            }
            idle.Last = idle.Now;   // input
            idle.Now += 5000u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            idle.Now = idle.Last + IdleParty.IdleDelayMs;
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
        }

        [Fact]
        public void Cancel_BehavesLikeThat_Too()
        {
            var (party, idle, presence) = Started();
            party.Cancel();
            Assert.False(party.Active);
            idle.Now += 600000u;
            Assert.Equal(PartyChange.None, party.Poll(true, false));
            idle.Last = idle.Now;
            idle.Now += IdleParty.IdleDelayMs;
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
        }

        [Fact]
        public void Cancel_WhileWaiting_ChangesNothing()
        {
            var (party, idle, presence) = Make();
            party.Cancel();
            idle.Now = idle.Last + IdleParty.IdleDelayMs;
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
        }

        [Fact]
        public void SourceFailure_CountsAsInput()
        {
            var (party, idle, presence) = Make();
            idle.Fail = true;
            for (int i = 0; i < 200; i++)
            {
                idle.Now += 5000u;
                Assert.Equal(PartyChange.None, party.Poll(true, false));
            }
            Assert.Equal(0, presence.Calls);
            var s = Started();
            s.idle.Fail = true;
            Assert.Equal(PartyChange.Stop, s.party.Poll(true, false));
            Assert.False(s.party.Active);
        }

        [Fact]
        public void TickWrap_DuringAParty_DoesNotStopIt()
        {
            uint last = 4294700000u;
            var idle = new FakeIdle { Last = last, Now = unchecked(last + IdleParty.IdleDelayMs) };   // wraps past 0 on the way
            var presence = new FakePresence();
            var party = new IdleParty(idle, presence);
            Assert.Equal(PartyChange.Start, party.Poll(true, false));
            for (int i = 0; i < 100; i++)
            {
                idle.Now = unchecked(idle.Now + 5000u);
                Assert.Equal(PartyChange.None, party.Poll(true, false));
                Assert.True(party.Active);
            }
            Assert.True(idle.Now < 1000000u);   // it did wrap
        }

        // ---------- roaming and pieces ----------

        static PoopRoam Roam(uint seed = 7) { var r = new Lcg(seed); return new PoopRoam(r.Next); }

        [Fact]
        public void Roam_StaysInside_For10000Steps()
        {
            var roam = Roam();
            roam.Begin(1920, 1040, 200, 250, 900, 790, 1);
            for (int i = 0; i < 10000; i++)
            {
                PoopDrop d;
                roam.Advance(0.05, out d);
                Assert.InRange(roam.X, 0, 1720);
                Assert.InRange(roam.Y, 0, 790);
            }
        }

        [Fact]
        public void Roam_DropsEveryFiveSeconds_UpToTheCap()
        {
            var roam = Roam();
            roam.Begin(1920, 1040, 200, 250, 900, 790, 1);
            var dropSteps = new List<int>();
            for (int i = 1; i <= 5000; i++)
            {
                PoopDrop d;
                if (roam.Advance(0.05, out d)) dropSteps.Add(i);
            }
            Assert.Equal(100, dropSteps[0]);   // the step that reaches 5.0 s
            Assert.Equal(PoopTuning.MaxPieces, dropSteps.Count);
            Assert.Equal(PoopTuning.MaxPieces, roam.Dropped);
            Assert.True(roam.Finished);
            double x = roam.X, y = roam.Y;
            for (int i = 0; i < 100; i++)
            {
                PoopDrop d;
                Assert.False(roam.Advance(0.05, out d));
            }
            Assert.Equal(x, roam.X);
            Assert.Equal(y, roam.Y);
        }

        [Fact]
        public void Roam_Drops_AreInsideTheArea_AndBehindHer()
        {
            var roam = Roam(11);
            roam.Begin(1920, 1040, 200, 250, 100, 100, 1);
            int drops = 0;
            for (int i = 0; i < 5000; i++)
            {
                PoopDrop d;
                if (!roam.Advance(0.05, out d)) continue;
                drops++;
                double half = PoopTuning.PieceSize / 2;
                Assert.InRange(d.X, half, 1920 - half);
                Assert.InRange(d.Y, half, 1040 - half);
                Assert.InRange(d.Variant, 0, 3);
                double centre = roam.X + 100;
                if (roam.Facing > 0) Assert.True(d.X < centre); else Assert.True(d.X > centre);
            }
            Assert.Equal(PoopTuning.MaxPieces, drops);
        }

        [Fact]
        public void Roam_BigStep_IsCapped()
        {
            var roam = Roam();
            roam.Begin(1920, 1040, 200, 250, 900, 400, 1);
            double x = roam.X, y = roam.Y;
            PoopDrop d;
            roam.Advance(60, out d);
            double moved = Math.Sqrt((roam.X - x) * (roam.X - x) + (roam.Y - y) * (roam.Y - y));
            Assert.True(moved <= PoopTuning.Speed * PoopTuning.MaxStepSeconds + 1e-9, "moved " + moved);
        }

        [Fact]
        public void Roam_BadInput_IsHarmless()
        {
            PoopDrop d;
            var early = Roam();
            Assert.False(early.Advance(0.05, out d));   // before Begin

            var roam = Roam();
            roam.Begin(1920, 1040, 200, 250, 900, 400, 1);
            double x = roam.X, y = roam.Y;
            Assert.False(roam.Advance(double.NaN, out d));
            Assert.False(roam.Advance(-1, out d));
            Assert.Equal(x, roam.X); Assert.Equal(y, roam.Y);

            var tiny = Roam();
            tiny.Begin(100, 100, 200, 250, 50, 50, 1);   // the area is smaller than the pet
            for (int i = 0; i < 1000; i++)
            {
                tiny.Advance(0.05, out d);
                Assert.Equal(0, tiny.X); Assert.Equal(0, tiny.Y);
            }

            var nan = Roam();
            nan.Begin(double.NaN, 1040, 200, double.NaN, double.NaN, -5, double.NaN);
            for (int i = 0; i < 100; i++)
            {
                nan.Advance(0.05, out d);
                Assert.False(double.IsNaN(nan.X)); Assert.False(double.IsNaN(nan.Y));
                Assert.True(nan.X >= 0 && nan.Y >= 0);
            }
        }

        [Fact]
        public void Roam_SpeedScale_Halves_TheDistance()
        {
            var full = Roam(3); full.Begin(1920, 1040, 200, 250, 860, 395, 1);
            var half = Roam(3); half.Begin(1920, 1040, 200, 250, 860, 395, 0.5);
            PoopDrop d;
            full.Advance(0.1, out d);
            half.Advance(0.1, out d);
            double df = Math.Sqrt((full.X - 860) * (full.X - 860) + (full.Y - 395) * (full.Y - 395));
            double dh = Math.Sqrt((half.X - 860) * (half.X - 860) + (half.Y - 395) * (half.Y - 395));
            Assert.Equal(PoopTuning.Speed * 0.1, df, 6);
            Assert.Equal(df / 2, dh, 6);
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

        static PetMenuBuilder PoopMenu(Settings s, List<string> calls, Action<bool>? idlePoop)
        {
            return new PetMenuBuilder(() => s, () => calls.Add("save"), () => new DateTime(2026, 11, 25, 10, 0, 0), new PetMenuActions
            {
                StartHelp = () => { },
                SizeChanged = size => calls.Add("size:" + size),
                SparklesChanged = on => calls.Add("sparkles:" + on),
                IdlePoopChanged = idlePoop,
            });
        }

        [Fact]
        public void Menu_IdlePoopItem_OnlyWithTheHook_AfterSparkles()
        {
            var texts = Texts(PoopMenu(new Settings(), new List<string>(), on => { })).ToList();
            int at = texts.IndexOf("Sparkles");
            Assert.Equal("Sparkle poop when idle", texts[at + 1]);
            Assert.Equal("How to start with Windows", texts[at + 2]);
            Assert.DoesNotContain("Sparkle poop when idle", Texts(PoopMenu(new Settings(), new List<string>(), null)));
            Assert.False(Flat(PoopMenu(new Settings(), new List<string>(), on => { }).Build()).First(i => i.Text == "Sparkle poop when idle").IsChecked);
        }

        [Fact]
        public void Menu_IdlePoop_TogglesSavesThenNotifies_AndAThrowingHookIsSwallowed()
        {
            var s = new Settings();
            var calls = new List<string>();
            var b = PoopMenu(s, calls, on => calls.Add("poop:" + on));
            b.Build().First(i => i.Text == "Sparkle poop when idle").OnClick();
            Assert.True(s.IdlePoop);
            Assert.True(b.Build().First(i => i.Text == "Sparkle poop when idle").IsChecked);
            b.Build().First(i => i.Text == "Sparkle poop when idle").OnClick();
            Assert.False(s.IdlePoop);
            Assert.Equal(new[] { "save", "poop:True", "save", "poop:False" }, calls.ToArray());

            var t = new Settings();
            var thrower = PoopMenu(t, new List<string>(), on => throw new InvalidOperationException("x"));
            thrower.Build().First(i => i.Text == "Sparkle poop when idle").OnClick();
            Assert.True(t.IdlePoop);
        }

        // ---------- the window wiring (read as text: the WPF code is not built by the test project) ----------

        static string Root()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            return dir!;
        }

        static string ReadApp(string file) => File.ReadAllText(Path.Combine(Root(), "src", "UnicornPet.App", file));

        /// <summary>The text of a file with every "#if !WORKSAFE" block (up to its #else or #endif) removed, and without // comments.</summary>
        static string WorkSafeText(string text)
        {
            var kept = new List<string>();
            int skipDepth = 0, depth = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("#if"))
                {
                    depth++;
                    if (skipDepth == 0 && line.Replace(" ", "") == "#if!WORKSAFE") skipDepth = depth;
                    continue;
                }
                if (line.StartsWith("#else") || line.StartsWith("#elif")) { if (skipDepth == depth) skipDepth = 0; continue; }
                if (line.StartsWith("#endif")) { if (skipDepth == depth) skipDepth = 0; depth--; continue; }
                if (skipDepth != 0 || line.StartsWith("//")) continue;
                kept.Add(raw);
            }
            return string.Join("\n", kept);
        }

        [Fact]
        public void OnlyTheTwoNewNativeCalls_AreDeclared_WithExactNames()
        {
            var src = ReadApp("PresenceNative.cs");
            Assert.Contains("[DllImport(\"user32.dll\")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);", src);
            Assert.Contains("[DllImport(\"shell32.dll\")] static extern int SHQueryUserNotificationState(out int state);", src);
            Assert.Equal(2, Regex.Matches(src, "DllImport").Count);
            Assert.DoesNotContain("EntryPoint", src);
            Assert.DoesNotContain("SetWindowsHookEx", src);
        }

        [Fact]
        public void NoOtherAppFile_GainsADllImport()
        {
            // the App files the work-safe exe compiles, as listed one by one in its project
            var csproj = File.ReadAllText(Path.Combine(Root(), "src", "UnicornPet.WorkSafe", "UnicornPet.WorkSafe.csproj"));
            var files = Regex.Matches(csproj, @"<Compile Include=""\.\.\\UnicornPet\.App\\([^""]+\.cs)""").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.Contains("PresenceNative.cs", files);
            var counts = files.ToDictionary(f => f, f => Regex.Matches(WorkSafeText(ReadApp(f)), "DllImport").Count);
            var expected = new Dictionary<string, int> { { "NoActivate.cs", 2 }, { "WindowsUserNameSource.cs", 1 }, { "PresenceNative.cs", 2 } };
            foreach (var kv in counts)
                Assert.True((expected.ContainsKey(kv.Key) ? expected[kv.Key] : 0) == kv.Value, kv.Key + " declares " + kv.Value + " native calls");
        }

        [Fact]
        public void IdleWiring_AsksNothing_WhileBothOptionsAreOff()
        {
            var src = ReadApp("PetWindow.Idle.cs");
            Assert.Contains("if (!Settings.IdlePoop && _party == null) return;", src);
        }
    }
}
