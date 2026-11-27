using System.Collections.Generic;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameSpawnerTests
    {
        static readonly double[] Times = { 0, 5, 9.99, 10, 15, 19.99, 20, 45, 60, 300 };

        [Fact]
        public void Next_AlwaysDrawsFourNumbers()
        {
            var r = new Lcg(11);
            int calls = 0;
            var s = new Spawner(() => { calls++; return r.Next(); });
            for (int i = 0; i < 200; i++)
            {
                double t = Times[i % Times.Length];
                s.Next(t, Difficulty.Speed(t));
                Assert.Equal(4 * (i + 1), calls);
            }
        }

        [Fact]
        public void Before10s_OnlyLowAndTall()
        {
            var r = new Lcg(5);
            var s = new Spawner(r.Next);
            for (int i = 0; i < 400; i++)
            {
                var k = s.Next(9.99, 230).Obstacle.Kind;
                Assert.True(k == ObstacleKind.Low || k == ObstacleKind.Tall, k.ToString());
            }
        }

        [Fact]
        public void Before20s_NoOverhead()
        {
            var r = new Lcg(6);
            var s = new Spawner(r.Next);
            for (int i = 0; i < 400; i++) Assert.NotEqual(ObstacleKind.Overhead, s.Next(19.99, 260).Obstacle.Kind);
        }

        [Fact]
        public void After20s_AllFourKindsAppear()
        {
            var r = new Lcg(7);
            var s = new Spawner(r.Next);
            var seen = new HashSet<ObstacleKind>();
            for (int i = 0; i < 400; i++) seen.Add(s.Next(30, 290).Obstacle.Kind);
            Assert.Equal(4, seen.Count);
        }

        [Fact]
        public void Gap_WithinMinAndMinPlusSpread_TimesSpeed()
        {
            var r = new Lcg(8);
            var s = new Spawner(r.Next);
            for (int i = 0; i < 400; i++)
            {
                double t = Times[i % Times.Length], v = Difficulty.Speed(t);
                var sp = s.Next(t, v);
                double gap = sp.DistanceToNext - sp.Obstacle.Box.W;
                Assert.InRange(gap, Difficulty.MinGapSeconds(t) * v - 1e-9, (Difficulty.MinGapSeconds(t) + GameTuning.GapSpread) * v + 1e-9);
                Assert.Equal(GameTuning.SpawnX, sp.Obstacle.X);
            }
        }

        [Fact]
        public void Star_OverObstacle_NeverForOverhead()
        {
            var r = new Lcg(9);
            var s = new Spawner(r.Next);
            int over = 0;
            for (int i = 0; i < 2000; i++)
            {
                var sp = s.Next(40, 320);
                if (sp.Star == null) continue;
                var ob = sp.Obstacle.Box;
                bool floats = sp.Star.Y > GameTuning.GapStarY;
                if (sp.Obstacle.Kind == ObstacleKind.Overhead) Assert.False(floats);
                if (floats)
                {
                    over++;
                    Assert.Equal(ob.H + GameTuning.StarLift, sp.Star.Y);
                    Assert.False(sp.Star.Box.Intersects(ob));
                }
            }
            Assert.True(over > 0);
        }

        [Fact]
        public void GapStar_IsClearOfBothObstacles()
        {
            var r = new Lcg(10);
            var s = new Spawner(r.Next);
            int gapStars = 0;
            for (int i = 0; i < 2000; i++)
            {
                double t = Times[i % Times.Length];
                var sp = s.Next(t, Difficulty.Speed(t));
                if (sp.Star == null || sp.Star.Y != GameTuning.GapStarY) continue;
                gapStars++;
                var star = sp.Star.Box;
                Assert.False(star.Intersects(sp.Obstacle.Box));
                Assert.True(star.X > sp.Obstacle.X + sp.Obstacle.Box.W);
                Assert.True(star.X + star.W < GameTuning.SpawnX + sp.DistanceToNext);
            }
            Assert.True(gapStars > 0);
        }

        [Fact]
        public void StarRate_IsAbout45Percent()
        {
            var r = new Lcg(12);
            var s = new Spawner(r.Next);
            int stars = 0;
            for (int i = 0; i < 2000; i++) if (s.Next(30, 290).Star != null) stars++;
            Assert.InRange(stars, 800, 1000);
        }

        [Fact]
        public void RngOutOfRange_IsClamped()
        {
            foreach (var v in new[] { 1.0, -0.5, 7, double.NaN })
            {
                var s = new Spawner(() => v);
                for (int i = 0; i < 10; i++)
                {
                    double t = Times[i];
                    var sp = s.Next(t, Difficulty.Speed(t));
                    Assert.InRange((int)sp.Obstacle.Kind, 0, Difficulty.KindCount(t) - 1);
                    Assert.True(sp.DistanceToNext > 0);
                }
            }
        }
    }
}
