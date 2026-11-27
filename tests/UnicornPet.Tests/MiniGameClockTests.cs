using System;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameClockTests
    {
        static int Run(FixedStepClock c, double hz, int frames, int[]? perFrame = null)
        {
            int total = 0;
            var dt = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / hz));
            for (int i = 0; i < frames; i++)
            {
                int n = c.Advance(dt);
                if (perFrame != null) perFrame[i] = n;
                total += n;
            }
            return total;
        }

        [Fact]
        public void SixtyHz_600Frames_Give600StepsPlusMinus1() => Assert.InRange(Run(new FixedStepClock(), 60, 600), 599, 601);

        [Fact]
        public void HundredFortyFourHz_144Frames_Give60StepsPlusMinus1() => Assert.InRange(Run(new FixedStepClock(), 144, 144), 59, 61);

        [Fact]
        public void ThirtyHz_GivesTwoStepsPerFrame()
        {
            var per = new int[60];
            Run(new FixedStepClock(), 30, 60, per);
            foreach (var n in per) Assert.Equal(2, n);
            Assert.InRange(Run(new FixedStepClock(), 30, 60), 119, 121);
        }

        [Fact]
        public void OneSecondStall_GivesFive_ThenBacklogIsGone()
        {
            var c = new FixedStepClock();
            Assert.Equal(5, c.Advance(TimeSpan.FromSeconds(1)));
            Assert.Equal(0, c.Advance(TimeSpan.Zero));
            Assert.True(c.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)) <= 1);
        }

        [Fact]
        public void Negative_IsZero()
        {
            var c = new FixedStepClock();
            Assert.Equal(0, c.Advance(TimeSpan.FromSeconds(-1)));
            Assert.Equal(0, c.Advance(TimeSpan.FromMilliseconds(10)));   // the negative did not eat into the remainder
            Assert.Equal(1, c.Advance(TimeSpan.FromMilliseconds(10)));
        }

        [Fact]
        public void Reset_ClearsRemainder()
        {
            var c = new FixedStepClock();
            Assert.Equal(0, c.Advance(TimeSpan.FromMilliseconds(15)));
            c.Reset();
            Assert.Equal(0, c.Advance(TimeSpan.FromMilliseconds(5)));
            Assert.Equal(1, c.Advance(TimeSpan.FromMilliseconds(12)));
        }
    }
}
