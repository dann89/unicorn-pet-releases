using System;
using System.Globalization;
using System.Text;

namespace UnicornPet.Core
{
    /// <summary>Final display cap for a line after {name} has been filled in.</summary>
    public static class BubbleText
    {
        public const int MaxDisplayLength = 200;

        /// <summary>
        /// Shortens text to at most <paramref name="max"/> UTF-16 units, cutting only between whole characters as the reader sees them
        /// (never inside a surrogate pair, an emoji sequence or a letter with its accents) and ending with an ellipsis.
        /// </summary>
        public static string Fit(string text, int max = MaxDisplayLength)
        {
            if (text == null) return "";
            if (text.Length <= max) return text;
            var sb = new StringBuilder(max);
            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                var el = (string)e.Current;
                if (sb.Length + el.Length > max - 1) break;
                sb.Append(el);
            }
            return sb.ToString().TrimEnd() + "…";
        }
    }

    /// <summary>
    /// Whether the pet should be asleep right now. Weekend sleep is derived from the clock; only the explicit "Sleep now" choice is stored.
    /// </summary>
    public static class SleepPolicy
    {
        public static bool IsWeekend(DateTime now) { return now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday; }
        public static bool ShouldSleep(Settings s, DateTime now) { return (s != null && s.Sleeping) || IsWeekend(now); }
    }

    /// <summary>Re-evaluates <see cref="SleepPolicy"/> from the clock at most once a minute and reports only changes.</summary>
    public sealed class SleepTracker
    {
        readonly Func<DateTime> _now;
        DateTime _checkedAt = DateTime.MinValue;
        bool _current;

        public SleepTracker(Func<DateTime> now) { _now = now; }

        public bool Current { get { return _current; } }

        /// <summary>Returns the new state when it changed since the last call, otherwise null. Cheap to call often.</summary>
        public bool? Poll(Settings s, bool force = false)
        {
            var now = _now();
            if (!force && now >= _checkedAt && now - _checkedAt < TimeSpan.FromMinutes(1)) return null;
            _checkedAt = now;
            var want = SleepPolicy.ShouldSleep(s, now);
            if (want == _current) return null;
            _current = want;
            return want;
        }
    }

    /// <summary>Applies a settings change in memory first, then saves, and says plainly whether the save worked.</summary>
    public static class SettingsCommit
    {
        public const string SaveFailedMessage = "Couldn't save settings";

        /// <summary>Returns "" when saved, otherwise the plain-text message to show. The in-memory change is kept either way.</summary>
        public static string Run(Action applyInMemory, Func<bool> save)
        {
            applyInMemory?.Invoke();
            bool ok;
            try { ok = save != null && save(); }
            catch { ok = false; }
            return ok ? "" : SaveFailedMessage;
        }
    }
}
