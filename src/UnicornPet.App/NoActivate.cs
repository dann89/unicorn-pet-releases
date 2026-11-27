using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace UnicornPet.App
{
    public static class NoActivate
    {
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

#if !WORKSAFE
        const int GWL_STYLE = -16;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
        const int DWMWCP_DONOTROUND = 1;
        const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
#endif

        /// <summary>Keeps the window out of Alt-Tab and the taskbar and stops it taking focus.</summary>
        public static void Apply(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
#if !WORKSAFE
            try
            {
                int corner = DWMWCP_DONOTROUND, none = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref none, 4);
            }
            catch { }
#endif
        }

        /// <summary>As <see cref="Apply"/>, and every click goes straight through the window to whatever is underneath (the sparkle poop pieces).</summary>
        public static void ApplyClickThrough(Window w)
        {
            Apply(w);
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT);
        }

        /// <summary>
        /// Keeps a window out of Alt-Tab and the taskbar but lets it take the keyboard (the mini game). Sets only WS_EX_TOOLWINDOW.
        /// </summary>
        public static void ApplyToolWindow(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        }

#if !WORKSAFE
        public static string Dump(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            int st = GetWindowLong(h, GWL_STYLE), ex = GetWindowLong(h, GWL_EXSTYLE);
            var sb = new StringBuilder();
            sb.AppendLine(w.GetType().Name + " style=0x" + st.ToString("X") + " exstyle=0x" + ex.ToString("X"));
            sb.AppendLine("  caption=" + ((st & 0xC00000) == 0xC00000) + " thickframe=" + ((st & 0x40000) != 0) + " border=" + ((st & 0x800000) != 0));
            sb.AppendLine("  toolwindow=" + ((ex & WS_EX_TOOLWINDOW) != 0) + " noactivate=" + ((ex & WS_EX_NOACTIVATE) != 0) + " layered=" + ((ex & 0x80000) != 0) + " topmost=" + ((ex & 8) != 0));
            int v;
            int hr = DwmGetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, out v, 4);
            sb.AppendLine("  corner_pref hr=0x" + hr.ToString("X") + " value=" + v);
            hr = DwmGetWindowAttribute(h, DWMWA_BORDER_COLOR, out v, 4);
            sb.AppendLine("  border_color hr=0x" + hr.ToString("X") + " value=0x" + v.ToString("X"));
            return sb.ToString();
        }
#endif
    }
}
