using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UnicornPet.Core;

namespace UnicornPet.App
{
    /// <summary>What the Settings window needs from the pet for the Messages group (work-safe build only).</summary>
    public sealed class MessagesUi
    {
        public Func<IReadOnlyList<MessageSection>> Sections;
        public Func<string> Status;
        public Func<System.Threading.Tasks.Task> Reload;
    }

    public partial class SettingsWindow : Window
    {
        static SettingsWindow _open;

        readonly Settings _settings;
        readonly string _fileName, _detected;
        readonly Func<bool, bool> _onSaved;   // returns whether the settings reached disk
        readonly Point _anchor;

        public SettingsWindow(Settings settings, string fileName, string detectedName, Func<bool, bool> onSaved, Point anchorScreenDips)
        {
            InitializeComponent();
            _settings = settings; _fileName = fileName; _detected = detectedName; _onSaved = onSaved; _anchor = anchorScreenDips;
            NameBox.Text = settings.CustomName ?? "";
            PetNameBox.Text = settings.PetName ?? "";
            Title = PetNameText.SettingsTitle(settings.PetName);
            SizeSmallButton.IsChecked = settings.PetSize == PetSize.Small;
            SizeMediumButton.IsChecked = settings.PetSize == PetSize.Medium;
            SizeLargeButton.IsChecked = settings.PetSize != PetSize.Small && settings.PetSize != PetSize.Medium;
            SparklesBox.IsChecked = settings.Sparkles;
            IdlePoopBox.IsChecked = settings.IdlePoop;
            IdlePoopBox.Visibility = Visibility.Visible;
            IdlePoopHint.Visibility = Visibility.Visible;
            RandomZoomiesBox.IsChecked = settings.RandomZoomies;
            RandomZoomiesBox.Visibility = Visibility.Visible;
            UseWindowsBox.IsChecked = settings.UseWindowsName;
            CheekyBox.IsChecked = settings.CheekyMode;
            ScheduledBox.IsChecked = settings.ScheduledEnabled;
#if !WORKSAFE
            StartupBox.IsChecked = settings.StartWithWindows;
#endif
#if WORKSAFE
            StartupBox.Visibility = Visibility.Collapsed;
            FetchBox.IsChecked = settings.FetchMessages;
            FetchBox.Visibility = Visibility.Visible;
            FetchHint.Visibility = Visibility.Visible;
            MsgSeparator.Visibility = Visibility.Visible;
            MsgHeader.Visibility = Visibility.Visible;
#endif
            NameBox.TextChanged += (s, e) => Refresh();
            PetNameBox.TextChanged += (s, e) => Refresh();
            UseWindowsBox.Click += (s, e) => Refresh();
            SaveButton.Click += (s, e) => { string err; TrySave(out err); };
            CancelButton.Click += (s, e) => Close();
            Loaded += (s, e) => { CenterOnMonitor(); NameBox.Focus(); NameBox.SelectAll(); };
            Closed += (s, e) => { if (_open == this) _open = null; };
            Refresh();
        }

        public static void ShowSingle(Func<SettingsWindow> create)
        {
            if (_open != null) { _open.Activate(); return; }
            _open = create();
            _open.Show();
            _open.Activate();
        }

        public string Preview { get { return PreviewText.Text; } }
        public string Error { get { return ErrorText.Visibility == Visibility.Visible ? ErrorText.Text : ""; } }
        public TextBox NameInput { get { return NameBox; } }
        public CheckBox UseWindows { get { return UseWindowsBox; } }

        public void RefreshPreview() { Refresh(); }

        const string NameRuleText = "Use letters, spaces, hyphens or apostrophes only.";

        void Refresh()
        {
            string normalized;
            bool ok = NameRules.TryNormalize(NameBox.Text, out normalized);
            string name = "";
            if (!ok)
            {
                ErrorText.Text = NameRuleText;
                ErrorText.Visibility = Visibility.Visible;
                PreviewText.Text = "";
            }
            else
            {
                ErrorText.Visibility = Visibility.Collapsed;
                var probe = new Settings { CustomName = normalized, UseWindowsName = UseWindowsBox.IsChecked == true };
                name = NameProvider.Resolve(probe, _fileName, _detected);
                PreviewText.Text = "Preview: " + MessagePicker.Substitute("Good morning, {name}!", name);
            }
            RefreshPetName(name);
        }

        /// <summary>Live check and preview of the unicorn's own name (same rules as the person's name); the title follows it.</summary>
        void RefreshPetName(string userName)
        {
            string pet;
            if (!NameRules.TryNormalize(PetNameBox.Text, out pet))
            {
                PetErrorText.Text = NameRuleText;
                PetErrorText.Visibility = Visibility.Visible;
                PetPreviewText.Text = "";
                return;
            }
            PetErrorText.Visibility = Visibility.Collapsed;
            PetPreviewText.Text = pet.Length == 0 ? "No name yet (optional)." : "Preview: " + PetNameText.Intro(userName, pet);
            Title = PetNameText.SettingsTitle(pet);
        }

        PetSize ChosenSize()
        {
            if (SizeSmallButton.IsChecked == true) return PetSize.Small;
            if (SizeMediumButton.IsChecked == true) return PetSize.Medium;
            return PetSize.Large;
        }

        public bool TrySave(out string error)
        {
            string normalized, petName;
            if (!NameRules.TryNormalize(NameBox.Text, out normalized))
            {
                Refresh();
                error = ErrorText.Text;
                return false;
            }
            if (!NameRules.TryNormalize(PetNameBox.Text, out petName))
            {
                Refresh();
                error = PetErrorText.Text;
                return false;
            }
            // the message source is checked before anything is applied: a bad path or address refuses the whole Save
            var msgKind = _settings.MessageSource;
            string msgFile = _settings.MessageFile ?? "", msgUrl = _settings.MessageUrl ?? "";
            IReadOnlyList<string> sectionsOff = _settings.SectionsOff;
            if (_msgUi != null)
            {
                msgKind = ChosenSource();
                msgFile = CustomSourceRules.CleanPath(MsgFileBox.Text);
                msgUrl = (MsgUrlBox.Text ?? "").Trim();
                var msgError = CustomSourceRules.FormError(msgKind, msgFile, msgUrl);
                if (msgError.Length > 0)
                {
                    MsgErrorText.Text = msgError;
                    MsgErrorText.Visibility = Visibility.Visible;
                    MsgErrorText.BringIntoView();
                    error = msgError;
                    return false;
                }
                // a box that is not in use keeps its saved value unless it is empty or usable
                if (msgFile.Length > 0 && !CustomSourceRules.IsAllowedFile(msgFile)) msgFile = _settings.MessageFile ?? "";
                if (msgUrl.Length > 0 && !CustomSourceRules.IsAllowedUrl(msgUrl)) msgUrl = _settings.MessageUrl ?? "";
                sectionsOff = SectionsOffFromBoxes();
            }
            bool startupChanged = false;
            // the change takes effect in memory at once (an opt-out must not wait for the disk); then the save is attempted
            var message = SettingsCommit.Run(() =>
            {
                _settings.CustomName = normalized;
                _settings.UseWindowsName = UseWindowsBox.IsChecked == true;
                _settings.CheekyMode = CheekyBox.IsChecked == true;
                _settings.ScheduledEnabled = ScheduledBox.IsChecked == true;
                _settings.PetName = petName;
                _settings.PetSize = ChosenSize();
                _settings.Sparkles = SparklesBox.IsChecked == true;
                _settings.IdlePoop = IdlePoopBox.IsChecked == true;
                _settings.RandomZoomies = RandomZoomiesBox.IsChecked == true;
                if (_msgUi != null)
                {
                    if (!string.Equals(_settings.MessageUrl ?? "", msgUrl, StringComparison.Ordinal)) _settings.LastCustomFetch = null;   // a new address may be read at once
                    _settings.MessageSource = msgKind;
                    _settings.MessageFile = msgFile;
                    _settings.MessageUrl = msgUrl;
                    _settings.SectionsOff = sectionsOff;
                }
#if WORKSAFE
                _settings.FetchMessages = FetchBox.IsChecked == true;
#else
                startupChanged = _settings.StartWithWindows != (StartupBox.IsChecked == true);
                _settings.StartWithWindows = StartupBox.IsChecked == true;
#endif
            }, () => _onSaved(startupChanged));
            if (message.Length > 0)
            {
                // stay open, say so in plain text, and do not pretend anything was kept
                ErrorText.Text = message;
                ErrorText.Visibility = Visibility.Visible;
                ErrorText.BringIntoView();   // the options may be scrolled
                error = message;
                return false;
            }
            error = "";
            if (IsVisible) Close();
            return true;
        }

        // ---------- Messages (work-safe build: attached by the pet window) ----------

        MessagesUi _msgUi;
        bool _msgReloading;

        /// <summary>Shows the Messages group: the source, its path or address, Reload now and one tick box per section.</summary>
        public void AttachMessages(MessagesUi ui)
        {
            if (ui == null) return;
            _msgUi = ui;
            MsgSeparator.Visibility = Visibility.Visible;
            MsgHeader.Visibility = Visibility.Visible;
            MsgSourcePanel.Visibility = Visibility.Visible;
            MsgReloadRow.Visibility = Visibility.Visible;
            MsgBuiltInButton.Visibility = Visibility.Visible;
            MsgFileButton.Visibility = Visibility.Visible;
            MsgWebButton.Visibility = Visibility.Visible;
            MsgReloadButton.Visibility = Visibility.Visible;
            MsgStatusText.Visibility = Visibility.Visible;
            MsgBuiltInButton.IsChecked = _settings.MessageSource == MessageSourceKind.BuiltIn;
            MsgFileButton.IsChecked = _settings.MessageSource == MessageSourceKind.File;
            MsgWebButton.IsChecked = _settings.MessageSource == MessageSourceKind.Web;
            MsgFileBox.Text = _settings.MessageFile ?? "";
            MsgUrlBox.Text = _settings.MessageUrl ?? "";

            MsgBuiltInButton.Checked += (s, e) => UpdateMessageRows();
            MsgFileButton.Checked += (s, e) => UpdateMessageRows();
            MsgWebButton.Checked += (s, e) => UpdateMessageRows();
            MsgFileBox.TextChanged += (s, e) => UpdateMessageRows();
            MsgUrlBox.TextChanged += (s, e) => UpdateMessageRows();
            MsgReloadButton.Click += (s, e) => ReloadMessages();

            // a file dropped onto the path box puts its path there (the text box's own drop handling would refuse a file)
            MsgFileBox.AllowDrop = true;
            DragEventHandler over = (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            MsgFileBox.PreviewDragEnter += over;
            MsgFileBox.PreviewDragOver += over;
            MsgFileBox.PreviewDrop += (s, e) =>
            {
                try
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0) MsgFileBox.Text = files[0];
                }
                catch { }
                e.Handled = true;
            };

            BuildSectionBoxes();
            UpdateMessageRows();
        }

        MessageSourceKind ChosenSource()
        {
            if (MsgFileButton.IsChecked == true) return MessageSourceKind.File;
            if (MsgWebButton.IsChecked == true) return MessageSourceKind.Web;
            return MessageSourceKind.BuiltIn;
        }

        /// <summary>Shows the row of the chosen source. Reload now uses the SAVED settings, so it is enabled only while the boxes equal them.</summary>
        void UpdateMessageRows()
        {
            if (_msgUi == null) return;
            var kind = ChosenSource();
            var file = kind == MessageSourceKind.File ? Visibility.Visible : Visibility.Collapsed;
            var web = kind == MessageSourceKind.Web ? Visibility.Visible : Visibility.Collapsed;
            MsgFileBox.Visibility = file; MsgFileHint.Visibility = file;
            MsgUrlBox.Visibility = web; MsgWebHint.Visibility = web;
            MsgErrorText.Visibility = Visibility.Collapsed;
            bool asSaved = kind == _settings.MessageSource
                && string.Equals(CustomSourceRules.CleanPath(MsgFileBox.Text), _settings.MessageFile ?? "", StringComparison.Ordinal)
                && string.Equals((MsgUrlBox.Text ?? "").Trim(), _settings.MessageUrl ?? "", StringComparison.Ordinal);
            MsgReloadButton.IsEnabled = kind != MessageSourceKind.BuiltIn && asSaved && !_msgReloading;
            string status = "";
            if (!asSaved) status = "Save first to use the new source.";
            else { try { status = _msgUi.Status() ?? ""; } catch { } }
            MsgStatusText.Text = status;
        }

        async void ReloadMessages()
        {
            if (_msgUi == null || _msgReloading) return;
            _msgReloading = true;
            try
            {
                MsgReloadButton.IsEnabled = false;
                MsgStatusText.Text = "Loading\u2026";
                await _msgUi.Reload();
            }
            catch { }
            finally
            {
                _msgReloading = false;
                try { BuildSectionBoxes(); UpdateMessageRows(); } catch { }
            }
        }

        /// <summary>One tick box per section, ticked unless its id is unticked in the settings (or in the box shown before a rebuild).</summary>
        void BuildSectionBoxes()
        {
            var shown = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var c in MsgSectionsPanel.Children.OfType<CheckBox>())
            {
                var id = c.Tag as string;
                if (id != null) shown[id] = c.IsChecked == true;
            }
            var off = new HashSet<string>(_settings.SectionsOff ?? new string[0], StringComparer.Ordinal);
            IReadOnlyList<MessageSection> sections = null;
            try { sections = _msgUi.Sections(); } catch { }
            if (sections == null) sections = new MessageSection[0];
            MsgSectionsPanel.Children.Clear();
            foreach (var s in sections)
            {
                bool on;
                if (!shown.TryGetValue(s.Id, out on)) on = !off.Contains(s.Id);
                // the title goes in a TextBlock so an underscore in it is shown, not taken as an access key
                MsgSectionsPanel.Children.Add(new CheckBox
                {
                    Content = new TextBlock { Text = s.Title + " (" + s.Lines.Count + ")", TextWrapping = TextWrapping.Wrap },
                    Tag = s.Id,
                    IsChecked = on,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            var vis = sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MsgSectionsHeader.Visibility = vis;
            MsgSectionsPanel.Visibility = vis;
        }

        /// <summary>The unticked boxes, plus stored ids that have no box right now (a section that is missing for a while keeps its untick). At most 32.</summary>
        IReadOnlyList<string> SectionsOffFromBoxes()
        {
            var off = new List<string>();
            var shown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in MsgSectionsPanel.Children.OfType<CheckBox>())
            {
                var id = c.Tag as string;
                if (id == null) continue;
                shown.Add(id);
                if (c.IsChecked != true && !off.Contains(id)) off.Add(id);
            }
            foreach (var id in _settings.SectionsOff ?? new string[0])
                if (!shown.Contains(id) && !off.Contains(id)) off.Add(id);
            return off.Take(SettingsStore.MaxSectionsOff).ToList();
        }

        // The window is never taller than the work area of its monitor (1280 x 720 at 150 % leaves about 480 DIPs): the options
        // then scroll and Save / Cancel stay pinned at the bottom. Work areas are in pixels; k converts them to DIPs.
        static Rect InDips(System.Windows.Forms.Screen scr, double k)
        {
            var wa = scr.WorkingArea;
            return new Rect(wa.X / k, wa.Y / k, wa.Width / k, wa.Height / k);
        }

        Rect AnchorWorkArea()
        {
            var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_anchor.X, (int)_anchor.Y));
            return InDips(scr, System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { MaxHeight = AnchorWorkArea().Height; } catch { }   // before the first layout, so Loaded already sees the capped height
        }

        void CenterOnMonitor()
        {
            try
            {
                var wa = AnchorWorkArea();
                MaxHeight = wa.Height;
                double height = Math.Min(ActualHeight, wa.Height);
                Left = wa.X + (wa.Width - ActualWidth) / 2;
                Top = wa.Y + (wa.Height - height) / 2;
            }
            catch { }
        }

        // moved to a monitor with another scale (when placed, or dragged): cap and keep on screen with that monitor's work area
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(FitToMonitor));   // after WPF has applied the new size and position
        }

        void FitToMonitor()
        {
            try
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;   // closed meanwhile
                var wa = InDips(System.Windows.Forms.Screen.FromHandle(h), System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
                MaxHeight = wa.Height;
                UpdateLayout();
                double height = Math.Min(ActualHeight, wa.Height);
                if (Top + height > wa.Bottom) Top = wa.Bottom - height;
                if (Top < wa.Top) Top = wa.Top;
            }
            catch { }
        }
    }
}
