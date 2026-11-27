using System;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UnicornPet.Core;

namespace UnicornPet.App
{
    // Zoomies: she races along the bottom of every monitor and back. Within a monitor she runs on the normal lane; between monitors she
    // hops while invisible and the normal work-area code snaps her in, so different scaling, heights, taskbars and gaps need no geometry.
    // The plan and the run are UnicornPet.Core.ZoomiesPlan and ZoomiesRun.
    public partial class PetWindow
    {
        ZoomiesRun _zoom;
        System.Windows.Forms.Screen[] _zoomScreens;
        bool _hopping;
        ZoomiesTimer _zoomTimer;

        /// <summary>From the menu (asked) or the now-and-then timer. Unasked runs also need no bubble and the shell's go-ahead.</summary>
        void StartZoomies(bool asked)
        {
            try
            {
                bool asleep = (_sleep != null && _sleep.Current) || _beh.State == PetState.Sleep;
                bool free = _loadedOnce && _sprites != null && !asleep && !QuietActive && !_inGame && !_dragging && !_petClosing
                    && !_frameGuard.Tripped && _mode == PetMode.Normal;
                if (!free) { if (!asked) _zoomTimer?.Postpone(); return; }
                // the asked path makes no native call
                if (!asked && (_bubble.IsVisible || !Native().AcceptsNotifications())) { _zoomTimer?.Postpone(); return; }

                var screens = System.Windows.Forms.Screen.AllScreens;
                var lanes = new ScreenLane[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                {
                    var r = screens[i].WorkingArea;
                    lanes[i] = new ScreenLane(r.Left, r.Top, r.Right, r.Bottom);
                }
                var mine = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName;
                int start = Array.FindIndex(screens, x => x.DeviceName == mine);
                var wa = WorkArea();
                double fraction = _beh.X / Math.Max(1, wa.Width - Width);
                var run = new ZoomiesRun(ZoomiesPlan.Build(lanes, start, fraction));
                if (!TryEnterMode(PetMode.Zoomies)) return;
                _zoomScreens = screens;
                _zoom = run;
                _hopping = false;
                _beh.Hold(PetState.WalkRight);
            }
            catch
            {
                LeaveMode();
            }
        }

        partial void ZoomiesFrame(TimeSpan dt, System.Windows.Rect wa, ref double left, ref double top)
        {
            try
            {
                if (_zoom == null) { Dispatcher.BeginInvoke(new Action(LeaveMode)); return; }
                // while hopping the window stays where the hop put it (otherwise this frame would write the old lane position back)
                if (_hopping) { left = Left; top = Top; return; }
                double lane = Math.Max(1, wa.Width - Width);
                var step = _zoom.Advance(dt.TotalSeconds, lane);
                if (step == ZoomStep.Hop) { HopTo(_zoom.Screen, ref left, ref top); return; }
                _beh.X = _zoom.Fraction * lane;
                left = wa.Left + _beh.X;   // top stays the lane's
                if (step == ZoomStep.Done) { Dispatcher.BeginInvoke(new Action(LeaveMode)); return; }
                var want = _zoom.Facing < 0 ? PetState.WalkLeft : PetState.WalkRight;
                if (_beh.State != want) _beh.Hold(want);
            }
            catch
            {
                Dispatcher.BeginInvoke(new Action(LeaveMode));
            }
        }

        /// <summary>Makes her invisible and lets this frame move the window to the middle of the next monitor; FinishHop snaps her in.</summary>
        void HopTo(int index, ref double left, ref double top)
        {
            if (_zoomScreens == null || index < 0 || index >= _zoomScreens.Length) { Dispatcher.BeginInvoke(new Action(LeaveMode)); return; }
            _hopping = true;
            Opacity = 0;
            var r = _zoomScreens[index].WorkingArea;
            double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
            left = (r.Left + r.Width / 2.0) / k - Width / 2;
            top = (r.Top + r.Height / 2.0) / k - Height / 2;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => FinishHop(index)));
        }

        void FinishHop(int index)
        {
            if (_mode != PetMode.Zoomies || _zoom == null) return;
            try
            {
                InvalidateWorkArea();
                var here = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName;
                // the monitor went away, or the hop missed: she stops where she landed (LeaveMode clamps her and makes her visible)
                if (here != _zoomScreens[index].DeviceName) { LeaveMode(); return; }
                var wa = WorkArea();
                _beh.X = _zoom.Fraction * Math.Max(1, wa.Width - Width);
                Place();
                Opacity = 1;
                _hopping = false;
                _zoom.Arrived();
                _last = _clock.Elapsed;
            }
            catch
            {
                LeaveMode();
            }
        }

        partial void ZoomiesLeft()
        {
            _hopping = false;
            _zoom = null;
            _zoomScreens = null;
        }

        /// <summary>Every 5 seconds. With "Zoomies now and then" off the timer only re-arms itself; nothing native is asked.</summary>
        partial void ZoomiesTick()
        {
            try
            {
                if (_zoomTimer == null) _zoomTimer = new ZoomiesTimer(() => DateTime.Now, _rand.NextDouble);
                if (_zoomTimer.Due(Settings.RandomZoomies)) StartZoomies(false);
            }
            catch { }
        }
    }
}
