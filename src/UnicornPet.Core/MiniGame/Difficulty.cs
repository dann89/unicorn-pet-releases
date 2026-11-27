using System;

namespace UnicornPet.Core.MiniGame
{
    public enum ObstacleKind { Low, Tall, Wide, Overhead }

    /// <summary>How the run gets harder with time (seconds since the run started).</summary>
    public static class Difficulty
    {
        public static double Speed(double seconds)
        {
            return Math.Min(GameTuning.MaxSpeed, GameTuning.StartSpeed + GameTuning.SpeedPerSecond * Math.Max(0, seconds));
        }

        /// <summary>Straight line from MinGapStart to MinGapEnd over RampSeconds, then flat.</summary>
        public static double MinGapSeconds(double seconds)
        {
            if (seconds <= 0) return GameTuning.MinGapStart;
            if (seconds >= GameTuning.RampSeconds) return GameTuning.MinGapEnd;
            return GameTuning.MinGapStart + (GameTuning.MinGapEnd - GameTuning.MinGapStart) * seconds / GameTuning.RampSeconds;
        }

        /// <summary>How many of the ObstacleKind values (in order) may appear: 2, then 3, then 4.</summary>
        public static int KindCount(double seconds)
        {
            if (seconds >= GameTuning.OverheadFromSeconds) return 4;
            if (seconds >= GameTuning.WideFromSeconds) return 3;
            return 2;
        }

        /// <summary>The collision box of a kind with X = 0.</summary>
        public static Box Shape(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Tall: return new Box(0, 0, 20, 30);
                case ObstacleKind.Wide: return new Box(0, 0, 44, 20);
                case ObstacleKind.Overhead: return new Box(0, 28, 44, 82);
                default: return new Box(0, 0, 22, 22);
            }
        }

        /// <summary>Time in the air of a full jump.</summary>
        public static double AirSeconds { get { return 2 * GameTuning.JumpVelocity / GameTuning.Gravity; } }
    }
}
