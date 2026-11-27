using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UnicornPet.Core;

namespace UnicornPet.App
{
    public partial class PetWindow : Window
    {
        SpriteSet _sprites;
        Rect _wa;
        bool _waValid;
        int _speechTicks;
        readonly Random _rand = new Random();
        readonly PetBehaviour _beh;
        readonly SettingsStore _settingsStore = new SettingsStore(System.IO.Path.Combine(App.RoamingDir, "settings.json"));
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render);
        TimeSpan _last;
        double _hopT = -1, _nextHopAt;
        int _lastTick = -1;
        PetState _lastState = (PetState)(-1);
        Point _downScreen;
        bool _pressed, _dragStarted, _dragging;

        readonly BubbleWindow _bubble = new BubbleWindow();
        readonly DispatcherTimer _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        readonly DispatcherTimer _dismissTimer = new DispatcherTimer();
        Scheduler _scheduler;
        public Settings Settings { get; private set; } = new Settings();
        public SettingsStore Store => _settingsStore;
        string _fileName = "", _detectedName = "";
        PetMenuBuilder _menuBuilder;
        TrayIcon _tray;
        GameWindow _game;      // the mini game window while it exists (shown or hidden with a paused run)
        bool _inGame;          // the game window is showing: the pet is hidden and its timers are stopped
        bool _petClosing;      // this window is closing or closed (Windows sign-out closes it before the game): never show it again
        bool _loadedOnce;

        // pet options: size (Large = the original 200 x 250 window), the horn glitter, the unicorn's own name
        PetSize _size = PetSize.Large;
        double _k = 1;                          // size scale: 1, 0.7 or 0.5
        int _spritePx = PetSizes.LargeSprite;   // drawn sprite size, DIPs
        string _pose = "idle";                  // the pose picture on screen (for the horn tip)
        readonly SparkleLayer _sparkles;
        string _introducedPetName = "";         // the pet name she last introduced herself with (or had at start)

        /// <summary>A double-click on her (a single click only ends zoomies or the poop party).</summary>
        public event Action Clicked;
        readonly DoubleClick _doubleClick = new DoubleClick(TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime), 8);

        // Hooks implemented only by the full build (PetWindow.Diagnostics.cs); in the work-safe build they compile to nothing.
        partial void ApplyHold();
        partial void RunDiagnostics();
        partial void CheckDiagnostics(ref bool active);
        partial void OnSpeechTick(int ticks);
        bool DiagnosticsActive { get { bool a = false; CheckDiagnostics(ref a); return a; } }

        public PetWindow()
        {
            InitializeComponent();
            _beh = new PetBehaviour(_rand.NextDouble);
            // the glitter is drawn above the sprite in the same window, and never takes the mouse
            _sparkles = new SparkleLayer(_rand.NextDouble, TryGetHorn);
            ((System.Windows.Controls.Panel)Content).Children.Add(_sparkles);
            _nextHopAt = 20 + _rand.NextDouble() * 30;
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.Tick += (s, e) =>
            {
                _frameGuard.Run(OnFrame);
                if (_frameGuard.Tripped) StopAnimation();
            };
            Loaded += (s, e) => OnLoaded();
            Closing += (s, e) => { if (!e.Cancel) { LeaveMode(); _petClosing = true; } };
            Closed += (s, e) => { _petClosing = true; SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; StopIdleWatch(); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += (s, e) => { TryRestartAnimation(); var m = PetMenu.ToWpf(_menuBuilder.Build()); m.PlacementTarget = this; m.IsOpen = true; e.Handled = true; };
            _speechTimer.Tick += (s, e) =>
            {
                ApplySleepState(false);
                var sp = _mode == PetMode.Normal ? _scheduler?.Tick(Settings) : null;
                if (sp != null) ShowSpeech(sp);
                _speechTicks++;
                OnSpeechTick(_speechTicks);
                IdleTick(); ZoomiesTick();
            };
            _dismissTimer.Tick += (s, e) => DismissBubble();
            _bubble.Dismissed += DismissBubble;
            Clicked += () => { TryRestartAnimation(); if (_scheduler != null) ShowSpeech(_scheduler.SpeakNow(Settings)); };
        }

        // after 5 consecutive failures the animation stops and the pet stays as a still picture (the rest of the program keeps working)
        readonly FailureGuard _frameGuard = new FailureGuard(5);

        void StopAnimation()
        {
            try
            {
                _timer.Stop();
                if (_sprites != null) SetPose(PetState.Idle, 0);
                Scale.ScaleX = 1; Scale.ScaleY = 1; Shift.Y = 0;
            }
            catch { }
            try { SyncSparkles(); } catch { }
        }

        // the drawn unicorn inside the window (the top 14 of the 200 at Large are empty picture)
        Rect PetRect() { return new Rect(Left, Top + Height - _spritePx + 14 * _k, _spritePx, _spritePx - 14 * _k); }

        void SetPose(PetState state, int tick)
        {
            _pose = PetPoses.For(state, tick);
            Sprite.Source = _sprites.Frame(state, tick);
        }

        public void ShowSpeech(Speech s)
        {
            if (_inGame) return;
            LeaveMode();
            var text = BubbleText.Fit(s.Text); // final display cap, after {name} has been filled in
            var dur = TimeSpan.FromMilliseconds(Math.Max(5000, 60.0 * text.Length));
            _beh.Talk(dur);
            Poke();
            _bubble.ShowAt(text, PetRect(), WorkArea());
            _dismissTimer.Stop();
            _dismissTimer.Interval = dur;
            _dismissTimer.Start();
        }

        public void DismissBubble()
        {
            _dismissTimer.Stop();
            if (_bubble.IsVisible) _bubble.Hide();
            if (_beh.State == PetState.Talk) { _beh.Talk(TimeSpan.FromMilliseconds(100)); Poke(); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NoActivate.Apply(this);
        }

        void OnLoaded()
        {
            if (_loadedOnce) return;   // the pet window is hidden and shown again around the mini game
            _loadedOnce = true;
            // leftovers of an interrupted save (only the exact temporary names this program creates)
            AtomicFile.CleanStaleTemp(App.RoamingDir, "settings.json", "messages.remote.json", "messages.remote.json.version", "messages.custom.json");
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            StartIdleWatch();
            bool firstRun = !_settingsStore.Exists;
            var st = _settingsStore.Load();
            Settings = st;
            SetSizeFields(st.PetSize);   // before the sprites are decoded, so they are decoded at the size they are drawn
            _sprites = SpriteSet.Load(VisualTreeHelper.GetDpi(this).DpiScaleX, _spritePx);
            var wa = WorkArea();
            _beh.X = _rand.NextDouble() * Math.Max(0, wa.Width - Width);
            _introducedPetName = PetNameText.Clean(st.PetName);
#if WORKSAFE
            // precedence: messages.json beside the exe (hand-edited override), then the verified remote copy, then the built-in defaults
            var bank = MessageBank.LoadWithRemote(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "messages.json"), System.IO.Path.Combine(App.RoamingDir, "messages.remote.json"));
            bank = StartCustomMessages(bank);
#else
            var bank = MessageBank.Load(System.IO.Path.Combine(App.RoamingDir, "messages.json"));
#endif
            _scheduler = new Scheduler(bank, () => DateTime.Now, new Random());
            _scheduler.ExpectTicks();
            _fileName = _scheduler.BankName;
            DetectNameAsync();
            RefreshName();
            _speechTimer.Start();
            // every menu change re-checks the glitter at once (Quiet has no hook of its own)
            _menuBuilder = new PetMenuBuilder(() => Settings, () => { _settingsStore.Save(Settings); SyncSparkles(); }, () => DateTime.Now, new PetMenuActions
            {
                TalkNow = () => ShowSpeech(_scheduler.SpeakNow(Settings)),
                SleepChanged = sleeping => { TryRestartAnimation(); ApplySleepState(true); },
#if WORKSAFE
                StartHelp = () => ShowSpeech(new Speech("Press Win+R, type shell:startup, press Enter, and drop a shortcut to me in that folder.", SpeechKind.Manual)),
                OpenSettings = OpenSettings,
#else
                StartupChanged = v => StartWithWindowsChanged?.Invoke(v),
                OpenSettings = OpenSettings,
                CheckUpdates = () => CheckForUpdatesRequested?.Invoke(),
#endif
                About = () => System.Windows.MessageBox.Show("Unicorn Pet " + (typeof(PetWindow).Assembly.GetName().Version.ToString(3)) + "\nA little friend for your desktop.\n\nWith \"Use my Windows first name\" ticked, it asks Windows for your display name once at start; on a domain PC Windows itself may contact the domain controller to answer. Untick it in Settings to prevent that.", "About Unicorn Pet"),
                Quit = () => { CloseMiniGame(); _tray?.Dispose(); Application.Current.Shutdown(); },
                // opened after the menu has finished closing, so the new window keeps the focus
                MiniGame = () => Dispatcher.BeginInvoke(new Action(OpenMiniGame)),
                Zoomies = () => Dispatcher.BeginInvoke(new Action(() => StartZoomies(true))),
                MiniGamePaused = () => _game != null && _game.HasPausedRun,
                SizeChanged = size => ApplySize(size),
                SparklesChanged = on => SyncSparkles(),
                IdlePoopChanged = on => { if (!on && _mode == PetMode.PoopParty) LeaveMode(); },
            });
            if (!DiagnosticsActive)
            {
                _tray = new TrayIcon(() => _menuBuilder.Build(), () => ShowSpeech(_scheduler.SpeakNow(Settings)));
                _tray.SetText(PetNameText.TrayText(Settings.PetName));
            }
            _sleep = new SleepTracker(() => DateTime.Now);
            ApplySleepState(true);
            ApplyHold();
            if (!DiagnosticsActive)
            {
                var greet = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5 + _rand.NextDouble() * 5) };
                greet.Tick += (s, ev) =>
                {
                    greet.Stop();
                    var g = _scheduler.Greet(Settings, firstRun);
                    if (g != null) ShowSpeech(g);
                    if (firstRun) _settingsStore.Save(Settings);
                };
                greet.Start();
            }
            _last = _clock.Elapsed;
            Place();
            _timer.Start();
            SyncSparkles();
#if WORKSAFE
            StartMessageUpdates();
#endif
            RunDiagnostics();

        }

#if WORKSAFE
        // Text-only refresh of the message list: signed, validated, at most once a day, applied the next time the program starts.
        MessageUpdater _messages;

        void StartMessageUpdates()
        {
            var fetcher = Fetcher();   // one shared fetcher for the signed list and the custom web list
            _messages = new MessageUpdater(
                (url, max, deadline) => fetcher.FetchAsync(url, max, deadline),
                new UnicornPet.Core.Updater.ManifestVerifier(UnicornPet.Core.Updater.ReleaseKey.PublicKeyXml),
                System.IO.Path.Combine(App.RoamingDir, "messages.remote.json"));
            var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            first.Tick += async (s, e) => { first.Stop(); await CheckMessagesAsync(); };
            first.Start();
            var daily = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            daily.Tick += async (s, e) => await CheckMessagesAsync();
            daily.Start();
        }

        async System.Threading.Tasks.Task CheckMessagesAsync()
        {
            try { await _messages.CheckAsync(MessageUpdater.DefaultManifestUrl, Settings, () => _settingsStore.Save(Settings)); }
            catch { }
        }
#endif
        bool _detectionStarted;

        async void DetectNameAsync()
        {
            try
            {
                // asked before anything is looked up: with "Use my Windows first name" off, GetUserNameEx is never called
                if (!Settings.UseWindowsName || _detectionStarted) return;
                _detectionStarted = true;
                var allowed = Settings;
                var task = System.Threading.Tasks.Task.Run(() => FirstNameResolver.ResolveIfAllowed(allowed, new WindowsUserNameSource()));
                var done = await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(2000));
                if (done == task) { _detectedName = task.Result ?? ""; RefreshName(); }
            }
            catch { }
        }

        public void RefreshName()
        {
            _scheduler?.UpdateName(NameProvider.Resolve(Settings, _fileName, _detectedName));
        }

        void OpenSettings()
        {
            SettingsWindow.ShowSingle(() =>
            {
                var w = new SettingsWindow(Settings, _fileName, _detectedName, startupChanged =>
            {
                bool saved = _settingsStore.Save(Settings);   // the change is already applied in memory; the dialog reports a failed save
                if (Settings.UseWindowsName) DetectNameAsync(); // no-op if already done; starts the lookup only once the person allows it
                RefreshName();
#if WORKSAFE
                MessagesSettingsSaved();
#endif
                try
                {
                    if (Settings.PetSize != _size) ApplySize(Settings.PetSize);
                    PetNameSaved();
                }
                catch { }   // a display problem must not be reported as a failed save
                ApplySleepState(true);   // also starts or stops the glitter
#if !WORKSAFE
                if (startupChanged && saved) StartWithWindowsChanged?.Invoke(Settings.StartWithWindows);
#endif
                return saved;
            }, new Point(Left + Width / 2, Top + Height / 2));
#if WORKSAFE
                w.AttachMessages(BuildMessagesUi());
#endif
                return w;
            });
        }

        // Sleep is derived: asleep when the person chose "Sleep now" or it is the weekend. Re-checked from the clock about once a minute.
        SleepTracker _sleep;

        void ApplySleepState(bool force)
        {
            if (_sleep == null) return;
            var change = _sleep.Poll(Settings, force);
            if (change.HasValue)
            {
                if (change.Value) _beh.Sleep(); else _beh.Wake();
                Poke();
            }
            SyncSparkles();   // also picks up Quiet running out (this runs every 5 s)
        }

        // ---------- pet options ----------

        /// <summary>Sets the window, sprite, speed and glitter for a size. Does not move the pet or decode sprites.</summary>
        void SetSizeFields(PetSize size)
        {
            _size = size;
            _k = PetSizes.Scale(size);
            _spritePx = PetSizes.SpritePx(size);
            Width = PetSizes.WindowWidth(size);
            Height = PetSizes.WindowHeight(size);
            Sprite.Width = _spritePx;
            Sprite.Height = _spritePx;
            _beh.SpeedScale = _k;
            _sparkles.SetSize(_spritePx, Width, Height);
        }

        /// <summary>Changes the size at once: keeps her centre where it was (inside the screen), re-decodes the poses at the new size and keeps the bubble attached.</summary>
        void ApplySize(PetSize size)
        {
            if (_sprites == null) return;   // not loaded yet: OnLoaded applies the saved size
            LeaveMode();
            double centre = _beh.X + Width / 2;
            SetSizeFields(size);
            _sprites = SpriteSet.Load(VisualTreeHelper.GetDpi(this).DpiScaleX, _spritePx);
            InvalidateWorkArea();
            _beh.X = centre - Width / 2;
            Place();   // clamps her into the work area
            int tick = _lastTick < 0 ? 0 : _lastTick;
            SetPose(_beh.State, tick);
            _lastState = (PetState)(-1);   // the next frame sets the pose and transform afresh
            if (_bubble.IsVisible) _bubble.Reposition(_bubble.LastSize, PetRect(), WorkArea());
            Poke();
            SyncSparkles();
        }

        /// <summary>The glitter runs only while it is wanted and she is visible, awake, not quiet and not in the mini game.</summary>
        void SyncSparkles()
        {
            SyncModes();
            bool quiet = Settings.QuietUntil.HasValue && Settings.QuietUntil.Value > DateTime.Now;
            bool asleep = (_sleep != null && _sleep.Current) || _beh.State == PetState.Sleep;
            bool on = _loadedOnce && _sprites != null && Settings.Sparkles && !_inGame && IsVisible && !asleep && !quiet && !_frameGuard.Tripped && _mode == PetMode.Normal;
            _sparkles.SetActive(on);
        }

        /// <summary>Where the horn tip is now, from the pose on screen and the sprite's current flip, breath and hop.</summary>
        bool TryGetHorn(out double x, out double y, out int facing)
        {
            x = 0; y = 0; facing = 1;
            if (_beh.State == PetState.Sleep) return false;
            double u, v;
            if (!HornTips.TryGet(_pose, out u, out v)) return false;
            HornTips.ToWindow(u, v, _spritePx, Width, Height, Scale.ScaleX, Scale.ScaleY, Shift.Y, out x, out y);
            facing = Scale.ScaleX < 0 ? -1 : 1;
            return true;
        }

        /// <summary>After Settings was saved: a new pet name shows in the tray tooltip, and she says hello with it once.</summary>
        void PetNameSaved()
        {
            var pet = PetNameText.Clean(Settings.PetName);
            if (pet == _introducedPetName) return;
            _introducedPetName = pet;
            _tray?.SetText(PetNameText.TrayText(pet));
            var line = PetNameText.Intro(NameProvider.Resolve(Settings, _fileName, _detectedName), pet);
            if (line != null) ShowSpeech(new Speech(line, SpeechKind.Manual));
        }

        // After the frame timer gave up, a display change or an explicit action (click, menu) may start it again, at most once per 30 s.
        void TryRestartAnimation()
        {
            if (_inGame) return;
            if (_frameGuard.TryRecover(DateTime.Now))
            {
                _last = _clock.Elapsed;
                _lastState = (PetState)(-1);
                _timer.Interval = TimeSpan.FromMilliseconds(33);
                _timer.Start();
                SyncSparkles();
            }
        }

        public PetBehaviour Behaviour => _beh;

        // ---------- mini game ----------

        void OpenMiniGame()
        {
            try
            {
                var pet = PetRect();   // read before the pet hides
                var wa = WorkArea();
                if (_game == null)
                {
                    var g = new GameWindow(() => Settings, () => _settingsStore.Save(Settings), _rand.NextDouble);
                    g.IsVisibleChanged += (s, e) => SetGameMode(_game != null && _game.IsVisible);
                    g.Closed += (s, e) => { if (_game == g) _game = null; SetGameMode(false); };
                    _game = g;
                }
                _game.ShowGame(pet, wa);
            }
            catch
            {
                CloseMiniGame();
                _game = null;
                SetGameMode(false);
            }
        }

        void CloseMiniGame()
        {
            try { _game?.Close(); } catch { }   // saves a beaten best on the way out
        }

        /// <summary>The pet steps aside while the game is showing (hidden, timers stopped) and comes back when it hides or closes.</summary>
        void SetGameMode(bool on)
        {
            if (_petClosing) { _inGame = false; return; }   // the game closing after the pet (sign-out): Show() would throw
            if (on == _inGame) return;
            if (on)
            {
                LeaveMode();
                DismissBubble();
                _timer.Stop();
                _speechTimer.Stop();
                _inGame = true;
                Hide();
                SyncSparkles();
                return;
            }
            _inGame = false;
            Show();
            InvalidateWorkArea();
            Place();
            _last = _clock.Elapsed;
            _lastState = (PetState)(-1);
            if (!_frameGuard.Tripped)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(33);
                _timer.Start();
            }
            _speechTimer.Start();
            ApplySleepState(true);
        }

        Rect WorkArea()
        {
            if (!_waValid)
            {
                var h = new WindowInteropHelper(this).Handle;
                var scr = System.Windows.Forms.Screen.FromHandle(h);
                var wa = scr.WorkingArea;
                double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
                _wa = new Rect(wa.X / k, wa.Y / k, wa.Width / k, wa.Height / k);
                _waValid = true;
            }
            return _wa;
        }

        void InvalidateWorkArea() { _waValid = false; }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _sprites = SpriteSet.Load(newDpi.DpiScaleX, _spritePx);
            InvalidateWorkArea();
            _lastState = (PetState)(-1);
            Poke();
        }

        public void Poke()
        {
            _timer.Interval = TimeSpan.FromMilliseconds(33);
        }

        void Place()
        {
            var wa = WorkArea();
            double maxX = Math.Max(0, wa.Width - Width);
            if (_beh.X > maxX) _beh.X = maxX;
            if (_beh.X < 0) _beh.X = 0;
            Left = wa.Left + _beh.X;
            Top = wa.Bottom - Height;
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LeaveMode();
                InvalidateWorkArea();
                _beh.Step(TimeSpan.Zero, 0, Math.Max(0, WorkArea().Width - Width));
                Place();
                TryRestartAnimation();
            }));
        }

        static int IntervalFor(PetState s, bool hopping)
        {
            if (hopping) return 33;
            switch (s)
            {
                case PetState.WalkLeft:
                case PetState.WalkRight: return 50;
                case PetState.Idle: return 200;
                case PetState.Talk: return 200;
                default: return 1000;
            }
        }

        void OnFrame()
        {
            var el = _clock.Elapsed;
            var dt = el - _last;
            _last = el;
            if (_dragging) return;

            var wa = WorkArea();
            _beh.Step(dt, 0, Math.Max(0, wa.Width - Width));

            double secs = el.TotalSeconds;
            if (!_beh.Force.HasValue && _beh.State != PetState.Sleep && _beh.State != PetState.Talk && _hopT < 0 && secs >= _nextHopAt && _mode == PetMode.Normal)
            {
                _beh.Hop();
                _nextHopAt = secs + 20 + _rand.NextDouble() * 40;
            }
            if (_beh.State == PetState.Hop) _hopT = Math.Min(1, (_hopT < 0 ? 0 : _hopT) + dt.TotalSeconds / 0.7); else _hopT = -1;

            var state = _beh.State;
            int tick = (int)(el.TotalMilliseconds / SpriteSet.FrameMs);
            bool walking = state == PetState.WalkLeft || state == PetState.WalkRight;
            if (state != _lastState || (walking && tick != _lastTick))
            {
                _lastTick = tick;
                _lastState = state;
                SetPose(state, tick);
            }

            if (state != PetState.Sleep)
            {
                double flip = state == PetState.WalkLeft ? -1 : 1;
                double breath = state == PetState.Idle ? 1 + 0.015 * Math.Sin(secs * 1.6) : 1;
                double sy = breath, sx = flip * breath, shift = 0;
                if (_hopT >= 0)
                {
                    double h = Math.Sin(Math.PI * _hopT);
                    shift = -h * 36 * _k;
                    sy = 1 + (h > 0.9 ? 0 : 0.05 * (1 - h)) - 0.04 * h;
                    sx = flip * (1 - (sy - 1) * 0.6);
                }
                if (Math.Abs(Scale.ScaleX - sx) > 0.0005) Scale.ScaleX = sx;
                if (Math.Abs(Scale.ScaleY - sy) > 0.0005) Scale.ScaleY = sy;
                if (Math.Abs(Shift.Y - shift) > 0.05 * _k) Shift.Y = shift;
            }

            double nl = wa.Left + _beh.X, nt = wa.Bottom - Height;
            if (_mode == PetMode.PoopParty) PoopPartyFrame(dt, wa, ref nl, ref nt); else if (_mode == PetMode.Zoomies) ZoomiesFrame(dt, wa, ref nl, ref nt);
            bool moved = Math.Abs(Left - nl) > 0.01 || Math.Abs(Top - nt) > 0.01;
            if (moved) { Left = nl; Top = nt; }
            if (_bubble.IsVisible && moved) _bubble.Reposition(_bubble.LastSize, PetRect(), wa);

            var want = TimeSpan.FromMilliseconds(IntervalFor(state, _hopT >= 0));
            if (_mode == PetMode.Zoomies) want = TimeSpan.FromMilliseconds(33);
            if (_timer.Interval != want) _timer.Interval = want;
        }
        void OnDown(object s, MouseButtonEventArgs e)
        {
            LeaveMode();
            _pressed = true;
            _dragStarted = false;
            _downScreen = PointToScreen(e.GetPosition(this));
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!_pressed || _dragStarted || e.LeftButton != MouseButtonState.Pressed) return;
            var p = PointToScreen(e.GetPosition(this));
            if (Math.Abs(p.X - _downScreen.X) + Math.Abs(p.Y - _downScreen.Y) < 6) return;
            _dragStarted = true;
            _doubleClick.Reset();
            _dragging = true;
            try { DragMove(); } catch { }
            _dragging = false;
            _pressed = false;
            InvalidateWorkArea();
            var wa = WorkArea();
            _beh.X = Math.Max(0, Math.Min(Left - wa.Left, Math.Max(0, wa.Width - Width)));
            _last = _clock.Elapsed;
            Place();
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            bool click = _pressed && !_dragStarted;
            _pressed = false;
            if (!click) return;
            TryRestartAnimation();
            if (_doubleClick.Click(_clock.Elapsed, _downScreen.X, _downScreen.Y)) Clicked?.Invoke();
        }
    }
}







