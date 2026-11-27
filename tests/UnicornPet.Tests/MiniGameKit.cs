using UnicornPet.Core.MiniGame;

namespace UnicornPet.Tests
{
    /// <summary>A tiny seeded random source with identical results on every framework (System.Random differs between them).</summary>
    sealed class Lcg
    {
        uint _s;
        public Lcg(uint seed) { _s = seed; }
        public double Next() { _s = _s * 1664525u + 1013904223u; return (_s >> 8) / 16777216.0; }
    }

    /// <summary>
    /// A simple player for fairness tests: jump so the peak is over the next ground obstacle, duck just before a cloud.
    /// shiftSeconds moves the jump earlier (positive) or later (negative), to show the timing window is wide.
    /// </summary>
    static class ScriptedPlayer
    {
        public static GameInput Input(RunnerGame g, double shiftSeconds = 0)
        {
            Obstacle? next = null;
            foreach (var o in g.Obstacles)
                if (o.X + o.Box.W > GameTuning.RunnerX - 2 && (next == null || o.X < next.X)) next = o;
            var input = new GameInput();
            if (next == null) return input;
            if (next.Kind == ObstacleKind.Overhead)
            {
                input.DuckHeld = next.X - (GameTuning.RunnerX + GameTuning.DuckW) <= g.Speed * 0.12;
                return input;
            }
            double centre = next.X + next.Box.W / 2;
            input.JumpPressed = g.Runner.OnGround && centre - (GameTuning.RunnerX + GameTuning.StandW / 2) <= g.Speed * (Difficulty.AirSeconds / 2 + shiftSeconds);
            return input;
        }

        /// <summary>Plays up to the given number of steps (stops early on a bump); returns the events seen.</summary>
        public static int Play(RunnerGame g, int steps, double shiftSeconds = 0, GameEvents count = GameEvents.None)
        {
            int seen = 0;
            for (int i = 0; i < steps && g.Phase == GamePhase.Running; i++)
                if ((g.Step(Input(g, shiftSeconds)) & count) != 0 && count != GameEvents.None) seen++;
            return seen;
        }
    }
}
