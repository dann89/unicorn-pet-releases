using System;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class BubbleLayoutTests
    {
        const double WaL = 0, WaT = 0, WaW = 1920, WaH = 1040;

        static BubblePlacement Place(double petL, double petT, double bw = 260, double bh = 80)
            => BubbleLayout.Compute(bw, bh, petL, petT, 200, 200, WaL, WaT, WaW, WaH);

        static void AssertInside(BubblePlacement p, double bw, double bh)
        {
            Assert.True(p.Left >= WaL - 1e-9, "left " + p.Left);
            Assert.True(p.Top >= WaT - 1e-9, "top " + p.Top);
            Assert.True(p.Left + bw <= WaL + WaW + 1e-9, "right " + (p.Left + bw));
            Assert.True(p.Top + bh <= WaT + WaH + 1e-9, "bottom " + (p.Top + bh));
        }

        [Fact]
        public void PetAtLeftEdge_BubbleStaysOnScreen()
        {
            var p = Place(0, 800);
            AssertInside(p, 260, 80);
            Assert.False(p.Beside);
        }

        [Fact]
        public void PetAtRightEdge_BubbleStaysOnScreen()
        {
            var p = Place(1720, 800);
            AssertInside(p, 260, 80);
            Assert.False(p.Beside);
        }

        [Fact]
        public void PetAtTop_NoRoomAbove_GoesBeside()
        {
            var p = Place(600, 0);
            Assert.True(p.Beside);
            AssertInside(p, 260, 80);
        }

        [Fact]
        public void PetAtTopRightCorner_BesideOnLeftSide()
        {
            var p = Place(1720, 0);
            Assert.True(p.Beside);
            Assert.True(p.Left + 260 <= 1720 + 1e-9);
            AssertInside(p, 260, 80);
        }

        [Fact]
        public void TailPointsAtPet_AndStaysInsideBubble()
        {
            var p = Place(0, 800);
            Assert.InRange(p.TailX, 16, 260 - 16);
            var q = Place(1720, 800);
            Assert.InRange(q.TailX, 16, 260 - 16);
            var m = Place(900, 800);
            Assert.Equal(900 + 100 - m.Left, m.TailX, 3);
        }

        [Fact]
        public void AnyPetPosition_BubbleInsideWorkArea()
        {
            for (double x = 0; x <= 1720; x += 37)
                for (double y = 0; y <= 840; y += 41)
                    AssertInside(Place(x, y), 260, 80);
        }

        [Fact]
        public void BubbleBiggerThanWorkArea_IsPinnedToOrigin()
        {
            var p = BubbleLayout.Compute(3000, 80, 0, 800, 200, 200, 0, 0, 1920, 1040);
            Assert.Equal(0, p.Left);
        }
    }
}
