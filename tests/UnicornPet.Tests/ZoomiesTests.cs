using System;
using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>Zoomies: the per-monitor leg planner, the run (speed, turns, hidden hops, the hard stop), the now-and-then timer and the menu item.</summary>
    public class ZoomiesTests
    {
        static readonly ScreenLane A = new ScreenLane(0, 0, 1920, 1040), B = new ScreenLane(1920, 0, 4480, 1400);

        static (int, double, double) L(int screen, double from, double to) => (screen, from, to);
        static (int, double, double)[] Legs(IReadOnlyList<ZoomLeg> legs) => legs.Select(l => (l.Screen, l.From, l.To)).ToArray();
        static (int, double, double)[] Plan(IReadOnlyList<ScreenLane> screens, int start, double fraction) => Legs(ZoomiesPlan.Build(screens, start, fraction));

        // ---------- planner ----------

        [Fact] public void OneScreen_RightEdge_LeftEdge_Home() =>
            Assert.Equal(new[] { L(0, .3, 1), L(0, 1, 0), L(0, 0, .3) }, Plan(new[] { A }, 0, 0.3));

        [Fact] public void TwoScreens_FromTheLeftOne() =>
            Assert.Equal(new[] { L(0, .5, 1), L(1, 0, 1), L(1, 1, 0), L(0, 1, 0), L(0, 0, .5) }, Plan(new[] { A, B }, 0, 0.5));

        [Fact] public void TwoScreens_FromTheRightOne() =>
            Assert.Equal(new[] { L(1, .5, 1), L(1, 1, 0), L(0, 1, 0), L(0, 0, 1), L(1, 0, .5) }, Plan(new[] { A, B }, 1, 0.5));

        [Fact] public void ListOrder_DoesNotMatter_OnlyLeftEdges() =>   // index 1 is A, index 0 is B
            Assert.Equal(new[] { L(1, .5, 1), L(0, 0, 1), L(0, 1, 0), L(1, 1, 0), L(1, 0, .5) }, Plan(new[] { B, A }, 1, 0.5));

        [Fact]
        public void NegativeCoordinates_SortFirst()
        {
            var left = new ScreenLane(-1920, 0, 0, 1040);
            Assert.Equal(new[] { L(0, .5, 1), L(0, 1, 0), L(1, 1, 0), L(1, 0, 1), L(0, 0, .5) }, Plan(new[] { A, left }, 0, 0.5));
        }

        [Fact]
        public void AGapBetweenScreens_ChangesNothing()
        {
            var far = new ScreenLane(5000, 0, 7560, 1400);
            Assert.Equal(Plan(new[] { A, B }, 0, 0.5), Plan(new[] { A, far }, 0, 0.5));
        }

        [Fact]
        public void StackedScreens_OrderByTop()
        {
            var lower = new ScreenLane(0, 1080, 1920, 2120);
            var upper = new ScreenLane(0, 0, 1920, 1040);
            // index 0 is the lower one: the upper one (index 1) comes first in the order
            Assert.Equal(new[] { L(0, .5, 1), L(0, 1, 0), L(1, 1, 0), L(1, 0, 1), L(0, 0, .5) }, Plan(new[] { lower, upper }, 0, 0.5));
        }

        [Fact]
        public void ThreeScreens_FromTheMiddle()
        {
            var left = new ScreenLane(-1920, 0, 0, 1040);
            var right = B;
            var middle = A;
            // indexes: 0 = right, 1 = left, 2 = middle
            Assert.Equal(new[] { L(2, .5, 1), L(0, 0, 1), L(0, 1, 0), L(2, 1, 0), L(1, 1, 0), L(1, 0, 1), L(2, 0, .5) },
                Plan(new[] { right, left, middle }, 2, 0.5));
        }

        [Fact] public void StartAtAnEnd_HasNoEmptyLegs() =>
            Assert.Equal(new[] { L(0, 1, 0), L(0, 0, 1) }, Plan(new[] { A }, 0, 1.0));

        [Fact]
        public void StartFraction_IsClamped_NaNIsMiddle()
        {
            Assert.Equal(new[] { L(0, 0, 1), L(0, 1, 0) }, Plan(new[] { A }, 0, -3));
            Assert.Equal(new[] { L(0, 1, 0), L(0, 0, 1) }, Plan(new[] { A }, 0, 7));
            Assert.Equal(new[] { L(0, .5, 1), L(0, 1, 0), L(0, 0, .5) }, Plan(new[] { A }, 0, double.NaN));
        }

        [Fact]
        public void UnknownStartScreen_UsesTheFirst_FromTheMiddle()
        {
            var expected = new[] { L(0, .5, 1), L(1, 0, 1), L(1, 1, 0), L(0, 1, 0), L(0, 0, .5) };
            Assert.Equal(expected, Plan(new[] { A, B }, 9, 0.2));
            Assert.Equal(expected, Plan(new[] { A, B }, -1, 0.2));
        }

        [Fact]
        public void EmptyOrNull_GivesNoLegs()
        {
            Assert.Empty(ZoomiesPlan.Build(new ScreenLane[0], 0, 0.5));
            Assert.Empty(ZoomiesPlan.Build(null!, 0, 0.5));
        }

        [Fact]
        public void DegenerateScreens_AreSkipped()
        {
            var flat = new ScreenLane(1920, 0, 1920, 1040);   // Right <= Left
            var thin = new ScreenLane(3000, 500, 4000, 500);  // Bottom <= Top
            var legs = ZoomiesPlan.Build(new[] { A, flat, thin }, 0, 0.5);
            Assert.All(legs, l => Assert.Equal(0, l.Screen));
            var fromFlat = ZoomiesPlan.Build(new[] { flat, A }, 0, 0.5);
            Assert.NotEmpty(fromFlat);
            Assert.All(fromFlat, l => Assert.Equal(1, l.Screen));
        }

        [Fact]
        public void MoreThanEight_KeepsEight_IncludingTheStart()
        {
            var screens = Enumerable.Range(0, 10).Select(i => new ScreenLane(i * 1920, 0, i * 1920 + 1920, 1040)).ToArray();
            var legs = ZoomiesPlan.Build(screens, 9, 0.5);
            Assert.Contains(legs, l => l.Screen == 9);
            Assert.Equal(8, legs.Select(l => l.Screen).Distinct().Count());
            Assert.Equal(9, legs[0].Screen);
        }

        // ---------- run ----------

        static ZoomiesRun TwoScreenRun() => new ZoomiesRun(ZoomiesPlan.Build(new[] { A, B }, 0, 0.5));

        [Fact]
        public void Run_OneScreen_EndsAtHome_InTheExpectedTime()
        {
            var run = new ZoomiesRun(ZoomiesPlan.Build(new[] { A }, 0, 0.5));
            double t = 0;
            ZoomStep step = ZoomStep.Running;
            for (int i = 0; i < 2000 && step != ZoomStep.Done; i++) { step = run.Advance(0.05, 1720); t += 0.05; }
            Assert.Equal(ZoomStep.Done, step);
            double expected = (0.5 + 1 + 0.5) * 1720 / ZoomiesRun.Speed;
            Assert.InRange(t, expected - 1e-9, expected + 0.05 + 1e-9);
            Assert.Equal(0.5, run.Fraction, 9);
        }

        [Fact]
        public void Run_Turns_FlipFacing()
        {
            var run = new ZoomiesRun(ZoomiesPlan.Build(new[] { A }, 0, 0.5));
            Assert.Equal(1, run.Facing);
            bool sawLeft = false;
            for (int i = 0; i < 200; i++)
            {
                run.Advance(0.05, 1720);
                if (run.Facing < 0) { sawLeft = true; break; }
            }
            Assert.True(sawLeft);
            for (int i = 0; i < 200 && run.Facing < 0; i++) run.Advance(0.05, 1720);
            Assert.Equal(1, run.Facing);
        }

        [Fact]
        public void Run_ReportsAHop_ThenWaitsForArrived()
        {
            var run = TwoScreenRun();
            ZoomStep step = ZoomStep.Running;
            for (int i = 0; i < 1000 && step == ZoomStep.Running; i++) step = run.Advance(0.05, 1720);
            Assert.Equal(ZoomStep.Hop, step);
            Assert.Equal(1, run.Screen);
            Assert.Equal(0, run.Fraction);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(ZoomStep.Hop, run.Advance(0.05, 2360));
                Assert.Equal(0, run.Fraction);
                Assert.Equal(1, run.Screen);
            }
            run.Arrived();
            Assert.Equal(ZoomStep.Running, run.Advance(0.05, 2360));
            Assert.True(run.Fraction > 0);
        }

        [Fact]
        public void Run_HopNeverArrives_EndsByTime()
        {
            var run = TwoScreenRun();
            int calls = 0;
            ZoomStep step = ZoomStep.Running;
            while (step != ZoomStep.Done && calls < 1000) { step = run.Advance(0.05, 1720); calls++; }
            Assert.Equal(ZoomStep.Done, step);
            Assert.InRange(calls, 499, 501);   // 25 s of 0.05 s steps
            Assert.Equal(ZoomStep.Done, run.Advance(0.05, 1720));
        }

        [Fact]
        public void Run_BigStep_IsCapped_AndTinyLane_IsSafe()
        {
            var run = new ZoomiesRun(ZoomiesPlan.Build(new[] { A }, 0, 0.5));
            run.Advance(10, 1720);
            Assert.True((run.Fraction - 0.5) * 1720 <= ZoomiesRun.Speed * ZoomiesRun.MaxStepSeconds + 1e-6);
            foreach (var lane in new[] { 0.0, -5.0, double.NaN })
            {
                var r = new ZoomiesRun(ZoomiesPlan.Build(new[] { A }, 0, 0.5));
                ZoomStep step = ZoomStep.Running;
                for (int i = 0; i < 1000 && step != ZoomStep.Done; i++) step = r.Advance(0.05, lane);
                Assert.Equal(ZoomStep.Done, step);
                Assert.False(double.IsNaN(r.Fraction));
            }
        }

        [Fact]
        public void Run_NoLegs_IsDoneAtOnce()
        {
            Assert.Equal(ZoomStep.Done, new ZoomiesRun(new ZoomLeg[0]).Advance(0.05, 1720));
            Assert.Equal(ZoomStep.Done, new ZoomiesRun(null!).Advance(0.05, 1720));
        }

        [Fact]
        public void Run_BadSeconds_AreIgnored()
        {
            var run = new ZoomiesRun(ZoomiesPlan.Build(new[] { A }, 0, 0.5));
            Assert.Equal(ZoomStep.Running, run.Advance(double.NaN, 1720));
            Assert.Equal(ZoomStep.Running, run.Advance(-1, 1720));
            Assert.Equal(0.5, run.Fraction);
        }

        // ---------- now-and-then timer ----------

        class Clock { public DateTime Now = new DateTime(2026, 11, 25, 9, 0, 0); public Func<DateTime> F => () => Now; }

        [Fact]
        public void Timer_FirstDue_Between45And90Minutes()
        {
            var c = new Clock();
            var start = c.Now;
            var t0 = new ZoomiesTimer(c.F, () => 0);
            c.Now = start.AddMinutes(45).AddSeconds(-1);
            Assert.False(t0.Due(true));
            c.Now = start.AddMinutes(45);
            Assert.True(t0.Due(true));

            c.Now = start;
            var t1 = new ZoomiesTimer(c.F, () => 1);
            c.Now = start.AddMinutes(90).AddSeconds(-1);
            Assert.False(t1.Due(true));
            c.Now = start.AddMinutes(90);
            Assert.True(t1.Due(true));
        }

        [Fact]
        public void Timer_IsTrueOnce_ThenReArms()
        {
            var c = new Clock();
            var t = new ZoomiesTimer(c.F, () => 0);
            c.Now = c.Now.AddMinutes(45);
            Assert.True(t.Due(true));
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(44);
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(1);
            Assert.True(t.Due(true));
        }

        [Fact]
        public void Timer_Disabled_NeverFires_AndEnablingStartsAFreshWait()
        {
            var c = new Clock();
            var t = new ZoomiesTimer(c.F, () => 0);
            for (int i = 0; i < 120; i++) { c.Now = c.Now.AddMinutes(5); Assert.False(t.Due(false)); }   // 10 hours
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(44);
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(1);
            Assert.True(t.Due(true));
        }

        [Fact]
        public void Timer_Postpone_IsFiveMinutes()
        {
            var c = new Clock();
            var t = new ZoomiesTimer(c.F, () => 0);
            c.Now = c.Now.AddMinutes(45);
            Assert.True(t.Due(true));
            t.Postpone();
            c.Now = c.Now.AddMinutes(4);
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(1);
            Assert.True(t.Due(true));
        }

        [Fact]
        public void Timer_ClockGoingBack_ReArms()
        {
            var c = new Clock();
            var t = new ZoomiesTimer(c.F, () => 0);
            c.Now = c.Now.AddMinutes(-120);
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(44);
            Assert.False(t.Due(true));
            c.Now = c.Now.AddMinutes(1);
            Assert.True(t.Due(true));
        }

        // ---------- menu ----------

        static List<string> Texts(PetMenuBuilder b)
        {
            var l = new List<string>();
            void Walk(IEnumerable<MenuItemModel> items) { foreach (var i in items) { if (!i.IsSeparator) l.Add(i.Text); if (i.Children != null) Walk(i.Children); } }
            Walk(b.Build());
            return l;
        }

        static readonly DateTime Wednesday = new DateTime(2026, 11, 25, 10, 0, 0), Saturday = new DateTime(2026, 11, 28, 10, 0, 0);

        static PetMenuBuilder ZoomMenu(Settings s, List<string> calls, bool game, Action? zoomies, DateTime? now = null)
        {
            var when = now ?? Wednesday;
            return new PetMenuBuilder(() => s, () => calls.Add("save"), () => when, new PetMenuActions
            {
                StartHelp = () => { },
                MiniGame = game ? new Action(() => calls.Add("game")) : null,
                Zoomies = zoomies,
            });
        }

        [Fact]
        public void Menu_Zoomies_OnlyWithTheHook_AfterMiniGame()
        {
            var calls = new List<string>();
            var t = Texts(ZoomMenu(new Settings(), calls, true, () => { }));
            int at = t.IndexOf("Make it talk now");
            Assert.Equal(new[] { "Make it talk now", "Mini game", "Zoomies!", "Cheeky mode" }, t.Skip(at).Take(4));
            var noGame = Texts(ZoomMenu(new Settings(), calls, false, () => { }));
            Assert.Equal("Zoomies!", noGame[noGame.IndexOf("Make it talk now") + 1]);
            Assert.DoesNotContain("Zoomies!", Texts(ZoomMenu(new Settings(), calls, true, null)));
        }

        [Fact]
        public void Menu_Zoomies_IsHidden_WhileAsleep_AtTheWeekend_AndInQuiet()
        {
            var calls = new List<string>();
            Assert.DoesNotContain("Zoomies!", Texts(ZoomMenu(new Settings { Sleeping = true }, calls, true, () => { })));
            Assert.DoesNotContain("Zoomies!", Texts(ZoomMenu(new Settings(), calls, true, () => { }, Saturday)));
            Assert.DoesNotContain("Zoomies!", Texts(ZoomMenu(new Settings { QuietUntil = Wednesday.AddHours(1) }, calls, true, () => { })));
            Assert.Contains("Zoomies!", Texts(ZoomMenu(new Settings { QuietUntil = Wednesday.AddHours(-1) }, calls, true, () => { })));
        }

        [Fact]
        public void Menu_Zoomies_Click_CallsTheHook_SavesNothing_AndAThrowIsSwallowed()
        {
            var calls = new List<string>();
            ZoomMenu(new Settings(), calls, false, () => calls.Add("zoom")).Build().First(i => i.Text == "Zoomies!").OnClick();
            Assert.Equal(new[] { "zoom" }, calls.ToArray());
            ZoomMenu(new Settings(), calls, false, () => throw new InvalidOperationException("x")).Build().First(i => i.Text == "Zoomies!").OnClick();
            Assert.Equal(new[] { "zoom" }, calls.ToArray());
        }

        [Fact]
        public void Menu_AllHooks_HasTwentyItemsInTheOrderOfS3()
        {
            var b = new PetMenuBuilder(() => new Settings(), () => { }, () => Wednesday, new PetMenuActions
            {
                TalkNow = () => { }, SleepChanged = on => { }, StartHelp = () => { }, OpenSettings = () => { }, About = () => { }, Quit = () => { },
                MiniGame = () => { }, Zoomies = () => { }, SizeChanged = size => { }, SparklesChanged = on => { }, IdlePoopChanged = on => { },
            });
            Assert.Equal(new[] { "Sleep now", "Quiet", "1 hour", "Until tomorrow", "Off", "Make it talk now", "Mini game", "Zoomies!", "Cheeky mode",
                "Scheduled messages", "Size", "Small", "Medium", "Large", "Sparkles", "Sparkle poop when idle", "How to start with Windows",
                "Settings…", "About", "Quit" }, Texts(b).ToArray());
        }
    }
}
