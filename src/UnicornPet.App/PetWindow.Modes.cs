using System;
using System.Windows;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>Who moves the pet: normal behaviour, the idle sparkle poop party, or zoomies. Only one at a time.</summary>
    enum PetMode { Normal, PoopParty, Zoomies }

    public partial class PetWindow
    {
        PetMode _mode = PetMode.Normal;

        // implemented in PetWindow.Idle.cs and PetWindow.Zoomies.cs
        partial void PoopPartyFrame(TimeSpan dt, Rect wa, ref double left, ref double top);
        partial void ZoomiesFrame(TimeSpan dt, Rect wa, ref double left, ref double top);
        partial void PoopPartyLeft();
        partial void ZoomiesLeft();
        partial void IdleTick();
        partial void ZoomiesTick();

        bool QuietActive => Settings.QuietUntil.HasValue && Settings.QuietUntil.Value > DateTime.Now;

        /// <summary>Starts a mode from Normal when she is loaded, awake, visible and free. Dismisses the bubble and stops the horn glitter.</summary>
        bool TryEnterMode(PetMode mode)
        {
            if (mode == PetMode.Normal || _mode != PetMode.Normal) return false;
            bool asleep = (_sleep != null && _sleep.Current) || _beh.State == PetState.Sleep;
            if (!_loadedOnce || _sprites == null || asleep || QuietActive || _inGame || _dragging || _petClosing || _frameGuard.Tripped) return false;
            DismissBubble();
            _mode = mode;
            SyncSparkles();
            Poke();
            return true;
        }

        /// <summary>
        /// Ends any mode the way a drop after a drag ends: the monitor under her becomes home, she is clamped into its work area and
        /// placed on its bottom edge, fully visible, and normal behaviour resumes. Does nothing in Normal.
        /// </summary>
        void LeaveMode()
        {
            if (_mode == PetMode.Normal) return;
            var was = _mode;
            _mode = PetMode.Normal;
            try
            {
                if (was == PetMode.PoopParty) PoopPartyLeft(); else ZoomiesLeft();
            }
            catch { }
            try
            {
                _beh.Release();
                Opacity = 1;
                InvalidateWorkArea();
                var wa = WorkArea();
                _beh.X = Math.Max(0, Math.Min(Left - wa.Left, Math.Max(0, wa.Width - Width)));
                Place();
                _last = _clock.Elapsed;
                _lastState = (PetState)(-1);
                Poke();
                SyncSparkles();
            }
            catch { }
        }

        /// <summary>Ends a mode that may no longer run: asleep (the tracker, never the pose), Quiet, the mini game, the frame guard tripped, or the poop option turned off.</summary>
        void SyncModes()
        {
            if (_mode == PetMode.Normal) return;
            bool asleep = _sleep != null && _sleep.Current;
            if (asleep || QuietActive || _inGame || _frameGuard.Tripped || (_mode == PetMode.PoopParty && !Settings.IdlePoop)) LeaveMode();
        }
    }
}
