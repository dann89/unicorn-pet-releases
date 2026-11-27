using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.Win32;
using UnicornPet.Core;

namespace UnicornPet.App
{
    // Idle sparkle poop (off by default): after 3 minutes without input she trots about her monitor and leaves click-through pieces.
    // Any input clears everything at once. The rules live in UnicornPet.Core.IdleParty and PoopRoam; this file only wires them.
    public partial class PetWindow
    {
        PresenceNative _native;
        IdleParty _party;
        PoopRoam _roam;
        readonly List<PoopPieceWindow> _pieces = new List<PoopPieceWindow>();
        DispatcherTimer _inputPoll;     // 100 ms, only while pieces are showing
        bool _partyBroken;              // something failed once: no more parties this session
        bool _sessionAway;              // locked or disconnected: never start a party unseen

        /// <summary>Created on first use. Creating it calls nothing native.</summary>
        PresenceNative Native() { return _native ?? (_native = new PresenceNative()); }

        /// <summary>Every 5 seconds (the speech timer). With the option off and no party object, nothing at all is asked.</summary>
        partial void IdleTick()
        {
            if (!Settings.IdlePoop && _party == null) return;
            if (_partyBroken || _mode == PetMode.PoopParty) return;   // during a party the 100 ms poll owns it
            try
            {
                if (_party == null) _party = new IdleParty(Native(), Native());
                if (_party.Poll(Settings.IdlePoop, PartyBlocked() || _mode != PetMode.Normal) == PartyChange.Start) StartParty();
            }
            catch { _partyBroken = true; }
        }

        /// <summary>Quiet, asleep (explicit or weekend; not the pose), the mini game, a drag, a tripped frame guard, not loaded.</summary>
        bool PartyBlocked()
        {
            return _sessionAway || QuietActive || (_sleep != null && _sleep.Current) || _inGame || _dragging || _frameGuard.Tripped || _sprites == null
                || !_loadedOnce || _petClosing || DiagnosticsActive;
        }

        void StartParty()
        {
            try
            {
                if (!TryEnterMode(PetMode.PoopParty)) { _party.Cancel(); return; }
                if (_roam == null) _roam = new PoopRoam(_rand.NextDouble);
                var wa = WorkArea();
                _roam.Begin(wa.Width, wa.Height, Width, Height, Left - wa.Left, Top - wa.Top, _k);
                _beh.Hold(PetState.WalkRight);
                if (_inputPoll == null)
                {
                    _inputPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    _inputPoll.Tick += (s, e) => OnInputPoll();
                }
                _inputPoll.Start();
            }
            catch
            {
                _partyBroken = true;
                LeaveMode();
            }
        }

        void OnInputPoll()
        {
            try
            {
                if (_mode != PetMode.PoopParty || _party == null) { _inputPoll.Stop(); return; }
                if (_party.Poll(Settings.IdlePoop, PartyBlocked()) == PartyChange.Stop) LeaveMode();
            }
            catch
            {
                _partyBroken = true;
                LeaveMode();
            }
        }

        partial void PoopPartyFrame(TimeSpan dt, System.Windows.Rect wa, ref double left, ref double top)
        {
            try
            {
                PoopDrop drop;
                if (_roam.Advance(dt.TotalSeconds, out drop))
                {
                    var piece = new PoopPieceWindow(drop.Variant);
                    piece.PlaceAt(wa.Left + drop.X - PoopTuning.PieceSize / 2, wa.Top + drop.Y - PoopTuning.PieceSize / 2);
                    _pieces.Add(piece);   // listed before Show, so a failing Show still gets the window closed
                    piece.Show();
                    Topmost = false;   // keep her in front of the new piece
                    Topmost = true;
                }
                left = wa.Left + _roam.X;
                top = wa.Top + _roam.Y;
                // after the cap she lies down: the sleeping pose, so the frame timer drops to its 1-second interval and nothing animates
                var want = _roam.Finished ? PetState.Sleep : _roam.Facing < 0 ? PetState.WalkLeft : PetState.WalkRight;
                if (_beh.State != want) _beh.Hold(want);
            }
            catch
            {
                _partyBroken = true;
                Dispatcher.BeginInvoke(new Action(LeaveMode));
            }
        }

        partial void PoopPartyLeft()
        {
            try { _inputPoll?.Stop(); } catch { }
            try { _party?.Cancel(); } catch { }
            ClearPieces();
        }

        /// <summary>Every piece is hidden in one pass; the hidden windows are closed afterwards, when the dispatcher is idle.</summary>
        void ClearPieces()
        {
            if (_pieces.Count == 0) return;
            var gone = _pieces.ToArray();
            _pieces.Clear();
            foreach (var piece in gone) { try { piece.Hide(); } catch { } }
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                foreach (var piece in gone) { try { piece.Close(); } catch { } }
            }));
        }

        void StartIdleWatch()
        {
            SystemEvents.SessionSwitch += OnSessionSwitchForIdle;
            SystemEvents.PowerModeChanged += OnPowerModeForIdle;
        }

        void StopIdleWatch()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitchForIdle;
            SystemEvents.PowerModeChanged -= OnPowerModeForIdle;
            try { _inputPoll?.Stop(); } catch { }
            var gone = _pieces.ToArray();
            _pieces.Clear();
            foreach (var piece in gone) { try { piece.Close(); } catch { } }
        }

        // a lock, unlock, sign-out or remote switch, and going to sleep, end a party (or a run) at once
        void OnSessionSwitchForIdle(object sender, SessionSwitchEventArgs e)
        {
            var reason = e.Reason;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (reason == SessionSwitchReason.SessionLock || reason == SessionSwitchReason.ConsoleDisconnect || reason == SessionSwitchReason.RemoteDisconnect) _sessionAway = true;
                    else if (reason == SessionSwitchReason.SessionUnlock || reason == SessionSwitchReason.ConsoleConnect || reason == SessionSwitchReason.RemoteConnect) _sessionAway = false;
                    LeaveMode();
                }));
            }
            catch { }
        }

        void OnPowerModeForIdle(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Suspend) return;
            try { Dispatcher.BeginInvoke(new Action(LeaveMode)); } catch { }
        }
    }
}
