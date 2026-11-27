using System;
using System.Windows;
using System.Windows.Media;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>
    /// One sparkle poop: a tiny static window (36 x 36 units) that clicks go straight through, that never takes focus and never shows in
    /// Alt-Tab. Its picture is drawn once from shared frozen brushes; there is no timer, no animation and no bitmap.
    /// </summary>
    sealed class PoopPieceWindow : Window
    {
        double _wantLeft, _wantTop;

        public PoopPieceWindow(int variant)
        {
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            Width = PoopTuning.PieceSize;
            Height = PoopTuning.PieceSize;
            Content = new PoopArt(variant);
            // a new window starts with the primary monitor's scale: on a monitor with other scaling the first placement can be off
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(Reassert));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { NoActivate.ApplyClickThrough(this); } catch { }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Reassert));
        }

        /// <summary>Where the piece goes (top-left, device-independent units of the pet's monitor).</summary>
        public void PlaceAt(double left, double top)
        {
            _wantLeft = left;
            _wantTop = top;
            Left = left;
            Top = top;
        }

        void Reassert()
        {
            try
            {
                if (Math.Abs(Left - _wantLeft) > 0.5 || Math.Abs(Top - _wantTop) > 0.5) { Left = _wantLeft; Top = _wantTop; }
            }
            catch { }
        }
    }

    /// <summary>A small three-tier swirl with two four-point stars, in one of the sparkle colours (pink, lavender, mint, gold).</summary>
    sealed class PoopArt : FrameworkElement
    {
        static readonly Brush[] Fills = { Solid(0xFF, 0xB7, 0xD5), Solid(0xC9, 0xB6, 0xF2), Solid(0xA8, 0xE6, 0xCF), Solid(0xFF, 0xD9, 0x7A) };
        static readonly Pen[] Outlines = { Outline(0xD9, 0x7A, 0xA6), Outline(0x93, 0x7C, 0xCF), Outline(0x5F, 0xB8, 0x95), Outline(0xD6, 0xA8, 0x3A) };
        static readonly Brush StarFill = Solid(0xFF, 0xFF, 0xFF);
        static readonly Pen StarEdge = Outline(0xE0, 0xB0, 0x40);
        static readonly Geometry StarShape = MakeStar();
        static readonly Transform StarA = Frozen(new TranslateTransform(6, 7)), StarB = Frozen(new TranslateTransform(30, 13));

        readonly int _variant;

        public PoopArt(int variant)
        {
            _variant = variant < 0 ? 0 : variant > 3 ? 3 : variant;
            IsHitTestVisible = false;
            Focusable = false;
            Width = PoopTuning.PieceSize;
            Height = PoopTuning.PieceSize;
        }

        static Brush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        static Pen Outline(byte r, byte g, byte b)
        {
            var pen = new Pen(Solid(r, g, b), 1);
            pen.Freeze();
            return pen;
        }

        static Transform Frozen(Transform t) { t.Freeze(); return t; }

        static Geometry MakeStar()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -4.5), true, true);
                c.LineTo(new Point(1.2, -1.2), true, false);
                c.LineTo(new Point(4.5, 0), true, false);
                c.LineTo(new Point(1.2, 1.2), true, false);
                c.LineTo(new Point(0, 4.5), true, false);
                c.LineTo(new Point(-1.2, 1.2), true, false);
                c.LineTo(new Point(-4.5, 0), true, false);
                c.LineTo(new Point(-1.2, -1.2), true, false);
            }
            g.Freeze();
            return g;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var fill = Fills[_variant];
            var pen = Outlines[_variant];
            dc.DrawEllipse(fill, pen, new Point(18, 29), 13, 5.5);    // bottom tier
            dc.DrawEllipse(fill, pen, new Point(18, 22.5), 10, 4.8);
            dc.DrawEllipse(fill, pen, new Point(18, 17), 6.8, 4);     // top tier
            dc.DrawEllipse(fill, pen, new Point(19.5, 12.5), 2.6, 2.6); // the little curl on top
            dc.PushTransform(StarA);
            dc.DrawGeometry(StarFill, StarEdge, StarShape);
            dc.Pop();
            dc.PushTransform(StarB);
            dc.DrawGeometry(StarFill, StarEdge, StarShape);
            dc.Pop();
        }
    }
}
