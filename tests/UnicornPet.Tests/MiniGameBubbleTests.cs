using System;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameBubbleTests
    {
        static readonly GameMoment[] Moments = { GameMoment.Start, GameMoment.Milestone, GameMoment.Bump, GameMoment.NewBest };

        static FieldRect Field { get { return new FieldRect(0, 0, GameTuning.FieldWidth, GameTuning.FieldHeight); } }

        // the card text starts here and spans the whole width (its text is centred, its length depends on the score)
        static FieldRect Card { get { return new FieldRect(0, GameBubbleLayout.CardTop, GameTuning.FieldWidth, GameTuning.FieldHeight - GameBubbleLayout.CardTop); } }

        static double SimulatedPeak()
        {
            var b = new RunnerBody();
            b.Step(true, false);
            double peak = b.Y;
            while (!b.OnGround) { b.Step(false, false); if (b.Y > peak) peak = b.Y; }
            return peak;
        }

        [Fact]
        public void EveryLine_WrapsToAtMostTwoLines_ThatFitTheWidthCap()
        {
            foreach (var m in Moments)
                foreach (var line in GameLines.Pool(m, true))
                {
                    var parts = GameBubbleLayout.Wrap(line).Split('\n');
                    Assert.True(parts.Length <= GameBubbleLayout.MaxLines, line);
                    foreach (var part in parts)
                        Assert.True(part.Length * GameBubbleLayout.CharWidth <= GameBubbleLayout.MaxTextWidth, line + " / " + part);
                }
        }

        [Fact]
        public void Wrap_KeepsEveryWordInOrder()
        {
            foreach (var m in Moments)
                foreach (var line in GameLines.Pool(m, true))
                    Assert.Equal(line, GameBubbleLayout.Wrap(line).Replace('\n', ' '));
        }

        [Fact]
        public void Wrap_BreaksAtSpaces()
        {
            Assert.Equal("Off we trot!", GameBubbleLayout.Wrap("Off we trot!"));
            Assert.Equal("Faster than the office\nprinter.", GameBubbleLayout.Wrap("Faster than the office printer."));
            Assert.Null(GameBubbleLayout.Wrap(null!));
        }

        [Fact]
        public void TheLargestBubble_FitsTheZone_AndTheZoneIsInsideTheField()
        {
            var zone = GameBubbleLayout.Zone;
            var big = GameBubbleLayout.Place(1000, 1000);   // the caps hold whatever the font measures
            Assert.True(big.Width <= 200);
            Assert.True(big.Left >= zone.Left && big.Top >= zone.Top && big.Right <= zone.Right && big.Bottom <= zone.Bottom);
            Assert.True(zone.Left >= 0 && zone.Top >= 0 && zone.Right <= Field.Right && zone.Bottom <= Field.Bottom);
        }

        [Fact]
        public void EveryLine_ClearOfTheRunner_AtEveryHeight_AndOfTheCard()
        {
            double peak = SimulatedPeak();
            Assert.True(peak <= GameBubbleLayout.JumpPeakBound);
            foreach (var m in Moments)
                foreach (var line in GameLines.Pool(m, true))
                {
                    var bubble = GameBubbleLayout.Estimate(GameBubbleLayout.Wrap(line));
                    Assert.False(bubble.Overlaps(Card), line);
                    Assert.False(bubble.Overlaps(GameBubbleLayout.RunnerColumn), line);
                    // standing, ducking, bumping and cheering are at height 0; every jump height up to past the peak
                    for (double y = 0; y <= GameBubbleLayout.JumpPeakBound; y += 0.5)
                        Assert.False(bubble.Overlaps(GameBubbleLayout.RunnerBounds(y)), line + " at " + y);
                }
        }

        [Fact]
        public void RunnerColumn_CoversThePictureAsGameCanvasDrawsIt()
        {
            // GameCanvas: left = RunnerX + StandW / 2 - size / 2, shaken by 2 either way; bottom on the ground line plus the art's margin
            double left = GameTuning.RunnerX + GameTuning.StandW / 2 - GameBubbleLayout.RunnerSize / 2.0;
            var col = GameBubbleLayout.RunnerColumn;
            Assert.True(col.Left <= left - 2 && col.Right >= left + GameBubbleLayout.RunnerSize + 2);
            Assert.True(col.Top < 0);   // at the peak the picture reaches above the field
            Assert.True(col.Bottom >= GameBubbleLayout.GroundY);
        }

        [Fact]
        public void Overlaps_TouchingEdgesDoNotCount()
        {
            var a = new FieldRect(0, 0, 10, 10);
            Assert.False(a.Overlaps(new FieldRect(10, 0, 5, 5)));
            Assert.False(a.Overlaps(new FieldRect(0, 10, 5, 5)));
            Assert.True(a.Overlaps(new FieldRect(9, 9, 5, 5)));
        }
    }
}
