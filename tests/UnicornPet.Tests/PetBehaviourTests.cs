using System;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class PetBehaviourTests
    {
        static readonly TimeSpan Dt = TimeSpan.FromMilliseconds(50);

        static PetBehaviour Seeded(int seed)
        {
            var r = new Random(seed);
            return new PetBehaviour(() => r.NextDouble());
        }

        static void RunUntil(PetBehaviour p, Func<bool> done, double min, double max, int maxSteps = 20000)
        {
            for (int i = 0; i < maxSteps && !done(); i++) p.Step(Dt, min, max);
            Assert.True(done(), "condition never reached");
        }

        [Fact]
        public void Walking_NeverLeavesBounds()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var p = Seeded(seed);
                p.X = 50;
                for (int i = 0; i < 20000; i++)
                {
                    p.Step(Dt, 0, 300);
                    Assert.InRange(p.X, 0, 300);
                }
            }
        }

        [Fact]
        public void ReachingEdge_TurnsAround()
        {
            var p = new PetBehaviour(() => 0.9);
            p.X = 90;
            RunUntil(p, () => p.State == PetState.WalkRight, 0, 100);
            RunUntil(p, () => p.State == PetState.WalkLeft || p.State == PetState.Idle, 0, 100);
            Assert.True(p.X <= 100);
        }

        [Fact]
        public void Sleep_StaysSleepingUntilWake()
        {
            var p = Seeded(3);
            p.Sleep();
            var x = p.X;
            for (int i = 0; i < 2000; i++) p.Step(Dt, 0, 300);
            Assert.Equal(PetState.Sleep, p.State);
            Assert.Equal(x, p.X);
            p.Wake();
            Assert.NotEqual(PetState.Sleep, p.State);
        }

        [Fact]
        public void Hop_ReturnsToIdleAfterHopDuration()
        {
            var p = Seeded(4);
            p.Hop();
            Assert.Equal(PetState.Hop, p.State);
            for (int i = 0; i < 40; i++) p.Step(Dt, 0, 300);
            Assert.NotEqual(PetState.Hop, p.State);
        }

        [Fact]
        public void Talk_StopsWalking_ThenResumes()
        {
            var p = new PetBehaviour(() => 0.9);
            p.X = 10;
            RunUntil(p, () => p.State == PetState.WalkRight, 0, 300);
            p.Talk(TimeSpan.FromSeconds(3));
            Assert.Equal(PetState.Talk, p.State);
            var x = p.X;
            for (int i = 0; i < 40; i++) p.Step(Dt, 0, 300);
            Assert.Equal(PetState.Talk, p.State);
            Assert.Equal(x, p.X);
            RunUntil(p, () => p.State != PetState.Talk, 0, 300);
            RunUntil(p, () => p.State == PetState.WalkRight || p.State == PetState.WalkLeft, 0, 300);
        }

        [Fact]
        public void Talk_WhileSleeping_ReturnsToSleep()
        {
            var p = Seeded(5);
            p.Sleep();
            p.Talk(TimeSpan.FromSeconds(1));
            Assert.Equal(PetState.Talk, p.State);
            for (int i = 0; i < 60; i++) p.Step(Dt, 0, 300);
            Assert.Equal(PetState.Sleep, p.State);
        }

        [Fact]
        public void BoundsShrinkBelowX_ClampsX()
        {
            var p = Seeded(6);
            p.X = 1500;
            p.Step(Dt, 0, 800);
            Assert.InRange(p.X, 0, 800);
            p.X = -50;
            p.Step(Dt, 0, 800);
            Assert.InRange(p.X, 0, 800);
        }

        [Fact]
        public void LargeTimeStep_DoesNotTeleportPastBounds()
        {
            var p = new PetBehaviour(() => 0.9);
            RunUntil(p, () => p.State == PetState.WalkRight, 0, 300);
            p.Step(TimeSpan.FromHours(3), 0, 300);
            Assert.InRange(p.X, 0, 300);
        }

        // ---------- 2.1: Hold (the pet modes own her pose and position) ----------

        [Fact]
        public void Hold_FreezesStateAndX_UntilRelease()
        {
            var p = Seeded(3);
            p.X = 120;
            p.Hold(PetState.WalkRight);
            for (int i = 0; i < 200; i++)
            {
                p.Step(Dt, 0, 300);
                Assert.Equal(PetState.WalkRight, p.State);
                Assert.Equal(120, p.X);
                Assert.True(p.Held);
            }
            p.Release();
            Assert.False(p.Held);
            Assert.Equal(PetState.Idle, p.State);
        }

        [Fact]
        public void Hold_UnknownState_IsIdle()
        {
            var p = Seeded(4);
            p.Hold(PetState.Talk);
            Assert.Equal(PetState.Idle, p.State);
            Assert.True(p.Held);
            p.Hold(PetState.Hop);
            Assert.Equal(PetState.Idle, p.State);
        }

        [Fact]
        public void Hold_Sleep_ShowsSleep_ReleaseWakes()
        {
            var p = Seeded(5);
            p.Hold(PetState.Sleep);
            Assert.Equal(PetState.Sleep, p.State);
            p.Step(Dt, 0, 300);
            Assert.Equal(PetState.Sleep, p.State);
            p.Release();
            Assert.Equal(PetState.Idle, p.State);
        }

        [Fact]
        public void Sleep_Talk_Hop_EndTheHold()
        {
            var a = Seeded(6); a.Hold(PetState.WalkLeft); a.Sleep();
            Assert.False(a.Held); Assert.Equal(PetState.Sleep, a.State);
            var b = Seeded(6); b.Hold(PetState.WalkLeft); b.Talk(TimeSpan.FromSeconds(2));
            Assert.False(b.Held); Assert.Equal(PetState.Talk, b.State);
            var c = Seeded(6); c.Hold(PetState.WalkLeft); c.Hop();
            Assert.False(c.Held); Assert.Equal(PetState.Hop, c.State);
            c.Step(TimeSpan.FromMilliseconds(250), 0, 300);
            c.Step(TimeSpan.FromMilliseconds(250), 0, 300);
            c.Step(TimeSpan.FromMilliseconds(250), 0, 300);
            Assert.Equal(PetState.Idle, c.State);   // the hop ends as usual
        }

        [Fact]
        public void Release_WithoutHold_ChangesNothing()
        {
            var p = Seeded(7);
            p.X = 40;
            p.Talk(TimeSpan.FromSeconds(3));
            p.Release();
            Assert.False(p.Held);
            Assert.Equal(PetState.Talk, p.State);
            Assert.Equal(40, p.X);
        }
    }
}
