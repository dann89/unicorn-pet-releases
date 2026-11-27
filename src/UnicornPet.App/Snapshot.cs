using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>Renders the app's own visuals to PNG files. Never captures the desktop.</summary>
    public static class Snapshot
    {
        const string SampleText = "Good morning! Today is going to be a good one.";

        public static void Run(string dir)
        {
            Directory.CreateDirectory(dir);
            var sprites = SpriteSet.Load(2.0);
            var poses = new List<KeyValuePair<string, PetState>>
            {
                new KeyValuePair<string, PetState>("idle", PetState.Idle),
                new KeyValuePair<string, PetState>("walk_right", PetState.WalkRight),
                new KeyValuePair<string, PetState>("walk_left", PetState.WalkLeft),
                new KeyValuePair<string, PetState>("sleep", PetState.Sleep),
                new KeyValuePair<string, PetState>("hop", PetState.Hop),
                new KeyValuePair<string, PetState>("talk_with_bubble", PetState.Talk),
            };

            foreach (var p in poses)
            {
                if (p.Value == PetState.WalkRight || p.Value == PetState.WalkLeft)
                    for (int t = 0; t < 4; t++) Save(Compose(sprites, p.Value, t, false), 2.0, Path.Combine(dir, p.Key + "_" + (t + 1) + ".png"), null);
                else
                    Save(Compose(sprites, p.Value, 0, p.Value == PetState.Talk), 2.0, Path.Combine(dir, p.Key + ".png"), null);
            }

            foreach (var bg in new[] { Tuple.Create("light", Color.FromRgb(0xF3, 0xF1, 0xEC)), Tuple.Create("dark", Color.FromRgb(0x20, 0x20, 0x24)) })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(bg.Item2) };
                foreach (var p in poses) row.Children.Add(Compose(sprites, p.Value, 1, p.Value == PetState.Talk));
                Save(row, 1.0, Path.Combine(dir, "sheet_" + bg.Item1 + ".png"), bg.Item2);
            }
        }

        static FrameworkElement Compose(SpriteSet sprites, PetState state, int tick, bool bubble)
        {
            var panel = new StackPanel { Width = 260, VerticalAlignment = VerticalAlignment.Bottom };
            var bc = new BubbleControl { Visibility = bubble ? Visibility.Visible : Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Center };
            bc.SetText(bubble ? SampleText : "x");
            bc.SetTail(130, true);
            panel.Children.Add(bc);
            var img = new Image { Width = 200, Height = 200, Source = sprites.Frame(state, tick), Margin = new Thickness(0, 4, 0, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (state == PetState.WalkLeft) { img.RenderTransformOrigin = new Point(0.5, 0.5); img.RenderTransform = new ScaleTransform(-1, 1); }
            panel.Children.Add(img);
            return panel;
        }

        static void Save(FrameworkElement el, double scale, string path, Color? bg)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            el.Arrange(new Rect(el.DesiredSize));
            el.UpdateLayout();
            int w = (int)Math.Ceiling(el.DesiredSize.Width * scale), h = (int)Math.Ceiling(el.DesiredSize.Height * scale);
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
