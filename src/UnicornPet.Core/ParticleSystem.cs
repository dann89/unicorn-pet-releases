using System;

namespace UnicornPet.Core
{
    /// <summary>
    /// One glitter particle. Positions, speeds and sizes are in pet units (1 = the drawn sprite's size), in the pet window's
    /// coordinates with y pointing down, so the same numbers work for every pet size.
    /// </summary>
    public struct Particle
    {
        public bool Alive;
        /// <summary>A four-point twinkle star; otherwise a small round dot.</summary>
        public bool Star;
        /// <summary>Palette index, 0 .. ParticleSystem.PaletteSize - 1 (gold, pink, lavender, mint, white).</summary>
        public int Color;
        public double X, Y;
        public double Vx, Vy;
        public double Age, Life;
        /// <summary>Radius in pet units.</summary>
        public double Size;
        /// <summary>Start of the twinkle cycle, radians.</summary>
        public double Phase;
    }

    /// <summary>
    /// The horn glitter: short bursts of a few particles from an emitter (the horn tip), drifting up and a little outwards and fading.
    /// Pure and deterministic: the random source is injected and time advances in fixed 1/20 s steps. A fixed pool of
    /// <see cref="MaxParticles"/> is reused; nothing is allocated after construction. Between bursts nothing is alive, and
    /// <see cref="Idle"/> with <see cref="SecondsUntilNextBurst"/> tells the caller how long it can sleep without drawing anything.
    /// </summary>
    public sealed class ParticleSystem
    {
        public const double StepSeconds = 0.05;   // 20 updates a second at most
        public const int MaxParticles = 8;
        public const int PaletteSize = 5;
        /// <summary>After a stall at most this many steps are run; the backlog is dropped, never fast-forwarded.</summary>
        public const int MaxStepsPerAdvance = 4;
        const int MaxIdleSteps = 200;        // 10 s of waiting
        const double Slack = 1e-9;           // so a frame of exactly n steps (rounded to timer ticks) is never n - 0.000001 steps

        // tuning (seconds; pet units; pet units per second)
        public const double FirstBurstDelay = 0.5;
        public const int BurstMin = 3, BurstMax = 5;
        public const double EmitGapMin = 0.10, EmitGapMax = 0.22;
        public const double RestMin = 1.5, RestMax = 3.5;
        public const double LifeMin = 0.9, LifeMax = 1.4;
        public const double RiseMin = 0.08, RiseMax = 0.16;
        /// <summary>Sideways speed; positive is the way she faces (outwards from the horn), a little negative is allowed.</summary>
        public const double DriftMin = -0.03, DriftMax = 0.05;
        /// <summary>Share of the speed kept after one second (soft slow-down).</summary>
        public const double SpeedKeptPerSecond = 0.45;
        public const double StarChance = 0.6;
        public const double StarSizeMin = 0.020, StarSizeMax = 0.032;
        public const double DotSizeMin = 0.007, DotSizeMax = 0.012;
        public const double Jitter = 0.012;

        static readonly double KeepPerStep = Math.Pow(SpeedKeptPerSecond, StepSeconds);

        readonly Func<double> _rng;
        readonly Particle[] _pool = new Particle[MaxParticles];
        int _alive;
        double _emitX, _emitY;
        int _facing = 1;
        bool _hasEmitter;
        double _width = 1, _height = 1.25;
        int _burstLeft;
        double _untilEmit, _untilBurst = FirstBurstDelay, _acc;

        public ParticleSystem(Func<double> random01)
        {
            if (random01 == null) throw new ArgumentNullException(nameof(random01));
            _rng = random01;
        }

        public int Capacity { get { return MaxParticles; } }
        public int AliveCount { get { return _alive; } }

        /// <summary>A copy of the particle in pool slot i (check Alive).</summary>
        public Particle this[int i] { get { return _pool[i]; } }

        /// <summary>Nothing alive and no burst under way: nothing to draw until the next burst.</summary>
        public bool Idle { get { return _alive == 0 && _burstLeft == 0; } }

        /// <summary>When idle, the time until the next burst starts; otherwise 0.</summary>
        public double SecondsUntilNextBurst { get { return Idle ? Math.Max(0, _untilBurst) : 0; } }

        /// <summary>Where new particles start (pet units, window coordinates) and which way she faces (+1 right, -1 left).</summary>
        public void SetEmitter(double x, double y, int facing)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) { _hasEmitter = false; return; }
            _emitX = x; _emitY = y; _facing = facing < 0 ? -1 : 1; _hasEmitter = true;
        }

        /// <summary>No horn to emit from (for example an unknown pose): bursts go by without new particles.</summary>
        public void ClearEmitter() { _hasEmitter = false; }

        /// <summary>The drawing area in pet units (the pet window). A particle whose centre leaves it is dropped.</summary>
        public void SetBounds(double width, double height)
        {
            if (width > 0 && height > 0 && !double.IsInfinity(width) && !double.IsInfinity(height)) { _width = width; _height = height; }
        }

        /// <summary>Drops every particle and starts over (first burst after <see cref="FirstBurstDelay"/>).</summary>
        public void Reset()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].Alive = false;
            _alive = 0;
            _burstLeft = 0;
            _untilEmit = 0;
            _untilBurst = FirstBurstDelay;
            _acc = 0;
        }

        /// <summary>
        /// Moves time on by the given seconds in whole fixed steps; the part of a step left over is kept for the next call, so the
        /// result does not depend on the caller's frame rate. Steps while idle only count down to the next burst and are cheap. After
        /// a stall at most <see cref="MaxStepsPerAdvance"/> steps with particles are run (and at most <see cref="MaxStepsPerAdvance"/>
        /// plus 10 s of idle steps); the rest of the backlog is dropped, never fast-forwarded. Returns the number of steps run.
        /// </summary>
        public int Advance(double seconds)
        {
            if (!(seconds > 0) || double.IsInfinity(seconds)) return 0;
            _acc += seconds;
            int steps = 0, busy = 0;
            while (_acc + Slack >= StepSeconds)
            {
                bool idle = Idle;
                if ((!idle && busy >= MaxStepsPerAdvance) || steps >= MaxIdleSteps + MaxStepsPerAdvance) { _acc = 0; break; }
                _acc -= StepSeconds;
                if (!idle) busy++;
                Step();
                steps++;
            }
            if (_acc < 0) _acc = 0;
            return steps;
        }

        /// <summary>One fixed step: age and move the particles, then emit if a burst is due.</summary>
        public void Step()
        {
            const double dt = StepSeconds;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;
                var p = _pool[i];
                p.Age += dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Vx *= KeepPerStep;
                p.Vy *= KeepPerStep;
                if (p.Age >= p.Life || p.X < 0 || p.X > _width || p.Y < 0 || p.Y > _height)
                {
                    p.Alive = false;
                    _alive--;
                }
                _pool[i] = p;
            }

            if (_burstLeft == 0)
            {
                _untilBurst -= dt;
                if (_untilBurst > Slack) return;
                _burstLeft = BurstMin + (int)(R() * (BurstMax - BurstMin + 1));
                if (_burstLeft > BurstMax) _burstLeft = BurstMax;
                _untilEmit = 0;
            }
            _untilEmit -= dt;
            if (_untilEmit > Slack) return;
            Emit();
            _burstLeft--;
            _untilEmit = Range(EmitGapMin, EmitGapMax);
            if (_burstLeft == 0) _untilBurst = Range(RestMin, RestMax);
        }

        void Emit()
        {
            if (!_hasEmitter) return;
            int slot = -1;
            for (int i = 0; i < _pool.Length; i++) if (!_pool[i].Alive) { slot = i; break; }
            if (slot < 0) return;   // pool full: this one is skipped
            var p = new Particle();
            p.Alive = true;
            p.Star = R() < StarChance;
            p.Color = Math.Min(PaletteSize - 1, (int)(R() * PaletteSize));
            p.X = _emitX + Range(-Jitter, Jitter);
            p.Y = _emitY + Range(-Jitter, Jitter);
            p.Vx = _facing * Range(DriftMin, DriftMax);
            p.Vy = -Range(RiseMin, RiseMax);
            p.Life = Range(LifeMin, LifeMax);
            p.Size = p.Star ? Range(StarSizeMin, StarSizeMax) : Range(DotSizeMin, DotSizeMax);
            p.Phase = R() * 2 * Math.PI;
            _pool[slot] = p;
            _alive++;
        }

        double R()
        {
            var v = _rng();
            if (!(v >= 0)) return 0;   // also NaN
            return v > 1 ? 1 : v;
        }

        double Range(double lo, double hi) { return lo + (hi - lo) * R(); }

        /// <summary>Opacity 0..1: a quick fade in over the first fifth of the life, full, then a fade out over the second half.</summary>
        public static double Opacity(Particle p)
        {
            if (!p.Alive || !(p.Life > 0)) return 0;
            double t = p.Age / p.Life;
            if (t < 0 || t >= 1) return 0;
            if (t < 0.2) return t / 0.2;
            if (t > 0.5) return (1 - t) / 0.5;
            return 1;
        }

        /// <summary>Size multiplier for the twinkle (stars pulse between 0.6 and 1.0; dots stay at 1).</summary>
        public static double Twinkle(Particle p)
        {
            if (!p.Star) return 1;
            return 0.8 + 0.2 * Math.Sin(p.Phase + p.Age * 11);
        }
    }
}
