using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnicornPet.Core.MiniGame;

namespace UnicornPet.App
{
    /// <summary>
    /// Draws the playfield from the game's state. It owns no timer and no game rules: the window steps the game and calls
    /// InvalidateVisual. All brushes are frozen statics; per frame only DrawingContext calls are made.
    /// </summary>
    public sealed class GameCanvas : FrameworkElement
    {
        const double Ground = GameTuning.FieldHeight - GameTuning.GroundStrip;   // y of the ground line, from the top
        const double ArtGround = 973.0 / 1024.0;                                 // ground line of the character pictures

        static readonly Brush Sky = Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xE4, 0xD6), Color.FromRgb(0xCF, 0xE8, 0xFA), 90));
        static readonly Brush Grass = Frozen(new SolidColorBrush(Color.FromRgb(0xA8, 0xE6, 0xCF)));
        static readonly Brush Pink = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xA6, 0xC8)));
        static readonly Brush Lavender = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xB6, 0xF2)));
        static readonly Brush Peach = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0xB6)));
        static readonly Brush Gold = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)));
        static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x5C)));
        static readonly Brush Veil = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)));
        static readonly Brush Pill = Frozen(new SolidColorBrush(Colors.White));
        static readonly Brush Halo = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));   // soft outline behind card text
        static readonly Pen Outline = FrozenPen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0x8C))), 1));
        static readonly Typeface Regular = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        static readonly Typeface SemiBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        // Where the visible part of each scenery picture is, in its own pixels:
        // width, height, then the visible bounds left, top, right, bottom.
        static readonly double[] MushroomFit = { 96, 96, 5, 10, 91, 91 };
        static readonly double[] CrystalFit = { 96, 128, 6, 9, 90, 122 };
        static readonly double[] LogFit = { 192, 96, 10, 17, 182, 91 };
        static readonly double[] CloudFit = { 192, 256, 24, 21, 167, 228 };
        static readonly double[] StarFit = { 64, 64, 3, 6, 61, 61 };

        RunnerGame _game;
        GameArt _art;
        string _line, _cardTitle, _cardSub;
        FormattedText _lineText, _titleText, _subText, _titleHalo, _subHalo;
        double _textDip;
        double _runPhase;
        int _lastSteps;

        public GameCanvas()
        {
            Width = GameTuning.FieldWidth;
            Height = GameTuning.FieldHeight;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        /// <summary>True after a run that set a new best: the game-over pose is the cheering one.</summary>
        public bool Cheering { get; set; }

        /// <summary>Connects the canvas to a game and its pictures; null for either detaches (nothing is drawn but the sky).</summary>
        public void Attach(RunnerGame game, GameArt art)
        {
            _game = game;
            _art = art;
            _runPhase = 0;
            _lastSteps = game == null ? 0 : game.Steps;
            InvalidateVisual();
        }

        /// <summary>The bubble line over the runner; null clears it.</summary>
        public void SetLine(string text)
        {
            if (text == _line) return;
            _line = text;
            _lineText = null;
            InvalidateVisual();
        }

        /// <summary>The title / paused / game-over card; nulls clear it.</summary>
        public void SetCard(string title, string sub)
        {
            if (title == _cardTitle && sub == _cardSub) return;
            _cardTitle = title;
            _cardSub = sub;
            _titleText = null;
            _subText = null;
            _titleHalo = null;
            _subHalo = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_game == null || _art == null)
            {
                dc.DrawRectangle(Sky, null, new Rect(0, 0, GameTuning.FieldWidth, GameTuning.FieldHeight));
                return;
            }
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dpi != _textDip) { _lineText = null; _titleText = null; _subText = null; _titleHalo = null; _subHalo = null; _textDip = dpi; }   // text is built for one DPI
            double dist = _game.Distance;

            // scenery, back to front: hills, middle layer, clouds, ground
            var hills = _art.Optional("bg_hills");
            if (hills != null) Tile(dc, hills, dist * 0.15, 0, 840, 164, dpi);
            else dc.DrawRectangle(Sky, null, new Rect(0, 0, GameTuning.FieldWidth, GameTuning.FieldHeight));
            var mid = _art.Optional("bg_mid");
            if (mid != null) Tile(dc, mid, dist * 0.5, 0, 840, 164, dpi);
            var clouds = _art.Optional("bg_clouds");
            if (clouds != null) Tile(dc, clouds, dist * 0.3, 0, 840, 80, dpi);
            var ground = _art.Optional("ground");
            if (ground != null) Tile(dc, ground, dist, Ground, 256, 28, dpi);
            else dc.DrawRectangle(Grass, null, new Rect(0, Ground, GameTuning.FieldWidth, GameTuning.GroundStrip));

            // stars, then obstacles (indexed loops: no enumerator allocated per frame)
            var stars = _game.Stars;
            var starArt = _art.Optional("star");
            for (int i = 0; i < stars.Count; i++) DrawThing(dc, starArt, StarFit, stars[i].Box, 2, true, Gold);
            var obstacles = _game.Obstacles;
            for (int i = 0; i < obstacles.Count; i++) DrawObstacle(dc, obstacles[i]);

            DrawRunner(dc);
            DrawCard(dc, dpi);
            DrawLine(dc, dpi);   // after the card, so the veil does not wash it out; the layout keeps it clear of the card text
        }

        void DrawObstacle(DrawingContext dc, Obstacle o)
        {
            switch (o.Kind)
            {
                case ObstacleKind.Tall: DrawThing(dc, _art.Optional("obstacle_crystal"), CrystalFit, o.Box, 3, false, Lavender); break;
                case ObstacleKind.Wide: DrawThing(dc, _art.Optional("obstacle_log"), LogFit, o.Box, 3, false, Peach); break;
                case ObstacleKind.Overhead: DrawThing(dc, _art.Optional("obstacle_cloud"), CloudFit, o.Box, 0, false, Lavender); break;
                default: DrawThing(dc, _art.Optional("obstacle_mushroom"), MushroomFit, o.Box, 3, false, Pink); break;
            }
        }

        /// <summary>
        /// Draws a picture so its visible part is the collision box plus a few pixels in height (centred on the box; bottom on the box
        /// bottom, or centred vertically for stars). The box itself is smaller than the art, so near misses are forgiving.
        /// Without the picture, a plain rounded shape fills the box exactly.
        /// </summary>
        static void DrawThing(DrawingContext dc, BitmapSource img, double[] fit, Box box, double extra, bool centreY, Brush fallback)
        {
            double boxBottom = Ground - box.Y;              // screen y of the box's bottom edge
            if (img == null)
            {
                dc.DrawRoundedRectangle(fallback, Outline, new Rect(box.X, boxBottom - box.H, box.W, box.H), 5, 5);
                return;
            }
            double visW = fit[4] - fit[2], visH = fit[5] - fit[3];
            double s = (box.H + extra) / visH;
            double left = box.X + box.W / 2 - (fit[2] + visW / 2) * s;
            double top = centreY
                ? boxBottom - box.H / 2 - (fit[3] + visH / 2) * s
                : boxBottom - fit[5] * s;
            dc.DrawImage(img, new Rect(left, top, fit[0] * s, fit[1] * s));
        }

        void DrawRunner(DrawingContext dc)
        {
            var body = _game.Runner;
            var phase = _game.Phase;

            // run cycle: about 100 ms a frame at the start speed, 70 ms at top speed; follows the simulated steps, not the clock
            if (_game.Steps < _lastSteps) _runPhase = 0;   // a new run started
            else if (phase == GamePhase.Running) _runPhase += (_game.Steps - _lastSteps) * GameTuning.StepSeconds * 1000 / FramePeriodMs(_game.Speed);
            _lastSteps = _game.Steps;

            RunnerPose pose;
            if (phase == GamePhase.Over) pose = Cheering ? RunnerPose.Cheer : RunnerPose.Bump;
            else if (phase != GamePhase.Running) pose = RunnerPose.Idle;
            else if (!body.OnGround) pose = body.VelocityY > 0 ? RunnerPose.JumpUp : RunnerPose.JumpDown;
            else if (body.Ducking) pose = RunnerPose.Duck;
            else pose = RunnerPose.Run;

            var img = _art.Runner(pose, (int)_runPhase);
            if (img == null) return;
            double size = GameArt.RunnerSize;
            double left = GameTuning.RunnerX + GameTuning.StandW / 2 - size / 2;
            double bottom = Ground - body.Y + size * (1 - ArtGround);   // the art's ground line sits on the playfield's
            if (phase == GamePhase.Over && _game.StepsSinceBump < 12) left += _game.StepsSinceBump % 2 == 0 ? 2 : -2;
            double height = pose == RunnerPose.Duck && _art.DuckIsPlaceholder ? size * 0.62 : size;
            dc.DrawImage(img, new Rect(left, bottom - height, size, height));
        }

        static double FramePeriodMs(double speed)
        {
            double t = (speed - GameTuning.StartSpeed) / (GameTuning.MaxSpeed - GameTuning.StartSpeed);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return 100 - 30 * t;
        }

        /// <summary>Repeats a picture left to right across the field, scrolled by offset, with x snapped to whole device pixels.</summary>
        static void Tile(DrawingContext dc, BitmapSource img, double offset, double y, double w, double h, double dpi)
        {
            double x = -(offset % w);
            x = Math.Round(x * dpi) / dpi;
            for (; x < GameTuning.FieldWidth; x += w) dc.DrawImage(img, new Rect(x, y, w, h));
        }

        void DrawLine(DrawingContext dc, double dpi)
        {
            if (string.IsNullOrEmpty(_line)) return;
            if (_lineText == null)
            {
                // wrapped by the tested rule; the caps below keep real text inside the tested box whatever the font measures
                _lineText = Text(GameBubbleLayout.Wrap(_line), Regular, GameBubbleLayout.FontSize, dpi);
                _lineText.MaxTextWidth = GameBubbleLayout.MaxTextWidth;
                _lineText.MaxLineCount = GameBubbleLayout.MaxLines;
                _lineText.Trimming = TextTrimming.CharacterEllipsis;
            }
            // fixed place: top band right of the runner's column, above the card (GameBubbleLayout), so it never covers either
            var b = GameBubbleLayout.Place(_lineText.Width, _lineText.Height);
            double r = Math.Min(b.Height / 2, 12);
            dc.DrawRoundedRectangle(Pill, Outline, new Rect(b.Left, b.Top, b.Width, b.Height), r, r);
            dc.DrawText(_lineText, new Point(b.Left + GameBubbleLayout.PadX, b.Top + GameBubbleLayout.PadY));
        }

        void DrawCard(DrawingContext dc, double dpi)
        {
            if (_cardTitle == null && _cardSub == null) return;
            if (_cardTitle != null && _titleText == null)
            {
                _titleText = Text(_cardTitle, SemiBold, 18, dpi);
                _titleHalo = Text(_cardTitle, SemiBold, 18, dpi, Halo);
            }
            if (_cardSub != null && _subText == null)
            {
                _subText = Text(_cardSub, Regular, 12, dpi);
                _subHalo = Text(_cardSub, Regular, 12, dpi, Halo);
            }
            dc.DrawRectangle(Veil, null, new Rect(0, 0, GameTuning.FieldWidth, GameTuning.FieldHeight));
            double y = GameBubbleLayout.CardTop;
            if (_titleText != null)
            {
                Outlined(dc, _titleText, _titleHalo, (GameTuning.FieldWidth - _titleText.Width) / 2, y);
                y += _titleText.Height + 6;
            }
            if (_subText != null) Outlined(dc, _subText, _subHalo, (GameTuning.FieldWidth - _subText.Width) / 2, y);
        }

        /// <summary>Text with a 1 px soft light outline (the same text drawn four times around it first), readable on busy scenery.</summary>
        static void Outlined(DrawingContext dc, FormattedText text, FormattedText halo, double x, double y)
        {
            dc.DrawText(halo, new Point(x - 1, y));
            dc.DrawText(halo, new Point(x + 1, y));
            dc.DrawText(halo, new Point(x, y - 1));
            dc.DrawText(halo, new Point(x, y + 1));
            dc.DrawText(text, new Point(x, y));
        }

        static FormattedText Text(string s, Typeface face, double size, double dpi, Brush brush = null)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush ?? Ink, dpi);
        }

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static Pen FrozenPen(Pen p) { p.Freeze(); return p; }
    }
}
