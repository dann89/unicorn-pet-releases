using System;
using System.Collections.Generic;
using System.Linq;

namespace UnicornPet.Core
{
    public enum SpeechKind { Scheduled, Random, Manual }

    public record Speech(string Text, SpeechKind Kind);

    public class Scheduler
    {
        public static readonly TimeSpan MaxLate = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan RandomSuppress = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan WorkStart = new TimeSpan(8, 0, 0);
        public static readonly TimeSpan WorkEnd = new TimeSpan(17, 15, 0);

        MessageBank _bank;
        readonly Func<DateTime> _now;
        readonly Random _rng;
        readonly MessagePicker _picker;
        readonly HashSet<int> _fired = new HashSet<int>();
        DateTime _firedDate = DateTime.MinValue;
        DateTime _nextRandomAt;
        string _lastScheduled;

        public Scheduler(MessageBank bank, Func<DateTime> now, Random rng)
        {
            _bank = bank;
            _now = now;
            _rng = rng;
            _picker = new MessagePicker(rng);
            _nextRandomAt = _now() + FirstGap();
        }

        bool _greeted;

        /// <summary>One greeting shortly after launch, when speech is allowed. First-ever run uses the welcome pool.</summary>
        public Speech Greet(Settings s, bool firstRun)
        {
            if (_greeted) return null;
            _greeted = true;
            var now = _now();
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) return null;
            if (s.Sleeping) return null;
            if (s.QuietUntil.HasValue && s.QuietUntil.Value > now) return null;

            IReadOnlyList<string> pool = firstRun ? _bank.Welcome : _bank.Hello;
            if (pool == null || pool.Count == 0) pool = firstRun ? MessageBank.Default.Welcome : MessageBank.Default.Hello;
            if (pool.Count == 0) return null;
            var text = _picker.Pick(pool, null);
            var earliest = now + RandomSuppress;
            if (_nextRandomAt < earliest) _nextRandomAt = earliest;
            return new Speech(MessagePicker.Substitute(text, _bank.Name), SpeechKind.Manual);
        }

        TimeSpan FirstGap() => TimeSpan.FromMinutes(2 + _rng.NextDouble() * 3);

        public string BankName => _bank.Name;

        public void UpdateName(string name) { _bank = _bank.WithName(name ?? ""); }

        DateTime? _seenUntil;   // the last tick, or MinValue once ExpectTicks was called and none has run yet

        /// <summary>
        /// Says that this scheduler is going to be ticked, so a list swapped in before the first tick swallows no scheduled line.
        /// </summary>
        public void ExpectTicks() { if (_seenUntil == null) _seenUntil = DateTime.MinValue; }

        /// <summary>
        /// Swaps in a new list while running (a custom file or web list arrived), keeping the current name. Slots of the new list that
        /// were already due today at the last tick count as fired, so no scheduled line repeats; a slot no tick has seen yet fires as
        /// usual. Without ExpectTicks or a tick, every slot whose time has passed counts as fired. null is ignored.
        /// </summary>
        public void ReplaceBank(MessageBank bank)
        {
            if (bank == null) return;
            var now = _now();
            _bank = bank.WithName(_bank.Name);
            _firedDate = now.Date;
            _fired.Clear();
            var seen = _seenUntil ?? now;
            if (seen.Date != now.Date) return;
            for (int i = 0; i < _bank.Scheduled.Count; i++)
                if (_bank.Scheduled[i].Time <= seen.TimeOfDay) _fired.Add(i);
        }

        TimeSpan RandomGap() => TimeSpan.FromMinutes(20 + _rng.NextDouble() * 20);

        public Speech SpeakNow(Settings s)
        {
            return new Speech(_picker.PickRandom(_bank, s != null && s.CheekyMode), SpeechKind.Manual);
        }

        public Speech Tick(Settings s)
        {
            var now = _now();
            _seenUntil = now;
            if (now.Date != _firedDate)
            {
                _firedDate = now.Date;
                _fired.Clear();
            }

            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) return null;
            if (s.Sleeping) return null;
            if (s.QuietUntil.HasValue && s.QuietUntil.Value > now) return null;

            var tod = now.TimeOfDay;

            if (s.ScheduledEnabled)
            {
                int latestDue = -1;
                for (int i = 0; i < _bank.Scheduled.Count; i++)
                {
                    if (_fired.Contains(i) || tod < _bank.Scheduled[i].Time) continue;
                    _fired.Add(i);
                    if (latestDue < 0 || _bank.Scheduled[i].Time >= _bank.Scheduled[latestDue].Time) latestDue = i;
                }
                if (latestDue >= 0 && tod - _bank.Scheduled[latestDue].Time < MaxLate)
                {
                    var slot = _bank.Scheduled[latestDue];
                    var text = _picker.Pick(slot.Pool, _lastScheduled);
                    _lastScheduled = text;
                    var earliest = now + TimeSpan.FromMinutes(20);
                    if (_nextRandomAt < earliest) _nextRandomAt = earliest;
                    return new Speech(MessagePicker.Substitute(text, _bank.Name), SpeechKind.Scheduled);
                }
            }

            if (now >= _nextRandomAt)
            {
                if (now - _nextRandomAt > MaxLate || tod < WorkStart || tod > WorkEnd)
                {
                    _nextRandomAt = now + RandomGap();
                    return null;
                }
                if (_bank.Scheduled.Any(x => (tod - x.Time).Duration() < RandomSuppress)) return null;
                _nextRandomAt = now + RandomGap();
                return new Speech(_picker.PickRandom(_bank, s.CheekyMode), SpeechKind.Random);
            }

            return null;
        }
    }
}

