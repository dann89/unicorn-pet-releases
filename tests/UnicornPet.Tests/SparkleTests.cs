using System;
using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>The horn glitter: particle rules (deterministic, pooled, bounded) and the horn-tip table and mapping.</summary>
    public class SparkleTests
    {
        static ParticleSystem Make(uint seed, double x = 0.9, double y = 0.3, int facing = 1)
        {
            var rng = new Lcg(seed);
            var ps = new ParticleSystem(rng.Next);
            ps.SetBounds(1.0, 1.25);
            ps.SetEmitter(x, y, facing);
            return ps;
        }

        static List<Particle> Alive(ParticleSystem ps)
        {
            var list = new List<Particle>();
            for (int i = 0; i < ps.Capacity; i++) if (ps[i].Alive) list.Add(ps[i]);
            return list;
        }

        static string Snapshot(ParticleSystem ps)
        {
            var parts = new List<string>();
            for (int i = 0; i < ps.Capacity; i++)
            {
                var p = ps[i];
                parts.Add(p.Alive ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2:R}:{3:R}:{4:R}:{5:R}:{6:R}", p.Star, p.Color, p.X, p.Y, p.Age, p.Life, p.Size) : "-");
            }
            return string.Join("|", parts);
        }

        [Fact]
        public void SameSeed_SameParticles()
        {
            var a = Make(42);
            var b = Make(42);
            for (int i = 0; i < 400; i++)
            {
                a.Advance(0.033);
                b.Advance(0.033);
                Assert.Equal(Snapshot(a), Snapshot(b));
            }
            var c = Make(1);
            var d = Make(2);
            c.Advance(1.0);
            d.Advance(1.0);
            Assert.NotEqual(Snapshot(c), Snapshot(d));   // a different seed gives different glitter
        }

        [Fact]
        public void FixedStep_FrameRateDoesNotChangeTheResult()
        {
            // 0.1 s frames and 0.05 s frames reach the same state after the same total time (whole steps, remainder kept)
            var a = Make(7);
            var b = Make(7);
            for (int i = 0; i < 100; i++) a.Advance(0.1);
            for (int i = 0; i < 200; i++) b.Advance(0.05);
            Assert.Equal(Snapshot(a), Snapshot(b));
        }

        [Fact]
        public void NeverMoreThanTheCap_AndCountIsExact()
        {
            foreach (uint seed in new uint[] { 1, 2, 3, 99, 12345 })
            {
                var ps = Make(seed);
                for (int i = 0; i < 3000; i++)
                {
                    ps.Step();
                    int alive = Alive(ps).Count;
                    Assert.Equal(alive, ps.AliveCount);
                    Assert.True(alive <= ParticleSystem.MaxParticles);
                }
            }
            Assert.Equal(8, ParticleSystem.MaxParticles);
            Assert.True(ParticleSystem.StepSeconds >= 0.05 - 1e-12);   // at most 20 updates a second
        }

        [Fact]
        public void PoolIsReused_ManyBirthsFewSlots()
        {
            var ps = Make(5);
            int births = 0;
            for (int i = 0; i < 1200; i++)   // 60 s
            {
                ps.Step();
                births += Alive(ps).Count(p => p.Age == 0);
            }
            Assert.Equal(8, ps.Capacity);
            Assert.True(births > 40, "births: " + births);
        }

        [Fact]
        public void ParticlesStayInsideTheBounds_AndRise()
        {
            foreach (var facing in new[] { 1, -1 })
            {
                var ps = Make(11, facing == 1 ? 0.98 : 0.02, 0.05, facing);   // right at the edge and near the top
                for (int i = 0; i < 2000; i++)
                {
                    ps.Step();
                    foreach (var p in Alive(ps))
                    {
                        Assert.InRange(p.X, 0, 1.0);
                        Assert.InRange(p.Y, 0, 1.25);
                        Assert.True(p.Vy < 0);
                    }
                }
            }
        }

        [Fact]
        public void DriftFollowsTheWaySheFaces()
        {
            // a source that always says 1 picks the top of every range: the largest outward drift
            var right = new ParticleSystem(() => 1.0);
            right.SetBounds(1, 1.25);
            right.SetEmitter(0.5, 0.5, 1);
            var left = new ParticleSystem(() => 1.0);
            left.SetBounds(1, 1.25);
            left.SetEmitter(0.5, 0.5, -1);
            right.Advance(ParticleSystem.FirstBurstDelay + 0.01);
            left.Advance(ParticleSystem.FirstBurstDelay + 0.01);
            Assert.True(Alive(right).All(p => p.Vx > 0));
            Assert.True(Alive(left).All(p => p.Vx < 0));
            Assert.NotEmpty(Alive(right));
        }

        [Fact]
        public void Idle_CountsDown_ThenBursts()
        {
            var ps = Make(3);
            Assert.True(ps.Idle);
            Assert.Equal(ParticleSystem.FirstBurstDelay, ps.SecondsUntilNextBurst, 9);
            Assert.Equal(6, ps.Advance(0.3));
            Assert.True(ps.Idle);
            Assert.Equal(0, ps.AliveCount);
            Assert.Equal(ParticleSystem.FirstBurstDelay - 0.3, ps.SecondsUntilNextBurst, 9);
            Assert.True(ps.Advance(0.25) >= 1);
            Assert.False(ps.Idle);
            Assert.Equal(1, ps.AliveCount);
        }

        [Fact]
        public void BurstsEnd_AndTheSystemGoesIdleBetweenThem()
        {
            var ps = Make(8);
            int idleSteps = 0, busySteps = 0;
            for (int i = 0; i < 2400; i++)   // 120 s
            {
                ps.Step();
                if (ps.Idle) { idleSteps++; Assert.True(ps.SecondsUntilNextBurst > 0); }
                else busySteps++;
            }
            // nothing is drawn (and the caller's timer can sleep) for a real share of the time
            Assert.True(idleSteps > 2400 / 5, "idle " + idleSteps + " busy " + busySteps);
            Assert.True(busySteps > 2400 / 5, "idle " + idleSteps + " busy " + busySteps);
        }

        [Fact]
        public void NoEmitter_NothingAppears()
        {
            var ps = new ParticleSystem(new Lcg(4).Next);
            for (int i = 0; i < 400; i++) ps.Step();
            Assert.Equal(0, ps.AliveCount);
            var p2 = Make(4);
            p2.SetEmitter(double.NaN, 0.3, 1);
            for (int i = 0; i < 400; i++) p2.Step();
            Assert.Equal(0, p2.AliveCount);
        }

        [Fact]
        public void LongStall_IsCapped_NotReplayed()
        {
            var ps = Make(9);
            ps.Advance(1.0);   // into a burst
            Assert.Equal(ParticleSystem.MaxStepsPerAdvance, ps.Advance(30));
            Assert.Equal(0, ps.Advance(double.NaN));
            Assert.Equal(0, ps.Advance(-1));
            Assert.Equal(0, ps.Advance(double.PositiveInfinity));
        }

        [Fact]
        public void Reset_ClearsEverything()
        {
            var ps = Make(10);
            ps.Advance(1.0);
            Assert.True(ps.AliveCount > 0);
            ps.Reset();
            Assert.Equal(0, ps.AliveCount);
            Assert.True(ps.Idle);
            Assert.Empty(Alive(ps));
            Assert.Equal(ParticleSystem.FirstBurstDelay, ps.SecondsUntilNextBurst, 9);
        }

        [Fact]
        public void BadRandomSource_IsClamped()
        {
            foreach (var bad in new Func<double>[] { () => double.NaN, () => -5, () => 9 })
            {
                var ps = new ParticleSystem(bad);
                ps.SetBounds(1, 1.25);
                ps.SetEmitter(0.5, 0.6, 1);
                for (int i = 0; i < 400; i++)
                {
                    ps.Step();
                    foreach (var p in Alive(ps))
                    {
                        Assert.InRange(p.Color, 0, ParticleSystem.PaletteSize - 1);
                        Assert.True(p.Life >= ParticleSystem.LifeMin && p.Life <= ParticleSystem.LifeMax);
                    }
                }
            }
            Assert.Throws<ArgumentNullException>(() => new ParticleSystem(null!));
        }

        [Fact]
        public void Opacity_FadesInAndOut()
        {
            var p = new Particle { Alive = true, Life = 1.0 };
            Assert.Equal(0, ParticleSystem.Opacity(p), 9);
            p.Age = 0.1; Assert.Equal(0.5, ParticleSystem.Opacity(p), 9);
            p.Age = 0.3; Assert.Equal(1, ParticleSystem.Opacity(p), 9);
            p.Age = 0.75; Assert.Equal(0.5, ParticleSystem.Opacity(p), 9);
            p.Age = 1.0; Assert.Equal(0, ParticleSystem.Opacity(p), 9);
            p.Alive = false; p.Age = 0.3; Assert.Equal(0, ParticleSystem.Opacity(p), 9);
            for (double age = 0; age < 3; age += 0.01)
            {
                var star = new Particle { Alive = true, Star = true, Life = 3, Age = age, Phase = 1 };
                Assert.InRange(ParticleSystem.Twinkle(star), 0.6 - 1e-9, 1.0 + 1e-9);
            }
            Assert.Equal(1, ParticleSystem.Twinkle(new Particle { Alive = true, Star = false, Age = 0.4 }), 9);
        }

        // ---------- horn tip ----------

        [Fact]
        public void EveryAwakePose_HasAHornTip_SleepHasNone()
        {
            foreach (var pose in new[] { "idle", "talk", "happy_hop", "walk_01", "walk_02", "walk_03", "walk_04" })
            {
                double x, y;
                Assert.True(HornTips.TryGet(pose, out x, out y), pose);
                Assert.InRange(x, 0.8, 0.95);   // upper right: the art faces right
                Assert.InRange(y, 0.0, 0.1);
            }
            double sx, sy;
            Assert.False(HornTips.TryGet("sleep", out sx, out sy));
            Assert.False(HornTips.TryGet("nope", out sx, out sy));
            Assert.False(HornTips.TryGet(null!, out sx, out sy));
        }

        [Fact]
        public void PoseMapping_MatchesTheSpriteRules()
        {
            Assert.Equal("idle", PetPoses.For(PetState.Idle, 3));
            Assert.Equal("sleep", PetPoses.For(PetState.Sleep, 3));
            Assert.Equal("talk", PetPoses.For(PetState.Talk, 3));
            Assert.Equal("happy_hop", PetPoses.For(PetState.Hop, 3));
            Assert.Equal("walk_01", PetPoses.For(PetState.WalkRight, 0));
            Assert.Equal("walk_02", PetPoses.For(PetState.WalkLeft, 5));
            Assert.Equal("walk_04", PetPoses.For(PetState.WalkLeft, -1));
            foreach (var n in PetPoses.All) Assert.Contains(n, new[] { "idle", "sleep", "talk", "happy_hop", "walk_01", "walk_02", "walk_03", "walk_04" });
        }

        [Fact]
        public void ToWindow_LargeFacingRight()
        {
            double x, y;
            HornTips.ToWindow(0.9072, 0.0420, 200, 200, 250, 1, 1, 0, out x, out y);
            Assert.Equal(181.44, x, 6);
            Assert.Equal(58.4, y, 6);
        }

        [Fact]
        public void ToWindow_FacingLeft_IsMirrored()
        {
            double rx, ry, lx, ly;
            HornTips.ToWindow(0.9072, 0.0420, 200, 200, 250, 1, 1, 0, out rx, out ry);
            HornTips.ToWindow(0.9072, 0.0420, 200, 200, 250, -1, 1, 0, out lx, out ly);
            Assert.Equal(200 - rx, lx, 6);
            Assert.Equal(ry, ly, 6);
        }

        [Fact]
        public void ToWindow_HopAndBreathAndSize()
        {
            double x, y;
            HornTips.ToWindow(0.9072, 0.0420, 200, 200, 250, 1, 1, -36, out x, out y);
            Assert.Equal(58.4 - 36, y, 6);
            // the breath scales about the bottom centre
            HornTips.ToWindow(0.5, 0.0, 200, 200, 250, 1.015, 1.015, 0, out x, out y);
            Assert.Equal(100, x, 6);
            Assert.Equal(250 - 200 * 1.015, y, 6);
            // Small: everything halves
            HornTips.ToWindow(0.9072, 0.0420, 100, 100, 125, 1, 1, 0, out x, out y);
            Assert.Equal(90.72, x, 6);
            Assert.Equal(29.2, y, 6);
        }
    }
}
