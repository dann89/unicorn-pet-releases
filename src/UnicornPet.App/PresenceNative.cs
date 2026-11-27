using System;
using System.Runtime.InteropServices;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>
    /// The two native calls of the idle sparkle poop (off by default): the time of the last keyboard or mouse input, and whether the shell
    /// says the person is busy (full-screen app, presentation, quiet time, locked). Only a time and a state are read; this API has no way
    /// to see what was typed. Polled, never hooked. Neither is called while both idle options are off.
    /// </summary>
    sealed class PresenceNative : IIdleSource, IPresenceSource
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        public uint NowTick => unchecked((uint)Environment.TickCount);

        public bool TryGetLastInputTick(out uint tick)
        {
            tick = 0;
            try
            {
                var info = new LASTINPUTINFO { cbSize = 8 };
                if (!GetLastInputInfo(ref info)) return false;
                tick = info.dwTime;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True only for QUNS_ACCEPTS_NOTIFICATIONS; a failed call, an exception or any other state counts as busy.</summary>
        public bool AcceptsNotifications()
        {
            try
            {
                int state;
                int hr = SHQueryUserNotificationState(out state);
                return PresenceCodes.Accepts(hr, state);
            }
            catch
            {
                return false;
            }
        }
    }
}
