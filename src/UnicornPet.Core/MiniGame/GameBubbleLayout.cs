using System;
using System.Text;

namespace UnicornPet.Core.MiniGame
{
    /// <summary>A rectangle on the playfield in screen units: x to the right, y down from the top of the field.</summary>
    public record FieldRect(double Left, double Top, double Width, double Height)
    {
        public double Right { get { return Left + Width; } }
        public double Bottom { get { return Top + Height; } }

        /// <summary>True when the two overlap with some area. Touching edges do not count.</summary>
        public bool Overlaps(FieldRect o)
        {
            return Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom;
        }
    }

    /// <summary>
    /// Where the bubble line goes in the game. One fixed rule: the bubble sits in the top band of the field, just right of the
    /// runner's column, and above the card text. The runner never leaves its column (x is fixed; only its height changes), so the
    /// whole column, from the ground to above the top of the field at the jump peak, is kept clear; the card (score, best,
    /// "Space or click...") starts at <see cref="CardTop"/>, below the band. Text is wrapped to at most two lines.
    /// </summary>
    public static class GameBubbleLayout
    {
        public const int RunnerSize = 64;            // the runner picture is drawn as a square this size (GameArt uses this)
        public const double ArtGround = 973.0 / 1024.0;   // ground line of the character pictures, as a share of their height
        public const double ShakeX = 2;              // the bump shake moves the runner this far left and right
        public const double JumpPeakBound = 82;      // above any jump peak: v^2 / 2g = 470^2 / 2700 = 81.8 (the stepped body peaks lower)
        public const double CardTop = 46;            // y of the first line of the title / paused / game-over card
        public const double Margin = 4;              // gap kept from the field edges and the runner column
        public const double CardGap = 2;             // gap kept above the card (its first line has its own leading above the letters)

        public const double FontSize = 12;           // bubble text size
        public const double PadX = 8, PadY = 4;      // inside the bubble, around the text
        public const double MaxTextWidth = 184;      // so a bubble is at most 200 wide
        public const int MaxLines = 2;
        public const double LineHeight = 16;         // 12 px Segoe UI is about 15.96 per line
        public const double CharWidth = 7;           // conservative average width of a character at 12 px (real text is narrower)

        public static double GroundY { get { return GameTuning.FieldHeight - GameTuning.GroundStrip; } }

        /// <summary>Left edge of the runner picture when it is not shaking (GameCanvas draws it here).</summary>
        public static double RunnerLeft { get { return GameTuning.RunnerX + GameTuning.StandW / 2 - RunnerSize / 2.0; } }

        /// <summary>The runner picture at height <paramref name="runnerY"/> above the ground, widened by the bump shake.</summary>
        public static FieldRect RunnerBounds(double runnerY)
        {
            double bottom = GroundY - runnerY + RunnerSize * (1 - ArtGround);
            return new FieldRect(RunnerLeft - ShakeX, bottom - RunnerSize, RunnerSize + 2 * ShakeX, RunnerSize);
        }

        /// <summary>The runner's column: everything it covers standing, jumping to the peak, ducking, bumping or cheering.</summary>
        public static FieldRect RunnerColumn
        {
            get
            {
                var top = RunnerBounds(JumpPeakBound);
                var low = RunnerBounds(0);
                return new FieldRect(top.Left, top.Top, top.Width, low.Bottom - top.Top);
            }
        }

        /// <summary>The band the bubble must stay in: right of the runner column, above the card, inside the field.</summary>
        public static FieldRect Zone
        {
            get
            {
                double left = RunnerColumn.Right + Margin;
                return new FieldRect(left, Margin, GameTuning.FieldWidth - Margin - left, CardTop - CardGap - Margin);
            }
        }

        /// <summary>Most characters on one bubble line (by the conservative width).</summary>
        public static int CharsPerLine { get { return (int)Math.Floor(MaxTextWidth / CharWidth); } }

        /// <summary>
        /// Breaks a line at spaces so no part is longer than <see cref="CharsPerLine"/> characters (a single longer word stays
        /// whole). Parts are joined with '\n'.
        /// </summary>
        public static string Wrap(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int max = CharsPerLine;
            var sb = new StringBuilder();
            int lineLen = 0;
            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                if (lineLen > 0 && lineLen + 1 + word.Length > max)
                {
                    sb.Append('\n');
                    lineLen = 0;
                }
                else if (lineLen > 0)
                {
                    sb.Append(' ');
                    lineLen++;
                }
                sb.Append(word);
                lineLen += word.Length;
            }
            return sb.ToString();
        }

        /// <summary>The bubble for text of this measured size: at the zone's top-left, never larger than two lines of the cap.</summary>
        public static FieldRect Place(double textWidth, double textHeight)
        {
            double w = Math.Min(Math.Max(0, textWidth), MaxTextWidth) + 2 * PadX;
            double h = Math.Min(Math.Max(0, textHeight), MaxLines * LineHeight) + 2 * PadY;
            var zone = Zone;
            return new FieldRect(zone.Left, zone.Top, w, h);
        }

        /// <summary>The bubble for wrapped text, sized by the conservative character width (used by the tests).</summary>
        public static FieldRect Estimate(string wrapped)
        {
            var lines = (wrapped ?? "").Split('\n');
            int longest = 0;
            foreach (var l in lines) longest = Math.Max(longest, l.Length);
            return Place(longest * CharWidth, lines.Length * LineHeight);
        }
    }
}
