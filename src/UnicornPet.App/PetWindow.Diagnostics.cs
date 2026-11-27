using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using UnicornPet.Core;

namespace UnicornPet.App
{
    // Full (frozen) build only: self-tests, hold state, update events and memory trimming. The work-safe build does not compile this file.
    public partial class PetWindow
    {
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

        public static string SelfTestOut;
        public static string MenuTestOut;
        public static string SettingsTestOut;
        public static PetState? HoldState;
        public event Action<bool> StartWithWindowsChanged;
        public event Action CheckForUpdatesRequested;

        partial void ApplyHold() { if (HoldState.HasValue) _beh.Force = HoldState; }

        partial void CheckDiagnostics(ref bool active)
        {
            active = SelfTestOut != null || MenuTestOut != null || SettingsTestOut != null || HoldState.HasValue;
        }

        partial void RunDiagnostics()
        {
            if (SelfTestOut != null) RunBubbleSelfTest();
            if (MenuTestOut != null) RunMenuSelfTest();
            if (SettingsTestOut != null) RunSettingsSelfTest();
        }

        partial void OnSpeechTick(int ticks)
        {
            if (ticks == 1 || ticks % 24 == 0) TrimMemory();
        }
        static void TrimMemory()
        {
            try
            {
                GC.Collect();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        void RunSettingsSelfTest()
        {
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                bool saved = false;
                var w = new SettingsWindow(Settings, "", "Sam", su => { bool ok = _settingsStore.Save(Settings); RefreshName(); saved = ok; return ok; }, new Point(0, 0));
                lines.Add("blank, windows name on -> " + w.Preview);
                w.UseWindows.IsChecked = false;
                w.RefreshPreview();
                lines.Add("blank, windows name off -> " + w.Preview);
                w.UseWindows.IsChecked = true;
                w.NameInput.Text = "Zoë";
                lines.Add("typed Zoe-diaeresis -> " + w.Preview);
                string err;
                bool ok = w.TrySave(out err);
                lines.Add("save -> ok=" + ok + " callback=" + saved + " persisted=" + _settingsStore.Load().CustomName);
                foreach (var bad in new[] { "{name}", "<b>x</b>", "Sam1", "a{b" })
                {
                    w.NameInput.Text = bad;
                    bool r = w.TrySave(out err);
                    lines.Add("typed " + bad + " -> error=" + w.Error + " | save ok=" + r + " | persisted=" + _settingsStore.Load().CustomName);
                }
                w.NameInput.Text = "";
                lines.Add("cleared -> " + w.Preview);
            }
            catch (Exception ex) { lines.Add("EXCEPTION " + ex.GetType().Name); }
            System.IO.File.WriteAllLines(SettingsTestOut, lines);
            Application.Current.Shutdown();
        }

        void RunMenuSelfTest()
        {
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                var tree = _menuBuilder.Build();
                var wpf = PetMenu.ToWpf(tree);
                var strip = new System.Windows.Forms.ContextMenuStrip();
                PetMenu.ToStrip(tree, strip);
                lines.Add("render: wpf items=" + wpf.Items.Count + " strip items=" + strip.Items.Count + " equal=" + (wpf.Items.Count == strip.Items.Count));
                var icon = new TrayIcon(() => _menuBuilder.Build(), null);
                lines.Add("tray icon created, visible=" + icon.Visible);
                icon.Dispose();
                lines.Add("tray icon disposed");
                lines.Add("menu top-level entries (incl. separators): " + tree.Count + ", has Settings: " + (Find(tree, "Settings") != null));
                foreach (var label in new[] { "Sleep now", "Wake up", "1 hour", "Until tomorrow", "Off", "Make it talk now", "Cheeky mode", "Cheeky mode", "Scheduled messages", "Start with Windows", "Check for updates" })
                {
                    var item = Find(_menuBuilder.Build(), label);
                    if (item == null) { lines.Add(label + ": NOT FOUND"); continue; }
                    item.OnClick();
                    lines.Add(label + " -> sleeping=" + Settings.Sleeping + " state=" + _beh.State + " quietUntil=" + Settings.QuietUntil + " cheeky=" + Settings.CheekyMode + " scheduled=" + Settings.ScheduledEnabled + " startup=" + Settings.StartWithWindows + " bubbleVisible=" + _bubble.IsVisible);
                }
                var reloaded = _settingsStore.Load();
                lines.Add("persisted: sleeping=" + reloaded.Sleeping + " cheeky=" + reloaded.CheekyMode + " scheduled=" + reloaded.ScheduledEnabled + " startup=" + reloaded.StartWithWindows + " quietUntil=" + reloaded.QuietUntil);
            }
            catch (Exception ex) { lines.Add("EXCEPTION " + ex); }
            System.IO.File.WriteAllLines(MenuTestOut, lines);
            Application.Current.Shutdown();
        }

        static MenuItemModel Find(System.Collections.Generic.IEnumerable<MenuItemModel> items, string text)
        {
            foreach (var i in items)
            {
                if (!i.IsSeparator && i.Text != null && i.Text.StartsWith(text)) return i;
                if (i.Children != null) { var c = Find(i.Children, text); if (c != null) return c; }
            }
            return null;
        }

        async void RunBubbleSelfTest()
        {
            var lines = new System.Collections.Generic.List<string>();
            var longText = "Good morning! Today is going to be a really good one, and I will be right here cheering you on the whole way.";
            var wa = WorkArea();
            foreach (var spot in new[] { "left", "right", "center" })
            {
                _beh.X = spot == "left" ? 0 : spot == "right" ? wa.Width : wa.Width / 2;
                Place();
                ShowSpeech(new Speech(longText, SpeechKind.Manual));
                await System.Threading.Tasks.Task.Delay(400);
                var b = new Rect(_bubble.Left, _bubble.Top, _bubble.ActualWidth, _bubble.ActualHeight);
                bool inside = b.Left >= wa.Left - 0.5 && b.Top >= wa.Top - 0.5 && b.Right <= wa.Right + 0.5 && b.Bottom <= wa.Bottom + 0.5;
                lines.Add(spot + " bubble=" + b + " workArea=" + wa + " inside=" + inside + " petState=" + _beh.State);
                DismissBubble();
                await System.Threading.Tasks.Task.Delay(300);
            }
            _timer.Stop();
            var fake = new Rect(Left, wa.Top, 200, 186);
            var sz = _bubble.Measure(longText);
            _bubble.Reposition(sz, fake, wa);
            _bubble.Show();
            await System.Threading.Tasks.Task.Delay(300);
            var tb = new Rect(_bubble.Left, _bubble.Top, _bubble.ActualWidth, _bubble.ActualHeight);
            lines.Add("top bubble=" + tb + " inside=" + (tb.Top >= wa.Top - 0.5 && tb.Bottom <= wa.Bottom + 0.5 && tb.Left >= wa.Left - 0.5 && tb.Right <= wa.Right + 0.5));
            System.IO.File.WriteAllLines(SelfTestOut, lines);
            Application.Current.Shutdown();
        }

    }
}
