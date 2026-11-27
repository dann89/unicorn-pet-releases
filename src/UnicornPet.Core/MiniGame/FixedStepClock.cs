using System;

namespace UnicornPet.Core.MiniGame
{
    /// <summary>
    /// Turns the time since the last frame into a whole number of fixed 1/60 s steps. The part of a step left over is kept for the
    /// next frame. After a stall the count is capped and the backlog is thrown away, so the game never fast-forwards.
    /// </summary>
    public sealed class FixedStepClock
    {
        const double Slack = 1e-6;   // so a frame of exactly 1/60 s (rounded to 100 ns ticks) is never 0.99999 of a step
        readonly int _max;
        double _remainder;

        public FixedStepClock(int maxStepsPerFrame = 5) { _max = Math.Max(1, maxStepsPerFrame); }

        public int Advance(TimeSpan elapsed)
        {
            double secs = elapsed.TotalSeconds;
            if (!(secs > 0)) return 0;
            _remainder += secs;
            int steps = (int)((_remainder + Slack) / GameTuning.StepSeconds);
            if (steps >= _max)
            {
                _remainder = 0;
                return _max;
            }
            _remainder -= steps * GameTuning.StepSeconds;
            if (_remainder < 0) _remainder = 0;
            return steps;
        }

        public void Reset() { _remainder = 0; }
    }
}
