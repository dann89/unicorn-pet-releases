using System;
using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class SchedulerTests
    {
        class Clock { public DateTime Now; public Func<DateTime> F => () => Now; }

        static readonly DateTime Mon = new DateTime(2026, 11, 23);
        static readonly DateTime Sat = new DateTime(2026, 11, 28);

        static (Scheduler s, Clock c) Make(DateTime start, int seed = 1)
        {
            var c = new Clock { Now = start };
            return (new Scheduler(MessageBank.Default, c.F, new Random(seed)), c);
        }

        static Settings S() => new Settings();

        [Fact]
        public void Tick_Saturday_ReturnsNull()
        {
            var (s, c) = Make(Sat.AddHours(8));
            for (int i = 0; i < 600; i++) { Assert.Null(s.Tick(S())); c.Now = c.Now.AddMinutes(1); }
        }

        [Fact]
        public void Tick_At0800Weekday_ReturnsScheduledOnce()
        {
            var (s, c) = Make(Mon.AddHours(8));
            var first = s.Tick(S());
            Assert.NotNull(first);
            Assert.Equal(SpeechKind.Scheduled, first.Kind);
            Assert.Null(s.Tick(S()));
            c.Now = c.Now.AddSeconds(30);
            Assert.Null(s.Tick(S()));
        }

        [Fact]
        public void Tick_At1250_ForSlot1245_ReturnsScheduled()
        {
            var (s, c) = Make(Mon.AddHours(12).AddMinutes(50));
            var sp = s.Tick(S());
            Assert.NotNull(sp);
            Assert.Equal(SpeechKind.Scheduled, sp.Kind);
            Assert.Contains(sp.Text, MessageBank.Default.Scheduled.First(x => x.Id == "lunch-soon").Pool.Concat(MessageBank.Default.Scheduled.First(x => x.Id == "lunch-soon").Pool.Select(p => MessagePicker.Substitute(p, ""))));
        }

        [Fact]
        public void Tick_At1300_ForSlot1245_ReturnsNull()
        {
            var (s, c) = Make(Mon.AddHours(13));
            Assert.Null(s.Tick(S()));
            c.Now = c.Now.AddMinutes(1);
            Assert.Null(s.Tick(S()));
        }

        [Fact]
        public void Tick_AfterClockJumpOverThreeSlots_ReturnsAtMostOne()
        {
            var (s, c) = Make(Mon.AddHours(7).AddMinutes(30));
            Assert.Null(s.Tick(S()));
            c.Now = Mon.AddHours(17).AddMinutes(2);
            var results = new List<Speech>();
            for (int i = 0; i < 5; i++) { var sp = s.Tick(S()); if (sp != null) results.Add(sp); }
            Assert.True(results.Count <= 1);
            Assert.All(results, r => Assert.Equal(SpeechKind.Scheduled, r.Kind));
        }

        [Fact]
        public void Tick_ScheduledDisabled_ReturnsNull()
        {
            var (s, c) = Make(Mon.AddHours(8));
            Assert.Null(s.Tick(new Settings { ScheduledEnabled = false }));
        }

        [Fact]
        public void Tick_Sleeping_ReturnsNull()
        {
            var (s, c) = Make(Mon.AddHours(8));
            Assert.Null(s.Tick(new Settings { Sleeping = true }));
        }

        [Fact]
        public void Tick_QuietUntilFuture_ReturnsNull()
        {
            var (s, c) = Make(Mon.AddHours(8));
            Assert.Null(s.Tick(new Settings { QuietUntil = Mon.AddHours(9) }));
        }

        [Fact]
        public void Tick_QuietExpired_ScheduledStillShownIfWithinWindow()
        {
            var (s, c) = Make(Mon.AddHours(8));
            Assert.Null(s.Tick(new Settings { QuietUntil = Mon.AddHours(8).AddMinutes(3) }));
            c.Now = Mon.AddHours(8).AddMinutes(4);
            var sp = s.Tick(new Settings { QuietUntil = Mon.AddHours(8).AddMinutes(3) });
            Assert.NotNull(sp);
        }

        [Fact]
        public void Random_FiresBetween20And40MinAfterPrevious()
        {
            for (int seed = 0; seed < 25; seed++)
            {
                var (s, c) = Make(Mon.AddHours(9), seed);
                var fires = new List<DateTime>();
                var end = Mon.AddHours(12).AddMinutes(30);
                while (c.Now < end)
                {
                    var sp = s.Tick(S());
                    if (sp != null) { Assert.Equal(SpeechKind.Random, sp.Kind); fires.Add(c.Now); }
                    c.Now = c.Now.AddMinutes(1);
                }
                Assert.NotEmpty(fires);
                Assert.InRange((fires[0] - Mon.AddHours(9)).TotalMinutes, 2, 5.01);
                for (int i = 1; i < fires.Count; i++)
                    Assert.InRange((fires[i] - fires[i - 1]).TotalMinutes, 20, 41);
            }
        }

        [Fact]
        public void Random_NotWithin5MinOfScheduled()
        {
            var slots = MessageBank.Default.Scheduled.Select(x => x.Time).ToList();
            for (int seed = 0; seed < 30; seed++)
            {
                var (s, c) = Make(Mon.AddHours(7).AddMinutes(30), seed);
                var end = Mon.AddHours(17).AddMinutes(30);
                while (c.Now < end)
                {
                    var sp = s.Tick(S());
                    if (sp != null && sp.Kind == SpeechKind.Random)
                    {
                        var t = c.Now.TimeOfDay;
                        Assert.All(slots, slot => Assert.True(Math.Abs((t - slot).TotalMinutes) > 5 - 1e-9, "random at " + t + " near " + slot));
                    }
                    c.Now = c.Now.AddMinutes(1);
                }
            }
        }

        [Fact]
        public void Random_OutsideWorkingHours_ReturnsNull()
        {
            foreach (var t in new[] { new TimeSpan(7, 59, 0), new TimeSpan(17, 16, 0), new TimeSpan(22, 0, 0) })
            {
                for (int seed = 0; seed < 10; seed++)
                {
                    var (s, c) = Make(Mon + t.Add(TimeSpan.FromHours(-3)), seed);
                    c.Now = Mon + t;
                    c.Now = Mon + t;
                    Assert.Null(s.Tick(S()));
                }
            }
        }

        [Fact]
        public void SpeakNow_WorksEvenWhenSleepingOrQuiet_OnWeekend()
        {
            var (s, c) = Make(Sat.AddHours(10));
            var sp = s.SpeakNow(new Settings { Sleeping = true, QuietUntil = Sat.AddDays(2) });
            Assert.NotNull(sp);
            Assert.Equal(SpeechKind.Manual, sp.Kind);
            Assert.False(string.IsNullOrWhiteSpace(sp.Text));
        }

        [Fact]
        public void Dst_ChangeDay_StillFiresAtLocalTime()
        {
            var monday = new DateTime(2026, 10, 26);
            var (s, c) = Make(monday.AddHours(8));
            Assert.NotNull(s.Tick(S()));
            c.Now = monday.AddHours(14);
            Assert.NotNull(s.Tick(S()));
        }

        [Fact]
        public void NewDay_ResetsFiredSlots()
        {
            var (s, c) = Make(Mon.AddHours(8));
            Assert.NotNull(s.Tick(S()));
            c.Now = Mon.AddDays(1).AddHours(8);
            Assert.NotNull(s.Tick(S()));
        }
    }
}

