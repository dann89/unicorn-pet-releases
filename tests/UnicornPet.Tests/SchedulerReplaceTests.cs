using System;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    // A list swapped in while the pet runs must not swallow a scheduled line that no tick has seen yet.
    public class SchedulerReplaceTests
    {
        static readonly DateTime Mon = new DateTime(2026, 11, 23);
        class Clock { public DateTime Now; public Func<DateTime> F => () => Now; }

        static MessageBank Bank(string tag, params TimeSpan[] times) =>
            new MessageBank("", times.Select(t => new ScheduledSlot(tag + t.Hours, t, new[] { tag + " at " + t.ToString("hhmm") })).ToList(), new[] { "Hi {name}." }, new string[0]);

        static readonly MessageBank A = Bank("A", new TimeSpan(8, 0, 0), new TimeSpan(12, 45, 0));
        static readonly MessageBank B = Bank("B", new TimeSpan(9, 0, 0), new TimeSpan(12, 45, 0));

        [Fact]
        public void LoadBeforeTheFirstTick_SlotJustDue_IsStillSpoken()
        {
            var c = new Clock { Now = Mon.AddHours(9).AddMinutes(3) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ExpectTicks();
            s.ReplaceBank(B);
            c.Now = c.Now.AddSeconds(5);
            Assert.Equal("B at 0900", s.Tick(new Settings())?.Text);
        }

        [Fact]
        public void SwapBetweenASlotAndTheNextTick_SlotIsStillSpoken()
        {
            var c = new Clock { Now = Mon.AddHours(9).AddSeconds(-2) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ExpectTicks();
            s.Tick(new Settings());
            c.Now = Mon.AddHours(9).AddSeconds(1);
            s.ReplaceBank(B);
            c.Now = Mon.AddHours(9).AddSeconds(3);
            Assert.Equal("B at 0900", s.Tick(new Settings())?.Text);
        }

        [Fact]
        public void SlotAlreadySeenByATick_IsNotRepeatedAfterASwap()
        {
            var c = new Clock { Now = Mon.AddHours(9).AddSeconds(2) };
            var s = new Scheduler(B, c.F, new Random(1));
            s.ExpectTicks();
            Assert.Equal("B at 0900", s.Tick(new Settings())?.Text);
            c.Now = c.Now.AddSeconds(1);
            s.ReplaceBank(B);
            c.Now = c.Now.AddSeconds(5);
            Assert.Null(s.Tick(new Settings()));
        }

        [Fact]
        public void LastTickYesterday_TodaysSlotIsStillSpoken()
        {
            var c = new Clock { Now = Mon.AddHours(17) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ExpectTicks();
            s.Tick(new Settings());
            c.Now = Mon.AddDays(1).AddHours(9).AddMinutes(1);
            s.ReplaceBank(B);
            Assert.Equal("B at 0900", s.Tick(new Settings())?.Text);
        }

        [Fact]
        public void Window_TellsTheSchedulerThatTicksWillFollow()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            var src = File.ReadAllText(Path.Combine(dir!, "src", "UnicornPet.App", "PetWindow.xaml.cs"));
            Assert.Contains("_scheduler.ExpectTicks();", src);
        }
    }
}
