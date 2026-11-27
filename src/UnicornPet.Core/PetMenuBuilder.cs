using System;
using System.Collections.Generic;

namespace UnicornPet.Core
{
    public class MenuItemModel
    {
        public string Text { get; set; }
        public bool IsChecked { get; set; }
        public bool IsSeparator { get; set; }
        public Action OnClick { get; set; }
        public IReadOnlyList<MenuItemModel> Children { get; set; }

        public static MenuItemModel Separator() => new MenuItemModel { IsSeparator = true };
    }

    public class PetMenuActions
    {
        public Action TalkNow { get; set; }
        public Action<bool> SleepChanged { get; set; }
#if !WORKSAFE
        public Action<bool> StartupChanged { get; set; }
#endif
        public Action OpenSettings { get; set; }
#if !WORKSAFE
        public Action CheckUpdates { get; set; }
#endif

        /// <summary>When set, the menu shows "How to start with Windows" (help only) instead of the Start with Windows toggle.</summary>
        public Action StartHelp { get; set; }
        public Action About { get; set; }
        public Action Quit { get; set; }

        /// <summary>When set, the menu shows "Mini game" (or "Resume mini game") right after "Make it talk now".</summary>
        public Action MiniGame { get; set; }
        /// <summary>True when a run is paused in the hidden game window. A throwing or missing function counts as false.</summary>
        public Func<bool> MiniGamePaused { get; set; }

        /// <summary>When set, the menu shows a "Size" submenu (Small, Medium, Large; the current one ticked) after "Scheduled messages".
        /// Called with the new size after it has been stored in the settings and saved.</summary>
        public Action<PetSize> SizeChanged { get; set; }
        /// <summary>When set, the menu shows a tickable "Sparkles" item after "Size". Called after the setting was flipped and saved.</summary>
        public Action<bool> SparklesChanged { get; set; }
        /// <summary>When set, the menu shows a tickable "Sparkle poop when idle" item after "Sparkles". Called after the setting was flipped and saved.</summary>
        public Action<bool> IdlePoopChanged { get; set; }
        /// <summary>When set, the menu shows "Zoomies!" right after the mini game item (or "Make it talk now"), except while asleep or Quiet. Saves nothing.</summary>
        public Action Zoomies { get; set; }
    }

    public class PetMenuBuilder
    {
        readonly Func<Settings> _settings;
        readonly Action _save;
        readonly Func<DateTime> _now;
        readonly PetMenuActions _a;

        public PetMenuBuilder(Func<Settings> settings, Action save, Func<DateTime> now, PetMenuActions actions)
        {
            _settings = settings;
            _save = save;
            _now = now;
            _a = actions;
        }

        static void Call(Action a) { try { a?.Invoke(); } catch { } }

        public IReadOnlyList<MenuItemModel> Build()
        {
            var s = _settings();
            var now = _now();
            bool quietActive = s.QuietUntil.HasValue && s.QuietUntil.Value > now;

            var list = new List<MenuItemModel>
            {
                new MenuItemModel
                {
                    Text = s.Sleeping ? "Wake up" : "Sleep now",
                    OnClick = () => { var st = _settings(); st.Sleeping = !st.Sleeping; _save(); Call(() => _a.SleepChanged?.Invoke(st.Sleeping)); }
                },
                new MenuItemModel
                {
                    Text = quietActive ? "Quiet (until " + s.QuietUntil.Value.ToString("HH:mm") + ")" : "Quiet",
                    Children = new List<MenuItemModel>
                    {
                        new MenuItemModel { Text = "1 hour", OnClick = () => { _settings().QuietUntil = _now().AddHours(1); _save(); } },
                        new MenuItemModel { Text = "Until tomorrow", OnClick = () => { _settings().QuietUntil = NextEight(_now()); _save(); } },
                        new MenuItemModel { Text = "Off", IsChecked = !quietActive, OnClick = () => { _settings().QuietUntil = null; _save(); } },
                    }
                },
                new MenuItemModel { Text = "Make it talk now", OnClick = () => Call(_a.TalkNow) },
                _a.MiniGame == null ? null : new MenuItemModel { Text = IsGamePaused() ? "Resume mini game" : "Mini game", OnClick = () => Call(_a.MiniGame) },
                _a.Zoomies == null || quietActive || SleepPolicy.ShouldSleep(s, now) ? null : new MenuItemModel { Text = "Zoomies!", OnClick = () => Call(_a.Zoomies) },
                MenuItemModel.Separator(),
                new MenuItemModel { Text = "Cheeky mode", IsChecked = s.CheekyMode, OnClick = () => { var st = _settings(); st.CheekyMode = !st.CheekyMode; _save(); } },
                new MenuItemModel { Text = "Scheduled messages", IsChecked = s.ScheduledEnabled, OnClick = () => { var st = _settings(); st.ScheduledEnabled = !st.ScheduledEnabled; _save(); } },
                _a.SizeChanged == null ? null : new MenuItemModel
                {
                    Text = "Size",
                    Children = new List<MenuItemModel> { SizeItem(s, PetSize.Small, "Small"), SizeItem(s, PetSize.Medium, "Medium"), SizeItem(s, PetSize.Large, "Large") }
                },
                _a.SparklesChanged == null ? null : new MenuItemModel
                {
                    Text = "Sparkles", IsChecked = s.Sparkles,
                    OnClick = () => { var st = _settings(); st.Sparkles = !st.Sparkles; _save(); Call(() => _a.SparklesChanged?.Invoke(st.Sparkles)); }
                },
                _a.IdlePoopChanged == null ? null : new MenuItemModel
                {
                    Text = "Sparkle poop when idle", IsChecked = s.IdlePoop,
                    OnClick = () => { var st = _settings(); st.IdlePoop = !st.IdlePoop; _save(); Call(() => _a.IdlePoopChanged?.Invoke(st.IdlePoop)); }
                },
                StartItem(s),
                MenuItemModel.Separator(),
                new MenuItemModel { Text = "Settings\u2026", OnClick = () => Call(_a.OpenSettings) },
#if !WORKSAFE
                _a.CheckUpdates == null ? null : new MenuItemModel { Text = "Check for updates", OnClick = () => Call(_a.CheckUpdates) },
#endif
                new MenuItemModel { Text = "About", OnClick = () => Call(_a.About) },
                new MenuItemModel { Text = "Quit", OnClick = () => Call(_a.Quit) },
            };
            list.RemoveAll(i => i == null);
            return list;
        }

        bool IsGamePaused()
        {
            try { return _a.MiniGamePaused != null && _a.MiniGamePaused(); }
            catch { return false; }
        }

        MenuItemModel SizeItem(Settings s, PetSize size, string text)
        {
            return new MenuItemModel
            {
                Text = text, IsChecked = s.PetSize == size,
                OnClick = () => { var st = _settings(); st.PetSize = size; _save(); Call(() => _a.SizeChanged?.Invoke(size)); }
            };
        }

        MenuItemModel StartItem(Settings s)
        {
            if (_a.StartHelp != null)
                return new MenuItemModel { Text = "How to start with Windows", OnClick = () => Call(_a.StartHelp) };
#if WORKSAFE
            return null;
#else
            return new MenuItemModel
            {
                Text = "Start with Windows", IsChecked = s.StartWithWindows,
                OnClick = () => { var st = _settings(); st.StartWithWindows = !st.StartWithWindows; _save(); Call(() => _a.StartupChanged?.Invoke(st.StartWithWindows)); }
            };
#endif
        }

        static DateTime NextEight(DateTime now)
        {
            var today8 = now.Date.AddHours(8);
            return now < today8 ? today8 : today8.AddDays(1);
        }
    }
}

