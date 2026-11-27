using System;

namespace UnicornPet.Core
{
    /// <summary>Idle time from the system's millisecond tick and the tick of the last input (both wrap every 49.7 days).</summary>
    public static class IdleMath
    {
        /// <summary>
        /// Milliseconds since the last input. A difference above 0x7FFFFFFF means the last input is a little "ahead" of now (the two
        /// values are read at slightly different moments), so it counts as 0: never as 49 days, which would start a party while typing.
        /// </summary>
        public static uint IdleMs(uint nowTick, uint lastInputTick)
        {
            var d = unchecked(nowTick - lastInputTick);
            return d > 0x7FFFFFFFu ? 0u : d;
        }
    }

    /// <summary>The system tick and the tick of the last keyboard or mouse input. Only times are read, never what was typed.</summary>
    public interface IIdleSource
    {
        uint NowTick { get; }
        bool TryGetLastInputTick(out uint tick);
    }

    /// <summary>Whether the shell says notifications may be shown now (no full-screen app, presentation or quiet time). False on any failure.</summary>
    public interface IPresenceSource
    {
        bool AcceptsNotifications();
    }

    /// <summary>The answer of SHQueryUserNotificationState.</summary>
    public static class PresenceCodes
    {
        /// <summary>QUNS_ACCEPTS_NOTIFICATIONS: the only state that allows a party or unprompted zoomies.</summary>
        public const int AcceptsNotifications = 5;

        public static bool Accepts(int hresult, int state) { return hresult >= 0 && state == AcceptsNotifications; }
    }

    public enum PartyChange { None, Start, Stop }

    /// <summary>
    /// Decides when the sparkle poop party starts and stops. Waiting: starts after 3 idle minutes when enabled, not blocked and the shell
    /// accepts. Active: any input stops it at once; becoming busy stops it within 5 seconds; disabled or blocked stops it. After a stop
    /// that was not caused by input (Spent) no new party starts until there has been input. With the option off and no party running,
    /// neither source is asked anything.
    /// </summary>
    public sealed class IdleParty
    {
        public const uint IdleDelayMs = 180000, PresenceEveryMs = 5000;

        enum Phase { Waiting, Active, Spent }

        readonly IIdleSource _idle;
        readonly IPresenceSource _presence;
        Phase _phase = Phase.Waiting;
        uint _startTick;      // last-input tick when the party started
        uint _askedAt;        // system tick of the last presence check during the party
        uint _spentTick;      // last-input tick when the party was stopped without input

        public IdleParty(IIdleSource idle, IPresenceSource presence) { _idle = idle; _presence = presence; }

        public bool Active => _phase == Phase.Active;

        public PartyChange Poll(bool enabled, bool blocked)
        {
            if (!enabled && _phase != Phase.Active) return PartyChange.None;

            uint last;
            if (!TryRead(out last))
            {
                // a failed read counts as input just now
                if (_phase == Phase.Active) { _phase = Phase.Waiting; return PartyChange.Stop; }
                return PartyChange.None;
            }

            if (_phase == Phase.Active)
            {
                if (last != _startTick) { _phase = Phase.Waiting; return PartyChange.Stop; }
                if (!enabled || blocked) { Spend(last); return PartyChange.Stop; }
                uint now = _idle.NowTick;
                if (unchecked(now - _askedAt) >= PresenceEveryMs)
                {
                    _askedAt = now;
                    if (!Ask()) { Spend(last); return PartyChange.Stop; }
                }
                return PartyChange.None;
            }

            if (_phase == Phase.Spent)
            {
                if (last == _spentTick) return PartyChange.None;
                _phase = Phase.Waiting;   // there was input: carry on waiting in this same call
            }

            if (!enabled || blocked) return PartyChange.None;
            uint nowTick = _idle.NowTick;
            if (IdleMath.IdleMs(nowTick, last) < IdleDelayMs) return PartyChange.None;
            if (!Ask()) return PartyChange.None;
            _startTick = last;
            _askedAt = nowTick;
            _phase = Phase.Active;
            return PartyChange.Start;
        }

        /// <summary>Ended from outside (a click, a lock, the mode left): no new party until there has been input.</summary>
        public void Cancel()
        {
            if (_phase == Phase.Active) Spend(_startTick);
        }

        void Spend(uint tick) { _phase = Phase.Spent; _spentTick = tick; }

        bool TryRead(out uint tick)
        {
            tick = 0;
            try { return _idle.TryGetLastInputTick(out tick); }
            catch { return false; }
        }

        bool Ask()
        {
            try { return _presence.AcceptsNotifications(); }
            catch { return false; }
        }
    }

    public static class PoopTuning
    {
        public const int MaxPieces = 40;
        public const double DropEverySeconds = 5, Speed = 70, PieceSize = 36, MaxStepSeconds = 0.25;
    }

    /// <summary>Where a piece lands: its centre inside the area, and one of four colours.</summary>
    public struct PoopDrop
    {
        public double X, Y;
        public int Variant;
    }

    /// <summary>
    /// Her walk during a party: straight lines between random points of her monitor's work area, a piece behind her every 5 seconds of
    /// walking, and nothing moving at all once the cap is reached. All values are in the area's own units (device-independent).
    /// </summary>
    public sealed class PoopRoam
    {
        readonly Func<double> _random01;
        bool _begun;
        double _maxX, _maxY, _areaW, _areaH, _petW, _petH, _speed;
        double _toX, _toY, _walked;

        public PoopRoam(Func<double> random01) { _random01 = random01; }

        /// <summary>The pet window's top-left inside the area.</summary>
        public double X { get; private set; }
        public double Y { get; private set; }
        /// <summary>+1 right, -1 left.</summary>
        public int Facing { get; private set; } = 1;
        public int Dropped { get; private set; }
        /// <summary>The cap is reached; nothing moves any more.</summary>
        public bool Finished => Dropped >= PoopTuning.MaxPieces;

        static double Safe(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? 0 : v; }
        static double Clamp(double v, double lo, double hi) { if (hi < lo) return (lo + hi) / 2; return v < lo ? lo : v > hi ? hi : v; }

        double R()
        {
            double v;
            try { v = _random01 == null ? 0.5 : _random01(); } catch { v = 0.5; }
            return double.IsNaN(v) ? 0.5 : v < 0 ? 0 : v > 1 ? 1 : v;
        }

        public void Begin(double areaWidth, double areaHeight, double petWidth, double petHeight, double startX, double startY, double speedScale)
        {
            _areaW = Math.Max(0, Safe(areaWidth)); _areaH = Math.Max(0, Safe(areaHeight));
            _petW = Math.Max(0, Safe(petWidth)); _petH = Math.Max(0, Safe(petHeight));
            _maxX = Math.Max(0, _areaW - _petW);
            _maxY = Math.Max(0, _areaH - _petH);
            X = Clamp(Safe(startX), 0, _maxX);
            Y = Clamp(Safe(startY), 0, _maxY);
            _speed = PoopTuning.Speed * (speedScale > 0 && speedScale <= 1 ? speedScale : 1);
            _walked = 0;
            Dropped = 0;
            Facing = 1;
            _begun = true;
            NextWaypoint();
        }

        void NextWaypoint()
        {
            _toX = R() * _maxX;
            _toY = R() * _maxY;
            if (Math.Abs(_toX - X) >= 1) Facing = _toX > X ? 1 : -1;
        }

        /// <summary>Moves her by one step (capped at a quarter second). True when a piece was dropped in this step.</summary>
        public bool Advance(double seconds, out PoopDrop drop)
        {
            drop = default(PoopDrop);
            if (!_begun || Finished || double.IsNaN(seconds) || seconds <= 0) return false;
            double step = Math.Min(seconds, PoopTuning.MaxStepSeconds);

            double dx = _toX - X, dy = _toY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double go = _speed * step;
            if (dist <= go)
            {
                X = _toX; Y = _toY;
                NextWaypoint();
            }
            else
            {
                X = Clamp(X + dx / dist * go, 0, _maxX);
                Y = Clamp(Y + dy / dist * go, 0, _maxY);
            }

            _walked += step;
            if (_walked < PoopTuning.DropEverySeconds - 1e-6) return false;
            _walked -= PoopTuning.DropEverySeconds;
            if (_walked < 0) _walked = 0;
            double half = PoopTuning.PieceSize / 2;
            drop.X = Clamp(X + _petW / 2 - Facing * _petW * 0.35, half, _areaW - half);
            drop.Y = Clamp(Y + _petH - half, half, _areaH - half);
            int v = (int)(R() * 4);
            drop.Variant = v < 0 ? 0 : v > 3 ? 3 : v;
            Dropped++;
            return true;
        }
    }
}
