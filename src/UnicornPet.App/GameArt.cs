using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace UnicornPet.App
{
    public enum RunnerPose { Idle, Run, JumpUp, JumpDown, Duck, Bump, Cheer }

    /// <summary>
    /// The pictures of the mini game, decoded once when the game opens (or the DPI changes) and dropped with the window.
    /// Every picture is looked up by name under Assets/game/; a missing one falls back to an existing pet pose or, for scenery,
    /// to a plain shape drawn by GameCanvas. Nothing here is static, so closing the game releases every bitmap.
    /// </summary>
    public sealed class GameArt
    {
        public const int RunnerSize = UnicornPet.Core.MiniGame.GameBubbleLayout.RunnerSize;   // the character pictures are drawn as a square this size (DIPs); the bubble layout keeps clear of it

        static readonly string[] RunNames = { "run_01", "run_02", "run_03", "run_04", "run_05", "run_06" };
        static readonly string[] WalkNames = { "walk_01", "walk_02", "walk_03", "walk_04" };
        static readonly string[] OptionalNames =
        {
            "obstacle_mushroom", "obstacle_crystal", "obstacle_log", "obstacle_cloud", "star",
            "bg_hills", "bg_mid", "bg_clouds", "ground",
        };

        readonly Dictionary<string, BitmapSource> _optional = new Dictionary<string, BitmapSource>();
        BitmapSource[] _run;
        BitmapSource _idle, _jumpUp, _jumpDown, _duck, _bump, _cheer;

        public bool DuckIsPlaceholder { get; private set; }

        public int RunFrameCount { get { return _run.Length; } }

        public static GameArt Load(double dpiScale)
        {
            var a = new GameArt();
            int charHeight = (int)Math.Min(256, Math.Ceiling(RunnerSize * Math.Max(1, dpiScale)));

            var run = new BitmapSource[RunNames.Length];
            bool allRun = true;
            for (int i = 0; i < run.Length; i++)
            {
                run[i] = TryLoad(RunNames[i], charHeight);
                if (run[i] == null) allRun = false;
            }
            a._jumpUp = TryLoad("jump_up", charHeight);
            a._jumpDown = TryLoad("jump_down", charHeight);
            a._duck = TryLoad("duck", charHeight);
            a._bump = TryLoad("oops", charHeight);
            a._cheer = TryLoad("cheer", charHeight);
            if (allRun) { a._run = run; a._idle = run[3]; }   // run_04 is the frame that stands on the ground line

            // fallbacks: the pet's own poses, decoded at game size, only when something above is missing
            if (!allRun || a._jumpUp == null || a._jumpDown == null || a._duck == null || a._bump == null || a._cheer == null)
            {
                var poses = SpriteSet.Load(dpiScale, RunnerSize);
                if (!allRun)
                {
                    a._run = new BitmapSource[WalkNames.Length];
                    for (int i = 0; i < WalkNames.Length; i++) a._run[i] = poses.Get(WalkNames[i]);
                    a._idle = poses.Get("idle");
                }
                if (a._jumpUp == null) a._jumpUp = poses.Get("happy_hop");
                if (a._jumpDown == null) a._jumpDown = poses.Get("happy_hop");
                if (a._duck == null) { a._duck = poses.Get("sleep"); a.DuckIsPlaceholder = true; }
                if (a._bump == null) a._bump = poses.Get("talk");
                if (a._cheer == null) a._cheer = poses.Get("happy_hop");
            }

            foreach (var n in OptionalNames)
            {
                var b = TryLoad(n, 0);
                if (b != null) a._optional[n] = b;
            }
            return a;
        }

        /// <summary>Never null.</summary>
        public BitmapSource Runner(RunnerPose pose, int frame)
        {
            switch (pose)
            {
                case RunnerPose.Run: return _run[((frame % _run.Length) + _run.Length) % _run.Length];
                case RunnerPose.JumpUp: return _jumpUp;
                case RunnerPose.JumpDown: return _jumpDown;
                case RunnerPose.Duck: return _duck;
                case RunnerPose.Bump: return _bump;
                case RunnerPose.Cheer: return _cheer;
                default: return _idle;
            }
        }

        /// <summary>A scenery picture (obstacle, star, background layer), or null: then GameCanvas draws its plain shape.</summary>
        public BitmapSource Optional(string name)
        {
            BitmapSource b;
            return name != null && _optional.TryGetValue(name, out b) ? b : null;
        }

        /// <summary>A frozen picture from the exe's resources, or null when it is not there (decodeHeight 0 = its own size).</summary>
        static BitmapSource TryLoad(string name, int decodeHeight)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri("pack://application:,,,/Assets/game/" + name + ".png", UriKind.Absolute);
                if (decodeHeight > 0) bi.DecodePixelHeight = decodeHeight;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }
    }
}
