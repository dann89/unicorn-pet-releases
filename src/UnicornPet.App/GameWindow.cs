using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UnicornPet.Core;
using UnicornPet.Core.MiniGame;

namespace UnicornPet.App
{
    /// <summary>
    /// The mini game window (built in code, no XAML). Esc or losing focus hides it at once with the run paused; the x closes it.
    /// The per-frame callback (CompositionTarget.Rendering) runs only while the window is visible and the game needs it:
    /// SetLoop is the only code that touches it, and SyncLoop is the one rule that decides it.
    /// </summary>
    public sealed class GameWindow : Window
    {
        const double OuterWidth = GameTuning.FieldWidth + 2, OuterHeight = GameTuning.FieldHeight + 26 + 2;   // 1 px border, 26 px bar
        const int LineSteps = 150;   // a bubble line stays for 2.5 s of play

        static readonly Brush BarBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFB, 0xEE, 0xF5)));
        static readonly Brush BorderBrushSoft = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xB8, 0xCD)));
        static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x5C)));

        readonly Func<Settings> _settings;
        readonly Func<bool> _save;
        readonly Func<double> _rng;
        readonly RunnerGame _game;
        readonly GameCanvas _canvas = new GameCanvas();
        readonly TextBlock _scoreText = new TextBlock();
        readonly TextBlock _bestText = new TextBlock();
        readonly FixedStepClock _clock = new FixedStepClock();
        readonly Stopwatch _watch = Stopwatch.StartNew();
        readonly FailureGuard _guard = new FailureGuard(5);   // the app swallows unhandled exceptions; this stops a broken frame repeating 60 times a second
        // one-shot: arms hide-on-focus-loss once the window has been active for a moment (the opening menu has finished closing)
        readonly DispatcherTimer _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        GameArt _art;
        TimeSpan _last;
        bool _loopOn, _closing, _dragging, _armed, _placed, _jumpQueued, _duckKey;
        int _shownScore = -1, _shownBest = -1, _lineLeft;
        string _lastLine;

        public GameWindow(Func<Settings> settings, Func<bool> save, Func<double> rng01)
        {
            _settings = settings;
            _save = save;
            _rng = rng01;
            _game = new RunnerGame(rng01);

            Title = "Unicorn";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            Focusable = true;
            UseLayoutRounding = true;
            Background = Brushes.White;

            var close = new Button
            {
                Content = "x", Width = 26, Height = 26, Focusable = false, IsTabStop = false,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TextBrush, ToolTip = "Close",
            };
            close.Click += (s, e) => Close();
            _scoreText.Foreground = TextBrush;
            _scoreText.VerticalAlignment = VerticalAlignment.Center;
            _scoreText.Margin = new Thickness(8, 0, 0, 0);
            _bestText.Foreground = TextBrush;
            _bestText.VerticalAlignment = VerticalAlignment.Center;
            _bestText.Margin = new Thickness(0, 0, 12, 0);

            var bar = new DockPanel { Height = 26, Background = BarBrush, LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            DockPanel.SetDock(_bestText, Dock.Right);
            bar.Children.Add(_bestText);
            bar.Children.Add(_scoreText);
            bar.MouseLeftButtonDown += OnBarDown;   // the top bar is the drag handle

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(_canvas);
            Content = new Border { BorderThickness = new Thickness(1), BorderBrush = BorderBrushSoft, Child = root };

            _canvas.MouseLeftButtonDown += (s, e) => { Primary(); e.Handled = true; };
            Activated += OnActivated;
            Deactivated += OnDeactivated;
            _settle.Tick += OnSettled;

            _art = GameArt.Load(VisualTreeHelper.GetDpi(this).DpiScaleX);
            _canvas.Attach(_game, _art);
            UpdateScore();
        }

        /// <summary>True when a run is paused in this (hidden) window: the menu then says "Resume mini game".</summary>
        public bool HasPausedRun { get { return _game.Phase == GamePhase.Paused; } }

        /// <summary>Shows the window (placed above the pet the first time) and takes the keyboard.</summary>
        public void ShowGame(Rect pet, Rect workArea)
        {
            if (_closing) return;
            // not armed until the window has been active for a moment or gets a key or click: the menu may still be closing
            _armed = false;
            _settle.Stop();   // before Show(): Activated, which starts it, fires inside Show()/Activate()
            if (!_placed)
            {
                var p = GameWindowLayout.Compute(OuterWidth, OuterHeight, pet.Left, pet.Top, pet.Width, pet.Height,
                    workArea.Left, workArea.Top, workArea.Width, workArea.Height);
                Left = p.Left;
                Top = p.Top;
                _placed = true;
            }
            Show();
            Activate();
            if (IsActive && !_settle.IsEnabled) _settle.Start();   // already active: no Activated event comes
            ShowCardForPhase();
            SyncLoop();
        }

        // ---------- the loop ----------

        /// <summary>The only code that adds or removes the per-frame callback.</summary>
        void SetLoop(bool on)
        {
            if (on == _loopOn) return;
            _loopOn = on;
            if (on)
            {
                _clock.Reset();
                _last = _watch.Elapsed;
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        /// <summary>The one rule: on exactly when visible, not closing, not dragged, and running or counting down after a bump.</summary>
        void SyncLoop()
        {
            var phase = _game.Phase;
            SetLoop(IsVisible && !_closing && !_dragging && (phase == GamePhase.Running || (phase == GamePhase.Over && !_game.CanRestart)));
        }

        void OnRendering(object sender, EventArgs e)
        {
            _guard.Run(Frame);
            if (_guard.Tripped)
            {
                SetLoop(false);
                Close();
            }
        }

        void Frame()
        {
            var now = _watch.Elapsed;
            int steps = _clock.Advance(now - _last);
            _last = now;
            if (steps == 0) return;

            bool duck = _duckKey || (_canvas.IsMouseOver && Mouse.RightButton == MouseButtonState.Pressed);
            var events = GameEvents.None;
            for (int i = 0; i < steps; i++)
                events |= _game.Step(new GameInput { JumpPressed = _jumpQueued && i == 0, DuckHeld = duck });
            _jumpQueued = false;

            if ((events & GameEvents.Bumped) != 0) OnBump();
            else if ((events & GameEvents.Milestone) != 0) ShowLine(GameMoment.Milestone);
            else if (_lineLeft > 0 && _game.Phase == GamePhase.Running)
            {
                _lineLeft -= steps;
                if (_lineLeft <= 0) _canvas.SetLine(null);
            }

            UpdateScore();
            _canvas.InvalidateVisual();
            if (_game.Phase == GamePhase.Over && _game.CanRestart)
            {
                ShowCardForPhase();
                SyncLoop();   // turns the loop off: nothing moves on the game-over card
            }
        }

        void OnBump()
        {
            if (HighScore.Submit(_settings(), _game.Score))
            {
                TrySave();
                _canvas.Cheering = true;
                ShowLine(GameMoment.NewBest);
            }
            else
            {
                ShowLine(GameMoment.Bump);
            }
        }

        // ---------- input ----------

        /// <summary>Start, resume, play again, or (while running) jump. The input that starts or resumes never also jumps.</summary>
        void Primary()
        {
            if (_closing) return;
            var before = _game.Phase;
            if (!_game.PrimaryAction())
            {
                _jumpQueued = true;
                return;
            }
            if (_game.Phase == GamePhase.Running)
            {
                _canvas.SetCard(null, null);
                if (before != GamePhase.Paused)
                {
                    _canvas.Cheering = false;
                    ShowLine(GameMoment.Start);
                    UpdateScore();
                }
            }
            SyncLoop();
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            _armed = true;
            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            _armed = true;
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    HideGame();
                    break;
                case Key.Space:
                case Key.Up:
                case Key.W:
                case Key.Enter:
                    e.Handled = true;
                    if (!e.IsRepeat) Primary();   // a held key never repeats a jump
                    break;
                case Key.Down:
                case Key.S:
                    e.Handled = true;
                    _duckKey = true;
                    break;
            }
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
            if (e.Key == Key.Down || e.Key == Key.S)
            {
                _duckKey = false;
                e.Handled = true;
            }
        }

        void OnBarDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _dragging = true;
            _game.Pause();
            ShowCardForPhase();
            SyncLoop();
            try { DragMove(); } catch { }
            _dragging = false;
            SyncLoop();
        }

        // ---------- hide, focus, close ----------

        void OnActivated(object sender, EventArgs e)
        {
            Keyboard.Focus(this);
            if (!_armed && IsVisible && !_closing)
            {
                _settle.Stop();
                _settle.Start();   // (re)start: a focus bounce from the closing menu restarts the wait
            }
        }

        /// <summary>
        /// The opening has settled: arm only if the window is active now. It never hides or re-activates here (a menu bounce and her
        /// clicking away look the same at this point). If Windows refused to activate it, it stays on its still card with nothing
        /// running, and the next activation (her click on it) or key arms it.
        /// </summary>
        void OnSettled(object sender, EventArgs e)
        {
            _settle.Stop();
            if (IsVisible && !_closing && IsActive) _armed = true;
        }

        void OnDeactivated(object sender, EventArgs e)
        {
            // never hide from inside the activation change itself; and only once armed (not while the opening menu is still closing)
            if (_armed && IsVisible && !_closing) Dispatcher.BeginInvoke(new Action(HideIfStillInactive));
        }

        void HideIfStillInactive()
        {
            if (!IsActive) HideGame();
        }

        /// <summary>Esc and focus loss: pause, hide, stop the loop. The run stays in memory for "Resume mini game".</summary>
        void HideGame()
        {
            if (_closing) return;
            _settle.Stop();
            _game.Pause();
            _jumpQueued = false;
            _duckKey = false;
            ShowCardForPhase();
            Hide();
            SyncLoop();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NoActivate.ApplyToolWindow(this);   // no taskbar button, not in Alt-Tab; it still takes focus
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (_closing) return;
            _art = GameArt.Load(newDpi.DpiScaleX);
            _canvas.Attach(_game, _art);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _closing = true;
            _settle.Stop();
            SetLoop(false);
            if (_game.Phase != GamePhase.Ready && HighScore.Submit(_settings(), _game.Score)) TrySave();
        }

        protected override void OnClosed(EventArgs e)
        {
            _settle.Stop();
            SetLoop(false);
            _canvas.Attach(null, null);
            _art = null;
            base.OnClosed(e);
        }

        // ---------- what is shown ----------

        void ShowCardForPhase()
        {
            switch (_game.Phase)
            {
                case GamePhase.Ready:
                    _canvas.SetCard("Unicorn Dash", "Space or click to start · Esc to hide");
                    break;
                case GamePhase.Paused:
                    _canvas.SetCard("Paused", "Space or click to carry on");
                    break;
                case GamePhase.Over:
                    if (_game.CanRestart)
                        _canvas.SetCard("Score " + Number(_game.Score) + "   Best " + Number(_settings().GameHighScore), "Space or click to go again");
                    else
                        _canvas.SetCard(null, null);   // the bump is still playing out
                    break;
                default:
                    _canvas.SetCard(null, null);
                    break;
            }
        }

        void ShowLine(GameMoment moment)
        {
            _lastLine = GameLines.Pick(moment, _settings().CheekyMode, _rng, _lastLine);
            _canvas.SetLine(_lastLine);
            _lineLeft = LineSteps;
        }

        void UpdateScore()
        {
            int score = _game.Score;
            if (score != _shownScore)
            {
                _shownScore = score;
                _scoreText.Text = "Score " + Number(score);
            }
            int best = _settings().GameHighScore;
            if (best != _shownBest)
            {
                _shownBest = best;
                _bestText.Text = "Best " + Number(best);
            }
        }

        void TrySave()
        {
            try { _save(); } catch { }   // a failed save is ignored; the best stays in memory for this session
        }

        static string Number(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
    }
}
