using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameDifficultyTests
    {
        [Fact]
        public void Speed_StartsAt200_ReachesCapAt60s_ThenFlat()
        {
            Assert.Equal(200, Difficulty.Speed(0), 9);
            Assert.Equal(290, Difficulty.Speed(30), 9);
            Assert.Equal(380, Difficulty.Speed(60), 9);
            Assert.Equal(380, Difficulty.Speed(500), 9);
        }

        [Fact]
        public void Speed_NegativeTime_IsStartSpeed() => Assert.Equal(200, Difficulty.Speed(-3), 9);

        [Fact]
        public void MinGap_135AtStart_095From60s()
        {
            Assert.Equal(1.35, Difficulty.MinGapSeconds(0), 9);
            Assert.Equal(1.15, Difficulty.MinGapSeconds(30), 9);
            Assert.Equal(0.95, Difficulty.MinGapSeconds(60), 9);
            Assert.Equal(0.95, Difficulty.MinGapSeconds(200), 9);
        }

        [Fact]
        public void KindCount_2_3_4_AtThresholds()
        {
            Assert.Equal(2, Difficulty.KindCount(0));
            Assert.Equal(2, Difficulty.KindCount(9.99));
            Assert.Equal(3, Difficulty.KindCount(10));
            Assert.Equal(3, Difficulty.KindCount(19.99));
            Assert.Equal(4, Difficulty.KindCount(20));
        }

        [Fact]
        public void Box_Overlap_True_TouchingEdge_False_Apart_False()
        {
            var a = new Box(0, 0, 10, 10);
            Assert.True(a.Intersects(new Box(9, 9, 5, 5)));
            Assert.False(a.Intersects(new Box(10, 0, 5, 5)));
            Assert.False(a.Intersects(new Box(0, 10, 5, 5)));
            Assert.False(a.Intersects(new Box(20, 20, 5, 5)));
        }

        [Fact]
        public void Overhead_ClearsDuck_HitsStand()
        {
            var cloud = Difficulty.Shape(ObstacleKind.Overhead);
            Assert.True(cloud.Y >= GameTuning.DuckH);
            Assert.True(cloud.Y < GameTuning.StandH);
        }

        [Fact]
        public void Overhead_TopAbovePeak()
        {
            var cloud = Difficulty.Shape(ObstacleKind.Overhead);
            Assert.True(cloud.Y + cloud.H > 80);
        }

        [Fact]
        public void Shapes_MatchTheSpec()
        {
            var low = Difficulty.Shape(ObstacleKind.Low);
            var tall = Difficulty.Shape(ObstacleKind.Tall);
            var wide = Difficulty.Shape(ObstacleKind.Wide);
            Assert.Equal(new[] { 22.0, 22, 0 }, new[] { low.W, low.H, low.Y });
            Assert.Equal(new[] { 20.0, 30, 0 }, new[] { tall.W, tall.H, tall.Y });
            Assert.Equal(new[] { 44.0, 20, 0 }, new[] { wide.W, wide.H, wide.Y });
        }

        [Fact]
        public void EveryGap_IsLongerThanAJump()
        {
            for (int s = 0; s <= 120; s++)
                Assert.True(Difficulty.MinGapSeconds(s) > Difficulty.AirSeconds + 0.2, "at " + s + " s");
        }

        [Fact]
        public void LookAhead_AtTopSpeed_IsAtLeast085s()
        {
            Assert.True((GameTuning.FieldWidth - GameTuning.RunnerX - GameTuning.StandW) / GameTuning.MaxSpeed >= 0.85);
        }
    }
}
