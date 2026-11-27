using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameRunnerBodyTests
    {
        // steps from the take-off step (counted as 1) until the step in which it lands
        static int Landing(RunnerBody b, int duckFrom = int.MaxValue)
        {
            Assert.True(b.Step(true, false));
            int n = 1;
            while (!b.OnGround)
            {
                n++;
                b.Step(false, n >= duckFrom);
                Assert.True(n < 200);
            }
            return n;
        }

        [Fact]
        public void Jump_PeaksBetween76And80()
        {
            var b = new RunnerBody();
            b.Step(true, false);
            double peak = b.Y;
            while (!b.OnGround) { b.Step(false, false); if (b.Y > peak) peak = b.Y; }
            Assert.InRange(peak, 76, 80);
        }

        [Fact]
        public void Jump_LandsAfter40To42Steps() => Assert.InRange(Landing(new RunnerBody()), 40, 42);

        [Fact]
        public void Jump_PressedInAir_IsIgnoredUnlessWithinBuffer()
        {
            int land = Landing(new RunnerBody());

            // pressed 10 steps before the landing step: forgotten by then
            var b = new RunnerBody();
            Assert.True(b.Step(true, false));
            for (int n = 2; n <= land + 10; n++)
                Assert.False(b.Step(n == land - 10, false), "step " + n);
            Assert.True(b.OnGround);

            // pressed 3 steps before the landing step: jumps on the step after landing
            b = new RunnerBody();
            Assert.True(b.Step(true, false));
            for (int n = 2; n <= land; n++)
                Assert.False(b.Step(n == land - 3, false), "step " + n);
            Assert.True(b.OnGround);
            Assert.True(b.Step(false, false));
            Assert.False(b.OnGround);
        }

        [Fact]
        public void JumpPressedEveryStep_JumpsAgainOnLanding()
        {
            // holding a key must not repeat jumps: that is the window's job (it ignores key repeat). Core jumps whenever told.
            var b = new RunnerBody();
            int jumps = 0;
            for (int i = 0; i < 100; i++) if (b.Step(true, false)) jumps++;
            Assert.True(jumps >= 2);
        }

        [Fact]
        public void Duck_OnGround_UsesLowBox()
        {
            var b = new RunnerBody();
            b.Step(false, true);
            Assert.True(b.Ducking);
            Assert.Equal(GameTuning.DuckW, b.Box.W);
            Assert.Equal(GameTuning.DuckH, b.Box.H);
            b.Step(false, false);
            Assert.False(b.Ducking);
            Assert.Equal(GameTuning.StandH, b.Box.H);
            Assert.Equal(GameTuning.RunnerX, b.Box.X);
        }

        [Fact]
        public void Duck_InAir_FallsFaster()
        {
            int plain = Landing(new RunnerBody());
            Assert.InRange(plain, 40, 42);

            var b = new RunnerBody();
            Assert.True(b.Step(true, false));
            int n = 1;
            while (!b.OnGround)
            {
                n++;
                b.Step(false, n >= 21);
                if (!b.OnGround) Assert.False(b.Ducking);
            }
            Assert.True(n <= plain - 6, "landed on step " + n);
            Assert.True(b.Ducking);
        }

        [Fact]
        public void JumpWhileDucking_Jumps()
        {
            var b = new RunnerBody();
            for (int i = 0; i < 5; i++) b.Step(false, true);
            Assert.True(b.Ducking);
            Assert.True(b.Step(true, true));
            Assert.False(b.OnGround);
            Assert.False(b.Ducking);
        }

        [Fact]
        public void Reset_ClearsEverything()
        {
            var b = new RunnerBody();
            b.Step(true, false);
            for (int i = 0; i < 5; i++) b.Step(false, false);
            b.Step(true, false); // remembered jump
            b.Reset();
            Assert.Equal(0, b.Y);
            Assert.Equal(0, b.VelocityY);
            Assert.True(b.OnGround);
            Assert.False(b.Ducking);
            Assert.False(b.Step(false, false)); // the remembered jump is gone
        }

        [Fact]
        public void NeverBelowGround()
        {
            var r = new Lcg(3);
            var b = new RunnerBody();
            for (int i = 0; i < 1000; i++)
            {
                b.Step(r.Next() < 0.1, r.Next() < 0.3);
                Assert.True(b.Y >= 0);
            }
        }
    }
}
