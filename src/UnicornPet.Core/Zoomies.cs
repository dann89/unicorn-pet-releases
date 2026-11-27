using System;
using System.Collections.Generic;
using System.Linq;

namespace UnicornPet.Core
{
    /// <summary>One monitor's work area, in pixels.</summary>
    public struct ScreenLane
    {
        public int Left, Top, Right, Bottom;
        public ScreenLane(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
    }

    /// <summary>One straight run along the bottom of one monitor. From/To: 0 = the left end of that lane, 1 = the right end.</summary>
    public struct ZoomLeg
    {
        /// <summary>Index into the list given to <see cref="ZoomiesPlan.Build"/>.</summary>
        public int Screen;
        public double From, To;
    }

    /// <summary>
    /// Plans a zoomies run from the monitors' work areas: out to the right end of the right-most monitor, back to the left end of the
    /// left-most one, and home. Monitors are visited in order of their left edge (then top); gaps, sizes and scaling do not matter,
    /// because she changes monitor by a hidden hop.
    /// </summary>
    public static class ZoomiesPlan
    {
        public const int MaxScreens = 8;

        public static IReadOnlyList<ZoomLeg> Build(IReadOnlyList<ScreenLane> screens, int startScreen, double startFraction)
        {
            var legs = new List<ZoomLeg>();
            if (screens == null) return legs;
            var good = new List<int>();
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].Right > screens[i].Left && screens[i].Bottom > screens[i].Top) good.Add(i);
            if (good.Count == 0) return legs;
            var o = good.OrderBy(i => screens[i].Left).ThenBy(i => screens[i].Top).ThenBy(i => i).ToList();

            double start = double.IsNaN(startFraction) ? 0.5 : startFraction < 0 ? 0 : startFraction > 1 ? 1 : startFraction;
            int home = o.IndexOf(startScreen);
            if (home < 0) { home = 0; start = 0.5; }   // unknown or unusable start monitor: the first one, from the middle

            if (o.Count > MaxScreens)
            {
                int lo = Math.Max(0, Math.Min(home - MaxScreens / 2, o.Count - MaxScreens));
                o = o.GetRange(lo, MaxScreens);
                home -= lo;
            }
            int n = o.Count;

            Add(legs, o[home], start, 1);
            for (int i = home + 1; i < n; i++) Add(legs, o[i], 0, 1);
            for (int i = n - 1; i >= 0; i--) Add(legs, o[i], 1, 0);
            for (int i = 0; i < home; i++) Add(legs, o[i], 0, 1);
            Add(legs, o[home], 0, start);
            return legs;
        }

        static void Add(List<ZoomLeg> legs, int screen, double from, double to)
        {
            if (from != to) legs.Add(new ZoomLeg { Screen = screen, From = from, To = to });
        }
    }

    public enum ZoomStep { Running, Hop, Done }

    /// <summary>
    /// Runs the planned legs at 450 units a second. Moving to another monitor is reported as a Hop and nothing moves until
    /// <see cref="Arrived"/>; a run that is not over after 25 seconds (a hop that never lands, say) is Done anyway.
    /// </summary>
    public sealed class ZoomiesRun
    {
        public const double Speed = 450, MaxSeconds = 25, MaxStepSeconds = 0.1;

        readonly IReadOnlyList<ZoomLeg> _legs;
        int _leg;
        bool _waiting, _done;
        double _total;

        public ZoomiesRun(IReadOnlyList<ZoomLeg> legs)
        {
            _legs = legs ?? new ZoomLeg[0];
            if (_legs.Count == 0) { _done = true; return; }
            Screen = _legs[0].Screen;
            Fraction = _legs[0].From;
            Facing = _legs[0].To >= _legs[0].From ? 1 : -1;
        }

        public int Screen { get; private set; }
        public double Fraction { get; private set; }
        public int Facing { get; private set; } = 1;

        /// <param name="laneWidth">Units of the current lane (work width - pet width); below 1 counts as 1.</param>
        public ZoomStep Advance(double seconds, double laneWidth)
        {
            if (_done) return ZoomStep.Done;
            if (double.IsNaN(seconds) || seconds <= 0) return _waiting ? ZoomStep.Hop : ZoomStep.Running;
            double step = Math.Min(seconds, MaxStepSeconds);
            _total += step;
            if (_total >= MaxSeconds - 1e-6) { _done = true; return ZoomStep.Done; }
            if (_waiting) return ZoomStep.Hop;

            double lane = double.IsNaN(laneWidth) || laneWidth < 1 ? 1 : laneWidth;
            double left = Speed * step / lane;   // fraction of the lane still to run in this step
            while (true)
            {
                var leg = _legs[_leg];
                double gap = Math.Abs(leg.To - Fraction);
                if (left < gap)
                {
                    Fraction += leg.To > Fraction ? left : -left;
                    return ZoomStep.Running;
                }
                Fraction = leg.To;
                left -= gap;
                if (_leg + 1 >= _legs.Count) { _done = true; return ZoomStep.Done; }
                _leg++;
                var next = _legs[_leg];
                Facing = next.To >= next.From ? 1 : -1;
                if (next.Screen != Screen)
                {
                    Screen = next.Screen;
                    Fraction = next.From;
                    _waiting = true;
                    return ZoomStep.Hop;
                }
                Fraction = next.From;   // a turn on the same monitor: carry on in this same step
            }
        }

        /// <summary>The hop reported by <see cref="Advance"/> has been carried out.</summary>
        public void Arrived() { _waiting = false; }
    }

    /// <summary>"Zoomies now and then": due once every 45 to 90 minutes while enabled; postponed by 5 minutes when the moment is wrong.</summary>
    public sealed class ZoomiesTimer
    {
        public const double MinMinutes = 45, MaxMinutes = 90, PostponeMinutes = 5;

        readonly Func<DateTime> _now;
        readonly Func<double> _random01;
        DateTime _armedAt;
        TimeSpan _wait;

        public ZoomiesTimer(Func<DateTime> now, Func<double> random01)
        {
            _now = now; _random01 = random01;
            Arm(_now());
        }

        void Arm(DateTime at)
        {
            double r;
            try { r = _random01 == null ? 0.5 : _random01(); } catch { r = 0.5; }
            if (double.IsNaN(r) || r < 0) r = 0; else if (r > 1) r = 1;
            _armedAt = at;
            _wait = TimeSpan.FromMinutes(MinMinutes + r * (MaxMinutes - MinMinutes));
        }

        /// <summary>True once when the time has come, then re-armed. While disabled it is never true and stays armed for a full fresh wait.</summary>
        public bool Due(bool enabled)
        {
            var now = _now();
            if (!enabled || now < _armedAt) { Arm(now); return false; }   // disabled, or the clock went back
            if (now - _armedAt < _wait) return false;
            Arm(now);
            return true;
        }

        public void Postpone()
        {
            _armedAt = _now();
            _wait = TimeSpan.FromMinutes(PostponeMinutes);
        }
    }
}
