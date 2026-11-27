using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>Where the horn tip is right now, in pet-window units, and which way she faces (+1 right, -1 left). False = no horn to show.</summary>
    public delegate bool HornSource(out double x, out double y, out int facing);

    /// <summary>
    /// Draws the horn glitter over the pet with a few ellipses on one DrawingVisual (no bitmaps, nothing allocated per particle).
    /// The rules live in Core's <see cref="ParticleSystem"/>. Its own timer runs only while <see cref="SetActive"/> says so; between
    /// bursts the timer sleeps until the next one and nothing is redrawn.
    /// </summary>
    public sealed class SparkleLayer : FrameworkElement
    {
        static readonly TimeSpan StepInterval = TimeSpan.FromSeconds(ParticleSystem.StepSeconds);
        // gold, pink, lavender, mint, white: the pet's own palette
        static readonly Brush[] Palette = MakePalette();

        readonly DrawingVisual _visual = new DrawingVisual();
        readonly ParticleSystem _system;
        readonly HornSource _horn;
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render);
        readonly Stopwatch _clock = new Stopwatch();
        readonly FailureGuard _guard = new FailureGuard(5);   // a drawing bug stops the glitter, never the pet
        TimeSpan _last;
        double _unit = PetSizes.LargeSprite;   // DIPs per pet unit (the sprite's size)
        int _drawn;                            // particles in the current drawing

        public SparkleLayer(Func<double> random01, HornSource horn)
        {
            _system = new ParticleSystem(random01);
            _horn = horn;
            IsHitTestVisible = false;
            AddVisualChild(_visual);
            _timer.Interval = StepInterval;
            _timer.Tick += (s, e) =>
            {
                _guard.Run(OnTick);
                if (_guard.Tripped) Stop();
            };
        }

        protected override int VisualChildrenCount { get { return 1; } }

        protected override Visual GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return _visual;
        }

        /// <summary>True while the timer runs (for checks).</summary>
        public bool Running { get { return _timer.IsEnabled; } }

        /// <summary>The sprite's size and the window's size in DIPs. Drops the particles on screen; the next burst starts afresh.</summary>
        public void SetSize(double spriteSize, double windowWidth, double windowHeight)
        {
            if (!(spriteSize > 0)) return;
            _unit = spriteSize;
            _system.SetBounds(windowWidth / spriteSize, windowHeight / spriteSize);
            _system.Reset();
            Clear();
            if (_timer.IsEnabled) Restart();
        }

        /// <summary>Starts or stops the glitter. Off means no timer at all and nothing drawn.</summary>
        public void SetActive(bool on)
        {
            if (on)
            {
                if (_timer.IsEnabled || _guard.Tripped) return;
                _system.Reset();
                Restart();
                _timer.Start();
            }
            else Stop();
        }

        void Restart()
        {
            _clock.Restart();
            _last = TimeSpan.Zero;
            _timer.Interval = NextInterval();
        }

        void Stop()
        {
            _timer.Stop();
            _clock.Stop();
            _system.Reset();
            Clear();
        }

        void OnTick()
        {
            var now = _clock.Elapsed;
            double dt = (now - _last).TotalSeconds;
            _last = now;
            double x, y;
            int facing;
            if (_horn != null && _horn(out x, out y, out facing)) _system.SetEmitter(x / _unit, y / _unit, facing);
            else _system.ClearEmitter();
            _system.Advance(dt);
            if (_system.AliveCount > 0 || _drawn > 0) Draw();
            var want = NextInterval();
            if (_timer.Interval != want) _timer.Interval = want;
        }

        /// <summary>20 times a second while there is glitter; otherwise asleep until the next burst is due.</summary>
        TimeSpan NextInterval()
        {
            if (!_system.Idle) return StepInterval;
            return TimeSpan.FromSeconds(Math.Max(ParticleSystem.StepSeconds, _system.SecondsUntilNextBurst));
        }

        void Clear()
        {
            if (_drawn == 0) return;
            using (_visual.RenderOpen()) { }
            _drawn = 0;
        }

        void Draw()
        {
            int n = 0;
            using (var dc = _visual.RenderOpen())
            {
                for (int i = 0; i < _system.Capacity; i++)
                {
                    var p = _system[i];
                    if (!p.Alive) continue;
                    n++;
                    double alpha = ParticleSystem.Opacity(p);
                    if (alpha < 0.02) continue;
                    var brush = Palette[p.Color < 0 || p.Color >= Palette.Length ? 0 : p.Color];
                    var centre = new Point(p.X * _unit, p.Y * _unit);
                    double r = Math.Max(0.75, p.Size * _unit * ParticleSystem.Twinkle(p));
                    dc.PushOpacity(alpha);
                    if (p.Star)
                    {
                        // a four-point twinkle: two thin crossed ellipses and a small round centre
                        dc.DrawEllipse(brush, null, centre, r, r * 0.22);
                        dc.DrawEllipse(brush, null, centre, r * 0.22, r);
                        dc.DrawEllipse(brush, null, centre, r * 0.3, r * 0.3);
                    }
                    else dc.DrawEllipse(brush, null, centre, r, r);
                    dc.Pop();
                }
            }
            _drawn = n;
        }

        static Brush[] MakePalette()
        {
            var colours = new[]
            {
                Color.FromRgb(0xFF, 0xC9, 0x3C),   // gold (the horn)
                Color.FromRgb(0xFF, 0x8F, 0xC8),   // pink
                Color.FromRgb(0xB8, 0x9C, 0xFF),   // lavender
                Color.FromRgb(0x7C, 0xDF, 0xBE),   // mint
                Color.FromRgb(0xFF, 0xFF, 0xFF),   // white
            };
            var list = new Brush[colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                var b = new SolidColorBrush(colours[i]);
                b.Freeze();
                list[i] = b;
            }
            return list;
        }
    }
}
