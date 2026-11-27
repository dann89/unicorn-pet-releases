using System;

namespace UnicornPet.Core.MiniGame
{
    public sealed class Obstacle
    {
        public ObstacleKind Kind;
        public double X;

        /// <summary>The kind's collision box moved to X.</summary>
        public Box Box
        {
            get
            {
                var b = Difficulty.Shape(Kind);
                b.X = X;
                return b;
            }
        }
    }

    public sealed class Star
    {
        public double X, Y;
        public Box Box { get { return new Box(X, Y, GameTuning.StarSize, GameTuning.StarSize); } }
    }

    public sealed class Spawn
    {
        public Obstacle Obstacle;
        public Star Star;            // may be null
        public double DistanceToNext;
    }

    /// <summary>Decides the next obstacle (and maybe a star) and how far away the one after it starts.</summary>
    public sealed class Spawner
    {
        readonly Func<double> _rng;

        public Spawner(Func<double> rng01) { _rng = rng01; }

        double R()
        {
            var v = _rng();
            if (!(v > 0)) return 0;   // also NaN
            return v > 1 ? 1 : v;
        }

        /// <summary>Always draws exactly four numbers, in this order, whatever it decides; that keeps runs repeatable.</summary>
        public Spawn Next(double seconds, double speed)
        {
            double r1 = R();   // kind
            double r2 = R();   // gap
            double r3 = R();   // star or not
            double r4 = R();   // star place

            int n = Difficulty.KindCount(seconds);
            var kind = (ObstacleKind)Math.Min(n - 1, (int)(r1 * n));
            var shape = Difficulty.Shape(kind);
            double gap = (Difficulty.MinGapSeconds(seconds) + r2 * GameTuning.GapSpread) * speed;

            var spawn = new Spawn
            {
                Obstacle = new Obstacle { Kind = kind, X = GameTuning.SpawnX },
                DistanceToNext = shape.W + gap,
            };
            if (r3 < GameTuning.StarChance)
            {
                if (kind != ObstacleKind.Overhead && r4 < 0.5)
                    spawn.Star = new Star { X = GameTuning.SpawnX + shape.W / 2 - GameTuning.StarSize / 2, Y = shape.H + GameTuning.StarLift };
                else
                    spawn.Star = new Star { X = GameTuning.SpawnX + shape.W + gap / 2 - GameTuning.StarSize / 2, Y = GameTuning.GapStarY };
            }
            return spawn;
        }
    }
}
