using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using UnicornPet.Core;

namespace UnicornPet.App
{
    public class TrayIcon : IDisposable
    {
        readonly NotifyIcon _icon;
        readonly ContextMenuStrip _strip = new ContextMenuStrip();
        Icon _ico;

#if !WORKSAFE
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        IntPtr _hicon;
#endif

        public TrayIcon(Func<System.Collections.Generic.IReadOnlyList<MenuItemModel>> build, Action doubleClick)
        {
            _icon = new NotifyIcon { Text = "Unicorn Pet", ContextMenuStrip = _strip };
            try
            {
#if WORKSAFE
                // the icon that is also embedded as the exe's own icon
                var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/unicorn.ico", UriKind.Absolute));
                using (var iconStream = sri.Stream) _ico = new Icon(iconStream);
                _icon.Icon = _ico;
#else
                var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/poses/idle.png", UriKind.Absolute));
                using (var bmp = new Bitmap(sri.Stream))
                using (var small = new Bitmap(32, 32))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, 0, 0, 32, 32);
                    }
                    _hicon = small.GetHicon();
                    _ico = Icon.FromHandle(_hicon);
                }
                _icon.Icon = _ico;
#endif
            }
            catch
            {
                _icon.Icon = SystemIcons.Application;
            }
            _strip.Opening += (s, e) => PetMenu.ToStrip(build(), _strip);
            _icon.DoubleClick += (s, e) => doubleClick?.Invoke();
            _icon.Visible = true;
        }

        public bool Visible => _icon.Visible;

        /// <summary>The tooltip: the unicorn's name when she has one, otherwise "Unicorn Pet" (at most 63 characters on .NET Framework).</summary>
        public void SetText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) text = "Unicorn Pet";
                _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
            catch { }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _strip.Dispose();
#if WORKSAFE
            if (_ico != null) _ico.Dispose();
#else
            if (_hicon != IntPtr.Zero) { DestroyIcon(_hicon); _hicon = IntPtr.Zero; }
#endif
        }
    }
}
