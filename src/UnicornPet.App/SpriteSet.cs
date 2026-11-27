using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using UnicornPet.Core;

namespace UnicornPet.App
{
    public class SpriteSet
    {
        public const int FrameMs = 140;
        readonly Dictionary<string, BitmapSource> _img = new Dictionary<string, BitmapSource>();

        public static SpriteSet Load(double dpiScale) { return Load(dpiScale, 200); }

        /// <summary>Loads the poses decoded for a given display height (device-independent pixels).</summary>
        public static SpriteSet Load(double dpiScale, int displayHeight)
        {
            var s = new SpriteSet();
            foreach (var n in PetPoses.All)
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri("pack://application:,,,/Assets/poses/" + n + ".png", UriKind.Absolute);
                bi.DecodePixelHeight = (int)Math.Min(512, Math.Ceiling(displayHeight * Math.Max(1, dpiScale)));
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                s._img[n] = bi;
            }
            return s;
        }

        /// <summary>A pose by file name (without .png), or null when there is no such pose.</summary>
        public BitmapSource Get(string name)
        {
            BitmapSource b;
            return name != null && _img.TryGetValue(name, out b) ? b : null;
        }

        /// <summary>The picture for a state; which pose that is comes from PetPoses (shared with the horn-tip table).</summary>
        public BitmapSource Frame(PetState state, int tick)
        {
            BitmapSource b;
            return _img.TryGetValue(PetPoses.For(state, tick), out b) ? b : _img["idle"];
        }
    }
}
