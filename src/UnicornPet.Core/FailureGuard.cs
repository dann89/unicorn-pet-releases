using System;

namespace UnicornPet.Core
{
    /// <summary>
    /// Runs a repeating action and gives up after a number of consecutive failures, so a bug in the animation can never turn into
    /// an endless stream of exceptions. A success resets the count. Once tripped, the action is never run again.
    /// </summary>
    public sealed class FailureGuard
    {
        readonly int _max;
        int _consecutive;

        public FailureGuard(int maxConsecutiveFailures = 5) { _max = Math.Max(1, maxConsecutiveFailures); }

        public bool Tripped { get; private set; }

        public int ConsecutiveFailures => _consecutive;

        public static readonly TimeSpan RecoveryGap = TimeSpan.FromSeconds(30);
        DateTime _lastRecovery = DateTime.MinValue;

        /// <summary>
        /// Lets a tripped guard run again after something that may have fixed the cause (a display change, a click, a menu choice).
        /// At most once per 30 seconds, so a persistent fault can never turn this into a loop. Returns true when the guard was reset.
        /// </summary>
        public bool TryRecover(DateTime now)
        {
            if (!Tripped) return false;
            if (now >= _lastRecovery && now - _lastRecovery < RecoveryGap) return false;
            _lastRecovery = now;
            _consecutive = 0;
            Tripped = false;
            return true;
        }

        /// <summary>Returns true when the action ran without throwing.</summary>
        public bool Run(Action action)
        {
            if (Tripped || action == null) return false;
            try
            {
                action();
                _consecutive = 0;
                return true;
            }
            catch
            {
                if (++_consecutive >= _max) Tripped = true;
                return false;
            }
        }
    }
}
