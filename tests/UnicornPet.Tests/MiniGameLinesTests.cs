using System;
using System.Linq;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameLinesTests
    {
        static readonly GameMoment[] Moments = { GameMoment.Start, GameMoment.Milestone, GameMoment.Bump, GameMoment.NewBest };

        [Fact]
        public void EveryLine_IsNonEmpty_AtMost40Chars_PlainPrintableAscii_NoBraces()
        {
            foreach (var m in Moments)
                foreach (var line in GameLines.Pool(m, true))
                {
                    Assert.False(string.IsNullOrWhiteSpace(line));
                    Assert.True(line.Length <= GameLines.MaxLength, line);
                    Assert.True(line.All(c => c >= 0x20 && c < 0x7F), line);
                    Assert.DoesNotContain("{", line);
                    Assert.DoesNotContain("}", line);
                }
        }

        [Fact]
        public void EveryMoment_HasAtLeastTwoAlwaysLines_AndCheekyAddsSome()
        {
            foreach (var m in Moments)
            {
                Assert.True(GameLines.Pool(m, false).Count >= 2, m.ToString());
                Assert.True(GameLines.Pool(m, true).Count > GameLines.Pool(m, false).Count, m.ToString());
            }
        }

        [Fact]
        public void CheekyOff_NeverReturnsACheekyLine()
        {
            var r = new Lcg(21);
            foreach (var m in Moments)
            {
                var allowed = GameLines.Pool(m, false);
                string last = "";
                for (int i = 0; i < 500; i++)
                {
                    last = GameLines.Pick(m, false, r.Next, last);
                    Assert.Contains(last, allowed);
                }
            }
        }

        [Fact]
        public void CheekyOn_CanReturnACheekyLine()
        {
            var r = new Lcg(22);
            foreach (var m in Moments)
            {
                var plain = GameLines.Pool(m, false);
                bool seen = false;
                for (int i = 0; i < 500 && !seen; i++) seen = !plain.Contains(GameLines.Pick(m, true, r.Next, null!));
                Assert.True(seen, m.ToString());
            }
        }

        [Fact]
        public void Pick_AvoidsTheLastLine()
        {
            var r = new Lcg(23);
            foreach (var m in Moments)
            {
                string last = GameLines.Pick(m, true, r.Next, null!);
                for (int i = 0; i < 200; i++)
                {
                    var next = GameLines.Pick(m, true, r.Next, last);
                    Assert.NotEqual(last, next);
                    last = next;
                }
            }
        }

        [Fact]
        public void Pick_RngAtOne_OrOutOfRange_DoesNotThrow()
        {
            foreach (var v in new[] { 1.0, 0, -3, 9, double.NaN })
                foreach (var m in Moments)
                    Assert.Contains(GameLines.Pick(m, true, () => v, null!), GameLines.Pool(m, true));
        }
    }

    public class MiniGameLayoutTests
    {
        const double W = 422, H = 218;

        static GamePlacement At(double petL, double petT, double waL = 0, double waT = 0, double waW = 1920, double waH = 1040) =>
            GameWindowLayout.Compute(W, H, petL, petT, 200, 186, waL, waT, waW, waH);

        static void Inside(GamePlacement p, double waL, double waT, double waW, double waH)
        {
            Assert.True(p.Left >= waL && p.Left + W <= waL + waW, "left " + p.Left);
            Assert.True(p.Top >= waT && p.Top + H <= waT + waH, "top " + p.Top);
        }

        [Fact]
        public void Placement_AbovePet_Centred()
        {
            var p = At(800, 854);
            Assert.Equal(800 + 100 - W / 2, p.Left, 9);
            Assert.Equal(854 - GameWindowLayout.Gap - H, p.Top, 9);
        }

        [Fact]
        public void Placement_PetAtLeftEdge_RightEdge_StaysInside()
        {
            Inside(At(0, 854), 0, 0, 1920, 1040);
            Assert.Equal(0, At(0, 854).Left, 9);
            Inside(At(1720, 854), 0, 0, 1920, 1040);
            Assert.Equal(1920 - W, At(1720, 854).Left, 9);
        }

        [Fact]
        public void Placement_NoRoomAbove_StaysInside()
        {
            var p = At(800, 50);
            Inside(p, 0, 0, 1920, 1040);
            Assert.Equal(0, p.Top, 9);
        }

        [Fact]
        public void Placement_SecondMonitorWithNegativeLeft_StaysInside()
        {
            var p = At(-1920, 854, -1920, 0, 1920, 1040);
            Inside(p, -1920, 0, 1920, 1040);
            Assert.Equal(-1920, p.Left, 9);
        }

        [Fact]
        public void Placement_AreaSmallerThanWindow_AlignsTopLeft()
        {
            var p = At(100, 100, 10, 20, 300, 150);
            Assert.Equal(10, p.Left, 9);
            Assert.Equal(20, p.Top, 9);
        }
    }
}
