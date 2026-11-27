using System;

namespace UnicornPet.Core
{
    /// <summary>
    /// Pairs clicks into double-clicks by time and screen position. The window's own click count is not used: the pet walks, so the
    /// second click lands on a different part of her window even when the pointer has not moved.
    /// </summary>
    public sealed class DoubleClick
    {
        readonly TimeSpan _within;
        readonly double _slack;
        TimeSpan? _first;
        double _x, _y;

        public DoubleClick(TimeSpan within, double slack) { _within = within; _slack = slack; }

        /// <summary>True when this click completes a double-click; the next one starts afresh.</summary>
        public bool Click(TimeSpan at, double x, double y)
        {
            if (_first.HasValue && at - _first.Value <= _within && Math.Abs(x - _x) <= _slack && Math.Abs(y - _y) <= _slack)
            {
                _first = null;
                return true;
            }
            _first = at; _x = x; _y = y;
            return false;
        }

        /// <summary>Forgets a waiting first click.</summary>
        public void Reset() { _first = null; }
    }
}
